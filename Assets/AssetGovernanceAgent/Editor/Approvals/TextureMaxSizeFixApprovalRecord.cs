using System;
using AssetGovernanceAgent.Editor.Models;
using AssetGovernanceAgent.Editor.Validation;
using UnityEngine;

namespace AssetGovernanceAgent.Editor.Approvals
{
    /// <summary>
    /// Unity本地对Texture Max Size修复计划的审批决定。
    /// Agent不能直接构造“已批准”状态；
    /// 后续只能由Unity本地审批服务创建。
    /// </summary>
    internal enum TextureMaxSizeApprovalDecision
    {
        Pending = 0, // 已生成修复计划，尚未由用户决定。
        Approved = 1, // 用户已允许执行这条确定的修复请求。
        Rejected = 2 // 用户拒绝执行这条修复请求。
    }

    /// <summary>
    /// 一条与具体修复请求强绑定的Unity本地审批记录。
    ///
    /// 当前仅定义数据模型，后续审批仓库负责在内存中保存它，
    /// 编辑器窗口负责创建批准或拒绝记录。
    /// </summary>
    [Serializable]
    internal sealed class TextureMaxSizeFixApprovalRecord
    {
        [SerializeField] private TextureMaxSizeFixRequest request;

        [SerializeField] private TextureMaxSizeApprovalDecision decision;

        [SerializeField] private string decisionReason;

        [SerializeField] private string decidedAtUtc;

        /// <summary>
        /// 本次审批绑定的完整修复请求。
        /// 后续执行时必须使用该请求进行精确匹配。
        /// </summary>
        internal TextureMaxSizeFixRequest Request => request;

        /// <summary>
        /// 当前本地审批决定。
        /// </summary>
        internal TextureMaxSizeApprovalDecision Decision => decision;

        /// <summary>
        /// 用户拒绝时的原因，或用户批准时的可选备注。
        /// </summary>
        internal string DecisionReason => decisionReason;

        /// <summary>
        /// 本地用户作出最终决定的UTC时间。
        /// Pending状态时为空字符串。
        /// </summary>
        internal string DecidedAtUtc => decidedAtUtc;

        /// <summary>
        /// 当前记录是否允许后续执行真正的资源修改。
        /// </summary>
        internal bool CanExecute =>
            decision == TextureMaxSizeApprovalDecision.Approved;

        /// <summary>
        /// 当前记录是否已经完成审批。
        /// </summary>
        internal bool IsFinalDecision =>
            decision == TextureMaxSizeApprovalDecision.Approved ||
            decision == TextureMaxSizeApprovalDecision.Rejected;

        /// <summary>
        /// 私有构造函数。
        /// 外部只能使用具名工厂方法创建三种合法状态。
        /// </summary>
        private TextureMaxSizeFixApprovalRecord(
            TextureMaxSizeFixRequest request, // 必填。需要绑定的修复请求。
            TextureMaxSizeApprovalDecision decision, // 必填。本地审批决定。
            string decisionReason, // Pending可为空；Rejected时必填。
            string decidedAtUtc) // Pending为空；最终决定必须为UTC时间。
        {
            if (request == null)
            {
                throw new ArgumentNullException(
                    nameof(request),
                    "审批记录必须绑定一条修复请求。");
            }

            // 防止JSON反序列化绕过请求构造函数。
            request.Validate();

            this.request = request;
            this.decision = decision;

            if (decision ==
                TextureMaxSizeApprovalDecision.Pending)
            {
                this.decisionReason = string.Empty;
                this.decidedAtUtc = string.Empty;
                return;
            }

            this.decisionReason =
                decision == TextureMaxSizeApprovalDecision.Rejected
                    ? GovernanceInputValidator.RequireText(
                        decisionReason,
                        nameof(decisionReason))
                    : string.IsNullOrWhiteSpace(decisionReason)
                        ? string.Empty
                        : decisionReason.Trim();

            this.decidedAtUtc =
                GovernanceInputValidator.RequireText(
                    decidedAtUtc,
                    nameof(decidedAtUtc));
        }

        /// <summary>
        /// 创建尚未由用户决定的审批记录。
        /// </summary>
        internal static TextureMaxSizeFixApprovalRecord CreatePending(
            TextureMaxSizeFixRequest request) // 必填。已通过Dry Run的修复请求。
        {
            return new TextureMaxSizeFixApprovalRecord(
                request,
                TextureMaxSizeApprovalDecision.Pending,
                string.Empty,
                string.Empty);
        }

        /// <summary>
        /// 创建用户已在Unity本地批准的审批记录。
        /// </summary>
        internal static TextureMaxSizeFixApprovalRecord CreateApproved(
            TextureMaxSizeFixRequest request, // 必填。用户批准的修复请求。
            string note = "") // 可选。用户留下的审批备注。
        {
            return new TextureMaxSizeFixApprovalRecord(
                request,
                TextureMaxSizeApprovalDecision.Approved,
                note,
                DateTime.UtcNow.ToString("O"));
        }

        /// <summary>
        /// 创建用户已在Unity本地拒绝的审批记录。
        /// </summary>
        internal static TextureMaxSizeFixApprovalRecord CreateRejected(
            TextureMaxSizeFixRequest request, // 必填。用户拒绝的修复请求。
            string reason) // 必填。用户拒绝的具体原因。
        {
            return new TextureMaxSizeFixApprovalRecord(
                request,
                TextureMaxSizeApprovalDecision.Rejected,
                reason,
                DateTime.UtcNow.ToString("O"));
        }

        /// <summary>
        /// 判断审批记录是否精确对应待执行的修复请求。
        ///
        /// 不能只比较OperationId，必须比较所有会影响资源修改的字段。
        /// </summary>
        internal bool Matches(
            TextureMaxSizeFixRequest candidateRequest) // 需要执行的候选请求。
        {
            if (candidateRequest == null)
            {
                return false;
            }

            candidateRequest.Validate();

            return string.Equals(
                       request.OperationId,
                       candidateRequest.OperationId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       request.IssueId,
                       candidateRequest.IssueId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       request.RuleId,
                       candidateRequest.RuleId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       request.RuleVersion,
                       candidateRequest.RuleVersion,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       request.AssetGuid,
                       candidateRequest.AssetGuid,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       request.AssetPath,
                       candidateRequest.AssetPath,
                       StringComparison.Ordinal) &&
                   request.ExpectedCurrentMaxSize ==
                   candidateRequest.ExpectedCurrentMaxSize &&
                   request.TargetMaxSize ==
                   candidateRequest.TargetMaxSize;
        }
    }
}