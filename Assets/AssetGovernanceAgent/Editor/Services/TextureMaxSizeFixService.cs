using UnityEngine;
using System;
using AssetGovernanceAgent.Editor.Models;
using AssetGovernanceAgent.Editor.Rules;
using UnityEditor;

namespace AssetGovernanceAgent.Editor.Services
{
    public sealed class TextureMaxSizeFixService
    {
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

        private static TextureMaxSizeFixResult CreateFailedResult(
            TextureMaxSizeFixRequest request, // 失败对应的请求。
            string errorCode, // 稳定的机器错误码。
            string message, // 面向开发者的错误说明。
            bool hasObservedMaxSize = false, // 是否读取到Unity实际值。
            int observedMaxSize = 0) // 读取到的实际Max Size。
        {
            return new TextureMaxSizeFixResult(
                request,
                TextureMaxSizeFixStatus.Failed,
                hasObservedMaxSize,
                observedMaxSize,
                message,
                errorCode);
        }
    }
}