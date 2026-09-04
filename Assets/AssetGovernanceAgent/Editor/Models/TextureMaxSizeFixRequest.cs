using System;
using AssetGovernanceAgent.Editor.Validation;
using UnityEngine;

namespace AssetGovernanceAgent.Editor.Models
{
    /// <summary>
    /// 修改Texture Importer Max Size时使用的强类型请求。
    ///
    /// Agent只能生成该请求，不能直接修改Unity资源。
    /// Unity执行端收到请求后，仍需重新验证资源状态、规则版本和本地审批状态。
    /// </summary>
    [Serializable]
    public sealed class TextureMaxSizeFixRequest
    {
        [SerializeField] private string operationId;
        [SerializeField] private string issueId;
        [SerializeField] private string ruleId;
        [SerializeField] private string ruleVersion;
        [SerializeField] private string assetGuid;
        [SerializeField] private string assetPath;
        [SerializeField] private int expectedCurrentMaxSize;
        [SerializeField] private int targetMaxSize;
      

        /// <summary>
        /// 本次操作的唯一编号，用于关联日志和防止重复执行。
        /// </summary>
        public string OperationId => operationId;

        /// <summary>
        /// 本次修复针对的GovernanceIssue编号。
        /// </summary>
        public string IssueId => issueId;

        /// <summary>
        /// 本次修复针对的规则编号。
        /// </summary>
        public string RuleId => ruleId;

        /// <summary>
        /// 生成修复请求时使用的规则版本。
        /// </summary>
        public string RuleVersion => ruleVersion;

        /// <summary>
        /// Unity资源GUID，作为资源的主要身份标识。
        /// </summary>
        public string AssetGuid => assetGuid;

        /// <summary>
        /// 生成请求时记录的资源路径快照。
        /// 执行前需要通过GUID重新取得真实路径并进行比较。
        /// </summary>
        public string AssetPath => assetPath;

        /// <summary>
        /// 扫描问题时观察到的Importer Max Size。
        /// 执行前如果实际值已经变化，则拒绝旧请求。
        /// </summary>
        public int ExpectedCurrentMaxSize => expectedCurrentMaxSize;

        /// <summary>
        /// 请求修改到的目标Max Size。
        /// </summary>
        public int TargetMaxSize => targetMaxSize;

        /// <summary>
        /// 创建一条Max Size修复请求。
        /// </summary>
        public TextureMaxSizeFixRequest(
            string operationId, // 必填。标准GUID格式的操作编号。
            string issueId, // 必填。扫描器生成的问题编号。
            string ruleId, // 必填。对应的确定性规则编号。
            string ruleVersion, // 必填。生成请求时的规则版本。
            string assetGuid, // 必填。Unity资源GUID。
            string assetPath, // 必填。Assets/下的资源路径。
            int expectedCurrentMaxSize, // 必填。扫描时观察到的Max Size。
            int targetMaxSize) // 必填。需要修改到的Max Size。
        {
            this.operationId = operationId;
            this.issueId = issueId;
            this.ruleId = ruleId;
            this.ruleVersion = ruleVersion;
            this.assetGuid = assetGuid;
            this.assetPath = assetPath;
            this.expectedCurrentMaxSize = expectedCurrentMaxSize;
            this.targetMaxSize = targetMaxSize;
            Validate();
        }

        /// <summary>
        /// 验证请求中的所有字段。
        ///
        /// 必须保留为公开方法，因为JSON反序列化可能不会调用带参数构造函数。
        /// 修复工具执行前必须再次主动调用Validate。
        /// </summary>
        public void Validate()
        {
            operationId = GovernanceInputValidator.ValidateOperationId(
                operationId);
            issueId = GovernanceInputValidator.RequireText(
                issueId,
                nameof(issueId));
            ruleId = GovernanceInputValidator.RequireText(
                ruleId,
                nameof(ruleId));
            ruleVersion = GovernanceInputValidator.RequireText(
                ruleVersion,
                nameof(ruleVersion));
            assetGuid = GovernanceInputValidator.RequireText(
                assetGuid,
                nameof(assetGuid));
            assetPath = GovernanceInputValidator.ValidateAssetPath(
                assetPath);
            
            GovernanceInputValidator.ValidateTextureMaxSizeChange(
                expectedCurrentMaxSize,
                targetMaxSize);
        }
    }
}