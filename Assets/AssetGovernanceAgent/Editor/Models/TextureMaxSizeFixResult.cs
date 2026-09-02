using System;
using AssetGovernanceAgent.Editor.Validation;
using UnityEngine;

namespace AssetGovernanceAgent.Editor.Models
{
    /// <summary>
    /// Texture Max Size修复操作的状态。
    ///
    /// 状态必须由Unity确定性代码生成，
    /// Agent只能读取和解释，不能自行声明资源已经修复成功。
    /// </summary>
    public enum TextureMaxSizeFixStatus
    {
        AwaitingApproval = 0, // Dry Run通过，等待Unity本地审批。
        Applied = 1, // Unity已经修改资源并重新读取了结果。
        Rejected = 2, // 用户在Unity本地拒绝修复。
        NoChange = 3, // 资源已经符合目标值，不需要修改。
        Failed = 4 // 状态校验或资源执行失败。
    }

    /// <summary>
    /// Texture Max Size修复操作的统一结果。
    ///
    /// Dry Run、审批、执行和复检都使用该结构，
    /// 防止Agent根据自然语言猜测工具是否执行成功。
    /// </summary>
    [Serializable]
    public sealed class TextureMaxSizeFixResult
    {
        [SerializeField] private string operationId;
        [SerializeField] private string issueId;
        [SerializeField] private string ruleId;
        [SerializeField] private string ruleVersion;
        [SerializeField] private string assetGuid;
        [SerializeField] private string assetPath;

        [SerializeField] private TextureMaxSizeFixStatus status;

        [SerializeField] private int expectedCurrentMaxSize;
        [SerializeField] private int targetMaxSize;

        [SerializeField] private bool hasObservedMaxSize;
        [SerializeField] private int observedMaxSize;

        [SerializeField] private string errorCode;
        [SerializeField] private string message;

        /// <summary>
        /// 操作唯一编号，用于关联请求、审批和审计日志。
        /// </summary>
        public string OperationId => operationId;

        /// <summary>
        /// 本次操作对应的问题编号。
        /// </summary>
        public string IssueId => issueId;

        /// <summary>
        /// 本次操作对应的规则编号。
        /// </summary>
        public string RuleId => ruleId;

        /// <summary>
        /// 生成请求时使用的规则版本。
        /// </summary>
        public string RuleVersion => ruleVersion;

        /// <summary>
        /// Unity资源GUID。
        /// </summary>
        public string AssetGuid => assetGuid;

        /// <summary>
        /// 生成请求时记录的资源路径。
        /// </summary>
        public string AssetPath => assetPath;

        /// <summary>
        /// 当前操作状态。
        /// </summary>
        public TextureMaxSizeFixStatus Status => status;

        /// <summary>
        /// 扫描问题时记录的Max Size。
        /// </summary>
        public int ExpectedCurrentMaxSize => expectedCurrentMaxSize;

        /// <summary>
        /// 请求修改到的目标Max Size。
        /// </summary>
        public int TargetMaxSize => targetMaxSize;

        /// <summary>
        /// Unity是否成功读取到了实际Max Size。
        /// </summary>
        public bool HasObservedMaxSize => hasObservedMaxSize;

        /// <summary>
        /// Unity最后一次实际读取到的Max Size。
        ///
        /// HasObservedMaxSize为false时，
        /// 不能使用该字段判断资源状态。
        /// </summary>
        public int ObservedMaxSize => observedMaxSize;

        /// <summary>
        /// 机器可读错误码。
        /// 只有Failed状态允许携带错误码。
        /// </summary>
        public string ErrorCode => errorCode;

        /// <summary>
        /// 面向开发者和Agent的结果说明。
        /// </summary>
        public string Message => message;

        /// <summary>
        /// 是否正在等待Unity本地审批。
        /// 该值由状态计算，不能由Agent直接传入。
        /// </summary>
        public bool RequiresApproval =>
            status == TextureMaxSizeFixStatus.AwaitingApproval;

        /// <summary>
        /// Unity是否实际修改了资源。
        /// </summary>
        public bool WasModified =>
            status == TextureMaxSizeFixStatus.Applied;

        /// <summary>
        /// 是否得到成功的最终结果。
        /// NoChange也属于成功，因为资源已经满足目标值。
        /// </summary>
        public bool IsSuccessful =>
            status == TextureMaxSizeFixStatus.Applied ||
            status == TextureMaxSizeFixStatus.NoChange;

        /// <summary>
        /// 创建一条结构化修复结果。
        ///
        /// 使用internal限制创建范围：
        /// 只有当前Editor程序集中的确定性服务能够生成结果，
        /// Agent或外部调用方不能随意构造成功状态。
        /// </summary>
        internal TextureMaxSizeFixResult(
            TextureMaxSizeFixRequest request, // 必填。结果对应的强类型修复请求。
            TextureMaxSizeFixStatus status, // 必填。Unity确定的操作状态。
            bool hasObservedMaxSize, // 必填。是否读取到实际Max Size。
            int observedMaxSize, // 读取到的实际Max Size；未读取时传0。
            string message, // 必填。结果说明。
            string errorCode = "" // Failed状态必填，其他状态应为空。
        )
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request), "修复结果必须关联一条修复请求。");
            }

            // 防止JSON反序列化绕过请求的构造函数校验。
            request.Validate();

            if (!Enum.IsDefined(typeof(TextureMaxSizeFixStatus), status))
            {
                throw new ArgumentOutOfRangeException(nameof(status), status, "修复结果状态不合法。");
            }

            if (hasObservedMaxSize)
            {
                GovernanceInputValidator.ValidateTextureMaxSize(observedMaxSize, nameof(observedMaxSize));
            }

            bool isFailed = status == TextureMaxSizeFixStatus.Failed;

            if (isFailed)
            {
                errorCode = GovernanceInputValidator.RequireText(errorCode, nameof(errorCode));
            }
            else
            {
                // 非失败状态不应该同时携带错误码。
                errorCode = string.Empty;
            }

            operationId = request.OperationId;
            issueId = request.IssueId;
            ruleId = request.RuleId;
            ruleVersion = request.RuleVersion;
            assetGuid = request.AssetGuid;
            assetPath = request.AssetPath;


            this.status = status;

            expectedCurrentMaxSize =
                request.ExpectedCurrentMaxSize;

            targetMaxSize =
                request.TargetMaxSize;

            this.hasObservedMaxSize =
                hasObservedMaxSize;

            this.observedMaxSize =
                hasObservedMaxSize
                    ? observedMaxSize
                    : 0;

            this.errorCode = errorCode;

            this.message =
                GovernanceInputValidator.RequireText(
                    message,
                    nameof(message));
        }
    }
}