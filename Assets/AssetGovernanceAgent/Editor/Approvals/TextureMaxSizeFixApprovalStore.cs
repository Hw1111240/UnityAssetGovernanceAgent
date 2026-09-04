using System;
using System.Collections.Generic;
using AssetGovernanceAgent.Editor.Models;

namespace AssetGovernanceAgent.Editor.Approvals
{
    /// <summary>
    /// 当前Unity Editor会话内的Texture Max Size审批记录仓库。
    ///
    /// 只保存内存数据，不写入资源、不写入规则资产。
    /// Unity重启或脚本域重载后记录会自动丢失，
    /// 后续必须重新执行Dry Run和本地审批。
    /// </summary>
    internal static class TextureMaxSizeFixApprovalStore
    {
        /// <summary>
        /// 以OperationId作为索引保存审批记录。
        /// 同一个操作编号只能对应一条经过精确匹配的修复请求。
        /// </summary>
        private static readonly Dictionary<
            string,
            TextureMaxSizeFixApprovalRecord> RecordsByOperationId =
            new Dictionary<
                string,
                TextureMaxSizeFixApprovalRecord>();

        /// <summary>
        /// 为Dry Run通过的修复请求登记待审批记录。
        /// 重复登记同一条完全相同的请求时，返回原记录，不重复创建。
        /// </summary>
        /// <param name="dryRunResult">
        /// 必填。只能是Dry Run返回的AwaitingApproval结果。
        /// </param>
        /// <returns>当前请求对应的待审批或已有审批记录。</returns>
        internal static TextureMaxSizeFixApprovalRecord RegisterPending(
            TextureMaxSizeFixResult dryRunResult) // Dry Run返回的结构化结果。
        {
            if (dryRunResult == null)
            {
                throw new ArgumentNullException(
                    nameof(dryRunResult),
                    "Dry Run结果不能为空。");
            }

            if (dryRunResult.Status !=
                TextureMaxSizeFixStatus.AwaitingApproval)
            {
                throw new InvalidOperationException(
                    "只有AwaitingApproval状态才能登记本地审批。");
            }

            if (!dryRunResult.HasObservedMaxSize)
            {
                throw new InvalidOperationException(
                    "Dry Run未读取到实际Max Size，不能登记审批。");
            }

            TextureMaxSizeFixRequest request =
                dryRunResult.Request;

            if (request == null)
            {
                throw new InvalidOperationException(
                    "Dry Run结果没有关联修复请求。");
            }

            request.Validate();

            if (dryRunResult.ObservedMaxSize !=
                request.ExpectedCurrentMaxSize)
            {
                throw new InvalidOperationException(
                    "Dry Run观察值与请求快照不一致，不能登记审批。");
            }

            if (RecordsByOperationId.TryGetValue(
                    request.OperationId,
                    out TextureMaxSizeFixApprovalRecord existingRecord))
            {
                if (!existingRecord.Matches(request))
                {
                    throw new InvalidOperationException(
                        "相同OperationId对应了不同修复请求，" +
                        "拒绝覆盖已有审批记录。");
                }

                return existingRecord;
            }

            TextureMaxSizeFixApprovalRecord pendingRecord =
                TextureMaxSizeFixApprovalRecord.CreatePending(
                    request);

            RecordsByOperationId.Add(
                request.OperationId,
                pendingRecord);

            return pendingRecord;
        }

        /// <summary>
        /// 由Unity本地界面批准一条待审批请求。
        /// 重复批准同一请求时返回已有批准记录，不重复创建。
        /// </summary>
        /// <param name="request">
        /// 必填。需要批准的原始修复请求，必须与待审批记录完全一致。
        /// </param>
        /// <param name="note">
        /// 可选。用户批准时留下的备注。
        /// </param>
        /// <returns>最终的已批准记录。</returns>
        internal static TextureMaxSizeFixApprovalRecord Approve(
            TextureMaxSizeFixRequest request, // 用户准备批准的修复请求。
            string note = "") // 用户可选审批备注。
        {
            TextureMaxSizeFixApprovalRecord record =
                GetMatchingRecordOrThrow(request);

            if (record.Decision ==
                TextureMaxSizeApprovalDecision.Approved)
            {
                return record;
            }

            if (record.Decision ==
                TextureMaxSizeApprovalDecision.Rejected)
            {
                throw new InvalidOperationException(
                    "该请求已经被拒绝，不能再次批准。" +
                    "请重新Dry Run后生成新的请求。");
            }

            TextureMaxSizeFixApprovalRecord approvedRecord =
                TextureMaxSizeFixApprovalRecord.CreateApproved(
                    request,
                    note);

            RecordsByOperationId[request.OperationId] =
                approvedRecord;

            return approvedRecord;
        }

        /// <summary>
        /// 由Unity本地界面拒绝一条待审批请求。
        /// 重复拒绝同一请求时返回已有拒绝记录，不重复创建。
        /// </summary>
        /// <param name="request">
        /// 必填。需要拒绝的原始修复请求，必须与待审批记录完全一致。
        /// </param>
        /// <param name="reason">
        /// 必填。用户拒绝的具体原因，不能为空。
        /// </param>
        /// <returns>最终的已拒绝记录。</returns>
        internal static TextureMaxSizeFixApprovalRecord Reject(
            TextureMaxSizeFixRequest request, // 用户准备拒绝的修复请求。
            string reason) // 用户拒绝原因。
        {
            TextureMaxSizeFixApprovalRecord record =
                GetMatchingRecordOrThrow(request);

            if (record.Decision ==
                TextureMaxSizeApprovalDecision.Rejected)
            {
                return record;
            }

            if (record.Decision ==
                TextureMaxSizeApprovalDecision.Approved)
            {
                throw new InvalidOperationException(
                    "该请求已经被批准，不能再改为拒绝。" +
                    "请重新Dry Run后生成新的请求。");
            }

            TextureMaxSizeFixApprovalRecord rejectedRecord =
                TextureMaxSizeFixApprovalRecord.CreateRejected(
                    request,
                    reason);

            RecordsByOperationId[request.OperationId] =
                rejectedRecord;

            return rejectedRecord;
        }

        /// <summary>
        /// 尝试取得一条已批准且精确匹配的审批记录。
        /// 后续真正执行资源修改前必须调用该方法。
        /// </summary>
        /// <param name="request">
        /// 必填。准备执行的修复请求。
        /// </param>
        /// <param name="approvedRecord">
        /// 输出。成功时为已批准记录；失败时为null。
        /// </param>
        /// <returns>存在已批准且完全匹配的记录时返回true。</returns>
        internal static bool TryGetApproved(
            TextureMaxSizeFixRequest request, // 准备执行的修复请求。
            out TextureMaxSizeFixApprovalRecord approvedRecord)
        {
            approvedRecord = null;

            if (request == null)
            {
                return false;
            }

            request.Validate();

            if (!RecordsByOperationId.TryGetValue(
                    request.OperationId,
                    out TextureMaxSizeFixApprovalRecord record))
            {
                return false;
            }

            if (!record.CanExecute || !record.Matches(request))
            {
                return false;
            }

            approvedRecord = record;

            return true;
        }

        /// <summary>
        /// 获取与请求完全匹配的审批记录。
        /// 该方法只用于批准或拒绝操作，找不到记录时明确拒绝。
        /// </summary>
        private static TextureMaxSizeFixApprovalRecord
            GetMatchingRecordOrThrow(
                TextureMaxSizeFixRequest request) // 用户操作对应的修复请求。
        {
            if (request == null)
            {
                throw new ArgumentNullException(
                    nameof(request),
                    "审批请求不能为空。");
            }

            request.Validate();

            if (!RecordsByOperationId.TryGetValue(
                    request.OperationId,
                    out TextureMaxSizeFixApprovalRecord record))
            {
                throw new InvalidOperationException(
                    "没有找到该请求的待审批记录。" +
                    "请先完成Dry Run。");
            }

            if (!record.Matches(request))
            {
                throw new InvalidOperationException(
                    "当前请求与审批记录不一致，拒绝审批操作。");
            }

            return record;
        }
    }
}