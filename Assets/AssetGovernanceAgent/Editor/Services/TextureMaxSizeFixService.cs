using UnityEngine;
using System;
using AssetGovernanceAgent.Editor.Models;
using AssetGovernanceAgent.Editor.Rules;
using UnityEditor;
using System.Collections.Generic;
using AssetGovernanceAgent.Editor.Approvals;

namespace AssetGovernanceAgent.Editor.Services
{
    public sealed class TextureMaxSizeFixService
    {
        private static readonly HashSet<string>
            WriteAttemptedOperationIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 对修复请求执行只读预检查。
        ///
        /// 检查通过后返回AwaitingApproval；
        /// 资源已经达到目标时返回NoChange；
        /// 状态不匹配时返回Failed。
        /// </summary>
        /// <param name="request">
        /// 必填。Agent或其他上层生成的强类型修复请求。
        /// </param>
        /// <param name="ruleSet">
        /// 必填。当前Unity项目实际使用的Texture规则。
        /// </param>
        /// <returns>
        /// 结构化检查结果。该方法不会修改任何Unity资源。
        /// </returns>
        public TextureMaxSizeFixResult DryRun(
            TextureMaxSizeFixRequest request,
            TextureGovernanceRuleSet ruleSet)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request), "Dry Run请求不能为空。");
            }

            if (ruleSet == null)
            {
                throw new ArgumentNullException(nameof(ruleSet), "规则集不能为空。");
            }

            request.Validate();


            if (!string.Equals(request.RuleId, TextureRuleIds.MaxTextureSize, StringComparison.Ordinal))
            {
                return CreateFailedResult(
                    request, // 当前请求。
                    "RULE_ID_MISMATCH", // 机器可读错误码。
                    "请求的RuleId不是Texture Max Size规则。");
            }


            if (!string.Equals(request.RuleVersion, ruleSet.RuleVersion, StringComparison.Ordinal))
            {
                return CreateFailedResult(
                    request,
                    "RULE_VERSION_MISMATCH",
                    $"请求规则版本为{request.RuleVersion}，" +
                    $"当前规则版本为{ruleSet.RuleVersion}。");
            }

            string expectedIssueId =
                $"{TextureRuleIds.MaxTextureSize}:{request.AssetGuid}";

            if (!string.Equals(request.IssueId, expectedIssueId, StringComparison.Ordinal))
            {
                return CreateFailedResult(
                    request,
                    "ISSUE_ID_MISMATCH",
                    "IssueId与RuleId、AssetGuid无法对应。");
            }

            // 修复目标可以小于规则上限，
            // 但绝对不能大于当前规则允许的最大值。
            if (request.TargetMaxSize > ruleSet.MaxTextureSize)
            {
                return CreateFailedResult(
                    request,
                    "TARGET_EXCEEDS_RULE",
                    $"目标值{request.TargetMaxSize}超过规则上限" +
                    $"{ruleSet.MaxTextureSize}。");
            }


            if (request.ExpectedCurrentMaxSize <= ruleSet.MaxTextureSize)
            {
                return CreateFailedResult(
                    request,
                    "REQUEST_NOT_VIOLATION",
                    "请求记录的当前值没有违反Max Size规则。");
            }

            string resolvedAssetPath = AssetDatabase.GUIDToAssetPath(request.AssetGuid);

            if (string.IsNullOrWhiteSpace(resolvedAssetPath))
            {
                return CreateFailedResult(
                    request,
                    "ASSET_NOT_FOUND",
                    "无法通过AssetGuid找到Unity资源。");
            }

            resolvedAssetPath =
                resolvedAssetPath.Replace('\\', '/');

            if (!string.Equals(resolvedAssetPath, request.AssetPath, StringComparison.Ordinal))
            {
                return CreateFailedResult(
                    request,
                    "ASSET_PATH_CHANGED",
                    $"资源路径已经从{request.AssetPath}变为" +
                    $"{resolvedAssetPath}，请重新扫描。");
            }

            TextureImporter importer = AssetImporter.GetAtPath(resolvedAssetPath) as TextureImporter;
            ;
            if (importer == null)
            {
                return CreateFailedResult(
                    request,
                    "TEXTURE_IMPORTER_NOT_FOUND",
                    "目标资源无法取得TextureImporter。");
            }

            int observedMaxSize = importer.maxTextureSize;

            if (observedMaxSize == request.TargetMaxSize)
            {
                return new TextureMaxSizeFixResult(
                    request, // 原始修复请求。
                    TextureMaxSizeFixStatus.NoChange, // 无需修改。
                    true, // 已成功读取实际值。
                    observedMaxSize, // 当前实际Max Size。
                    "资源已经符合目标值，无需重复修改。");
            }

            if (observedMaxSize != request.ExpectedCurrentMaxSize)
            {
                return CreateFailedResult(
                    request,
                    "RESOURCE_STATE_CHANGED",
                    $"扫描时Max Size为" +
                    $"{request.ExpectedCurrentMaxSize}，" +
                    $"当前实际值为{observedMaxSize}。" +
                    "请重新扫描后再生成修复计划。",
                    true, // Unity已经读取到实际值。
                    observedMaxSize); // 当前实际Max Size。
            }

            // 所有只读检查均通过。
            // 这里只返回等待审批，绝对不修改Importer。
            return new TextureMaxSizeFixResult(
                request, // 已通过校验的修复请求。
                TextureMaxSizeFixStatus.AwaitingApproval, // 等待本地审批。
                true, // 已经读取到Unity实际值。
                observedMaxSize, // 执行前实际Max Size。
                "Dry Run通过，等待Unity本地批准。");
        }

        /// <summary>
        /// 执行已经获得Unity本地批准的Max Size修复。
        ///
        /// 必须在Unity Editor主线程同步调用。
        /// 先核对本地授权，再检查实时资源状态；
        /// 写入和重新导入后，通过DryRun再次检查该资源。
        /// </summary>
        /// <param name="request">
        /// 必填。必须与本地已批准记录中的完整请求一致。
        /// </param>
        /// <param name="ruleSet">
        /// 必填。由Unity本地提供的当前规则，不能信任外部传入的规则内容。
        /// </param>
        /// <returns>
        /// Applied：本次修改后复检通过。
        /// NoChange：调用时资源已经达到目标，没有再次写入。
        /// Failed：授权、状态、执行或复检失败。
        /// </returns>
        internal TextureMaxSizeFixResult ExecuteApproved(
            TextureMaxSizeFixRequest request, // 必填。准备执行的修复请求。
            TextureGovernanceRuleSet ruleSet) // 必填。Unity当前实际使用的规则。
        {
            if (request == null)
            {
                throw new ArgumentNullException(
                    nameof(request), // 异常对应的参数。
                    "执行请求不能为空。"); // 错误说明。
            }

            if (ruleSet == null)
            {
                throw new ArgumentNullException(
                    nameof(ruleSet), // 异常对应的参数。
                    "执行规则不能为空。"); // 错误说明。
            }

            // 请求格式不合法时直接抛异常。
            // 与DryRun保持一致，后续由接口层统一处理这类输入错误。
            request.Validate();

            // 此方法内部同时检查Approved状态和完整请求匹配。
            // ExecuteApproved绝不能自行调用Approve。
            if (!TextureMaxSizeFixApprovalStore.TryGetApproved(
                    request, // 当前准备执行的完整请求。
                    out _)) // 这里只需要授权检查结果。
            {
                return CreateFailedResult(
                    request, // 当前请求。
                    "APPROVAL_REQUIRED_OR_MISMATCH", // 未批准或参数不匹配。
                    "没有找到与当前请求完全一致的本地批准记录。");
            }

            // 之前通过DryRun，不代表此刻仍然允许修改。
            // 用户审批期间，资源、路径或规则都可能发生变化。
            TextureMaxSizeFixResult precheck = DryRun(
                request, // 当前请求。
                ruleSet); // 当前本地规则。

            if (precheck.Status == TextureMaxSizeFixStatus.NoChange ||
                precheck.Status == TextureMaxSizeFixStatus.Failed)
            {
                // 已达到目标无需再写；预检查失败也不能继续写。
                return precheck;
            }

            // 采用明确的允许状态，避免未来新增状态后意外进入写入。
            if (precheck.Status != TextureMaxSizeFixStatus.AwaitingApproval)
            {
                return CreateFailedResult(
                    request, // 当前请求。
                    "UNEXPECTED_PRECHECK_STATUS", // 非预期的检查状态。
                    "当前预检查状态不允许进入资源修改。");
            }

            TextureImporter importer =
                AssetImporter.GetAtPath(request.AssetPath) as TextureImporter;

            if (importer == null)
            {
                return CreateFailedResult(
                    request, // 当前请求。
                    "TEXTURE_IMPORTER_NOT_FOUND", // 无法取得纹理导入器。
                    "执行前无法取得TextureImporter。");
            }

            // 紧邻写入再次检查当前值，拒绝已经变化的资源。
            int currentMaxSize = importer.maxTextureSize;

            if (currentMaxSize != request.ExpectedCurrentMaxSize)
            {
                return CreateFailedResult(
                    request, // 当前请求。
                    "RESOURCE_STATE_CHANGED", // 资源配置与快照不一致。
                    "执行前Max Size已变化，请重新扫描。",
                    true, // 已读取到实际值。
                    currentMaxSize); // 当前实际Max Size。
            }

            // Add返回false表示这个编号已经尝试过写入。
            // 标记放在写入前，即使重新导入失败，也不会自动重试写入。
            if (!WriteAttemptedOperationIds.Add(request.OperationId))
            {
                return CreateFailedResult(
                    request, // 当前请求。
                    "OPERATION_ALREADY_ATTEMPTED", // 旧操作不能重复写入。
                    "该操作已尝试过写入。请检查资源状态；需要再次修改时，" +
                    "创建新OperationId并重新预检查、审批。",
                    true, // 本次已读取到实际值。
                    currentMaxSize); // 本次没有再次写入。
            }

            try
            {
                // 正式写入点：仅修改已经批准的Max Size配置。
                importer.maxTextureSize = request.TargetMaxSize;

                // 保存当前Importer配置，并重新导入该资源。
                importer.SaveAndReimport();

                // 复用确定性检查：重新定位资源、读取Importer，
                // 并重新检查规则版本、路径和目标值。
                TextureMaxSizeFixResult postcheck = DryRun(
                    request, // 刚执行的请求。
                    ruleSet); // 当前规则。

                if (postcheck.Status != TextureMaxSizeFixStatus.NoChange)
                {
                    return CreateFailedResult(
                        request, // 当前请求。
                        "POSTCHECK_FAILED", // 写入后没有确认目标达成。
                        $"修改后复检未通过：{postcheck.Message}",
                        postcheck.HasObservedMaxSize, // 复检是否读取到实际值。
                        postcheck.ObservedMaxSize, // 复检观察值。
                        true); // 已经尝试过写入，不能声称资源完全未动。
                }

                // 此处返回Applied，因为本次确实执行了写入，
                // 并且复检确认资源已达到目标。
                return new TextureMaxSizeFixResult(
                    request, // 当前修复请求。
                    TextureMaxSizeFixStatus.Applied, // 修改成功且复检通过。
                    true, // 已读取到修改后的实际值。
                    postcheck.ObservedMaxSize, // 复检确认的实际Max Size。
                    "Max Size修改完成，单资源复检通过。",
                    errorCode: "", // 成功结果没有错误码。
                    writeAttempted: true); // 本次进入过写入阶段。
            }
            catch (Exception exception)
            {
                // 写入或复检异常时，资源可能已部分改变。
                // 不使用写入前的值冒充当前状态，也不自动重试。
                return CreateFailedResult(
                    request, // 当前请求。
                    "EXECUTION_OR_VERIFICATION_FAILED", // 写入或复检异常。
                    $"执行或复检发生异常：{exception.Message}。" +
                    "资源可能已改变，请重新扫描确认。",
                    false, // 没有可靠的异常后观察值。
                    0, // 无观察值时使用占位值，调用方必须忽略。
                    true); // 本次已进入写入阶段。
            }
        }


        private static TextureMaxSizeFixResult CreateFailedResult(
            TextureMaxSizeFixRequest request, // 失败对应的请求。
            string errorCode, // 稳定的机器错误码。
            string message, // 面向开发者的错误说明。
            bool hasObservedMaxSize = false, // 是否读取到Unity实际值。
            int observedMaxSize = 0,
            bool writeAttempted = false) // 读取到的实际Max Size。
        {
            return new TextureMaxSizeFixResult(
                request,
                TextureMaxSizeFixStatus.Failed,
                hasObservedMaxSize,
                observedMaxSize,
                message,
                errorCode,
                writeAttempted);
        }
    }
}