using System;
using UnityEngine;

namespace AssetGovernanceAgent.Editor.Models
{
    /// <summary>
    /// 资源治理问题类别。
    /// 当前首版只使用Texture，其他类型留给后续阶段扩展。
    /// </summary>
    public enum GovernanceCategory
    {
        Texture = 0,       // Texture导入设置问题。
        Naming = 1,        // 资源命名或目录问题。
        Reference = 2,     // Missing Script、丢失引用等问题。
        Addressables = 3   // Addressables配置问题。
    }

    /// <summary>
    /// 资源治理问题的严重程度。
    /// 严重程度只用于报告和排序，不能直接决定是否自动修复。
    /// </summary>
    public enum GovernanceSeverity
    {
        Info = 0,       // 提示信息，不影响资源正常使用。
        Warning = 1,    // 建议处理，但通常不会立即造成错误。
        Error = 2,      // 明确违反项目规则，需要处理。
        Critical = 3    // 可能造成严重运行或资源问题。
    }

    /// <summary>
    /// Unity确定性扫描器生成的统一问题结构。
    /// 该对象只保存扫描事实，不保存大模型自行推测的结论。
    /// </summary>
    [Serializable]
    public sealed class GovernanceIssue
    {
        [SerializeField] private string issueId;
        [SerializeField] private string ruleId;
        [SerializeField] private string ruleVersion;
        [SerializeField] private string assetGuid;
        [SerializeField] private string assetPath;
        [SerializeField] private GovernanceCategory category;
        [SerializeField] private GovernanceSeverity severity;
        [SerializeField] private string currentValue;
        [SerializeField] private string expectedValue;
        [SerializeField] private string evidence;
        [SerializeField] private string suggestion;
        [SerializeField] private bool isAutoFixable;
        [SerializeField] private string suggestedToolName;

        public string IssueId => issueId;
        public string RuleId => ruleId;
        public string RuleVersion => ruleVersion;
        public string AssetGuid => assetGuid;
        public string AssetPath => assetPath;
        public GovernanceCategory Category => category;
        public GovernanceSeverity Severity => severity;
        public string CurrentValue => currentValue;
        public string ExpectedValue => expectedValue;
        public string Evidence => evidence;
        public string Suggestion => suggestion;
        public bool IsAutoFixable => isAutoFixable;
        public string SuggestedToolName => suggestedToolName;

        /// <summary>
        /// 创建一条经过基础校验的资源治理问题。
        /// </summary>
        /// <param name="issueId">必填。问题唯一编号，例如“texture_max_size:资源GUID”。</param>
        /// <param name="ruleId">必填。命中的确定性规则编号。</param>
        /// <param name="ruleVersion">必填。规则版本，用于关联对应知识库规范。</param>
        /// <param name="assetGuid">必填。Unity资源GUID，避免资源移动后只依赖旧路径。</param>
        /// <param name="assetPath">必填。必须位于Assets/目录下，禁止绝对路径和“..”。</param>
        /// <param name="category">必填。问题类别，首版使用Texture。</param>
        /// <param name="severity">必填。问题严重程度。</param>
        /// <param name="currentValue">必填。扫描到的当前配置值。</param>
        /// <param name="expectedValue">必填。规则要求的目标值。</param>
        /// <param name="evidence">必填。Unity扫描器得到的确定性证据。</param>
        /// <param name="suggestion">必填。面向开发者的处理建议。</param>
        /// <param name="isAutoFixable">必填。是否存在受控自动修复能力。</param>
        /// <param name="suggestedToolName">
        /// 可选。自动修复工具名称；isAutoFixable为true时必须提供。
        /// </param>
        public GovernanceIssue(
            string issueId,                    // 问题唯一编号。
            string ruleId,                     // 规则编号。
            string ruleVersion,                // 规则版本。
            string assetGuid,                  // Unity资源GUID。
            string assetPath,                  // Assets/下的资源路径。
            GovernanceCategory category,       // 问题类别。
            GovernanceSeverity severity,       // 严重程度。
            string currentValue,               // 当前值。
            string expectedValue,              // 期望值。
            string evidence,                   // 确定性证据。
            string suggestion,                 // 处理建议。
            bool isAutoFixable,                // 是否可受控修复。
            string suggestedToolName = "")     // 建议调用的修复工具。
        {
            this.issueId = RequireText(issueId, nameof(issueId));
            this.ruleId = RequireText(ruleId, nameof(ruleId));
            this.ruleVersion = RequireText(ruleVersion, nameof(ruleVersion));
            this.assetGuid = RequireText(assetGuid, nameof(assetGuid));
            this.assetPath = ValidateAssetPath(assetPath);
            this.category = category;
            this.severity = severity;
            this.currentValue = RequireText(currentValue, nameof(currentValue));
            this.expectedValue = RequireText(expectedValue, nameof(expectedValue));
            this.evidence = RequireText(evidence, nameof(evidence));
            this.suggestion = RequireText(suggestion, nameof(suggestion));
            this.isAutoFixable = isAutoFixable;

            if (isAutoFixable && string.IsNullOrWhiteSpace(suggestedToolName))
            {
                throw new ArgumentException(
                    "支持自动修复的问题必须指定修复工具名称。",
                    nameof(suggestedToolName));
            }

            this.suggestedToolName = isAutoFixable
                ? suggestedToolName.Trim()
                : string.Empty;
        }

        /// <summary>
        /// 验证必填文本，防止扫描器生成字段缺失的问题对象。
        /// </summary>
        private static string RequireText(
            string value,          // 需要校验的文本。
            string parameterName)  // 出错时显示的参数名称。
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "参数不能为空或只包含空白字符。",
                    parameterName);
            }

            return value.Trim();
        }

        /// <summary>
        /// 验证并统一Unity资源路径格式。
        /// </summary>
        private static string ValidateAssetPath(
            string assetPath) // 必填。需要验证的Unity资源路径。
        {
            string normalizedPath = RequireText(
                assetPath,
                nameof(assetPath)).Replace('\\', '/');

            bool isInsideAssets = normalizedPath.StartsWith(
                "Assets/",
                StringComparison.Ordinal);

            bool containsParentTraversal =
                normalizedPath.Contains("../") ||
                normalizedPath.EndsWith("/..", StringComparison.Ordinal);

            if (!isInsideAssets || containsParentTraversal)
            {
                throw new ArgumentException(
                    "资源路径必须位于Assets/目录下，并且不能包含“..”。",
                    nameof(assetPath));
            }

            return normalizedPath;
        }
    }
}