using AssetGovernanceAgent.Editor.Models;
using UnityEditor;
using UnityEngine;

namespace AssetGovernanceAgent.Editor.Rules
{
    /// <summary>
    /// Texture的用途类型。
    /// 不同用途的Texture不能共用完全相同的规则，例如UI通常不需要Mipmap。
    /// </summary>
    public enum TextureUsageType
    {
        Ui = 0,       // UI界面使用的Sprite或Texture。
        World = 1     // 三维场景、模型或环境使用的Texture。
    }

    /// <summary>
    /// Texture治理规则的固定编号。
    /// RuleId用于标识规则含义，不应随着规则参数调整而改变。
    /// </summary>
    public static class TextureRuleIds
    {
        public const string MaxTextureSize = "TEX_MAX_SIZE_001";
        public const string ReadWrite = "TEX_READ_WRITE_001";
        public const string Mipmap = "TEX_MIPMAP_001";
        public const string Compression = "TEX_COMPRESSION_001";
        public const string PlatformOverride = "TEX_PLATFORM_OVERRIDE_001";
    }

    /// <summary>
    /// Texture治理规则集合。
    /// 使用ScriptableObject保存规则，使规则参数能够通过Inspector配置，
    /// 不需要修改扫描器代码或依赖大模型判断。
    /// </summary>
    [CreateAssetMenu(
        fileName = "TextureGovernanceRuleSet",
        menuName = "Asset Governance/Texture Rule Set")]
    public sealed class TextureGovernanceRuleSet : ScriptableObject
    {
        [Header("规则基本信息")]

        [SerializeField]
        [Tooltip("规则版本。修改规则要求后应更新版本，例如1.0.0。")]
        private string ruleVersion = "1.0.0";

        [SerializeField]
        [Tooltip("该规则集合适用的Texture用途。")]
        private TextureUsageType usageType = TextureUsageType.Ui;

        [Header("Max Size规则")]

        [SerializeField]
        [Min(32)]
        [Tooltip("允许的最大Texture尺寸。扫描器只比较当前值，不在此阶段修改资源。")]
        private int maxTextureSize = 2048;

        [SerializeField]
        [Tooltip("Max Size不符合规则时生成的问题等级。")]
        private GovernanceSeverity maxSizeSeverity =
            GovernanceSeverity.Warning;

        [Header("Read/Write规则")]

        [SerializeField]
        [Tooltip("规则要求的Read/Write状态。UI Texture通常应关闭。")]
        private bool expectedIsReadable;

        [SerializeField]
        [Tooltip("Read/Write不符合规则时生成的问题等级。")]
        private GovernanceSeverity readWriteSeverity =
            GovernanceSeverity.Warning;

        [Header("Mipmap规则")]

        [SerializeField]
        [Tooltip("规则要求的Mipmap状态。UI Texture通常应关闭。")]
        private bool expectedMipmapEnabled;

        [SerializeField]
        [Tooltip("Mipmap不符合规则时生成的问题等级。")]
        private GovernanceSeverity mipmapSeverity =
            GovernanceSeverity.Warning;

        [Header("Compression规则")]

        [SerializeField]
        [Tooltip("规则要求的Texture压缩类型。")]
        private TextureImporterCompression expectedCompression =
            TextureImporterCompression.Compressed;

        [SerializeField]
        [Tooltip("Compression不符合规则时生成的问题等级。")]
        private GovernanceSeverity compressionSeverity =
            GovernanceSeverity.Warning;

        [Header("平台Override规则")]

        [SerializeField]
        [Tooltip("目标平台名称，例如Android或iPhone。")]
        private string targetPlatform = "Android";

        [SerializeField]
        [Tooltip("是否要求目标平台必须启用Override。")]
        private bool requirePlatformOverride = true;

        [SerializeField]
        [Tooltip("平台Override不符合规则时生成的问题等级。")]
        private GovernanceSeverity platformOverrideSeverity =
            GovernanceSeverity.Warning;

        /// <summary>
        /// 当前规则版本，用于问题报告、RAG来源匹配和修改前后追踪。
        /// </summary>
        public string RuleVersion => ruleVersion;

        /// <summary>
        /// 当前规则集合适用的Texture用途。
        /// </summary>
        public TextureUsageType UsageType => usageType;

        /// <summary>
        /// 允许的最大Texture尺寸。
        /// </summary>
        public int MaxTextureSize => maxTextureSize;

        /// <summary>
        /// Max Size问题的严重程度。
        /// </summary>
        public GovernanceSeverity MaxSizeSeverity => maxSizeSeverity;

        /// <summary>
        /// 规则要求的Read/Write状态。
        /// </summary>
        public bool ExpectedIsReadable => expectedIsReadable;

        /// <summary>
        /// Read/Write问题的严重程度。
        /// </summary>
        public GovernanceSeverity ReadWriteSeverity => readWriteSeverity;

        /// <summary>
        /// 规则要求的Mipmap状态。
        /// </summary>
        public bool ExpectedMipmapEnabled => expectedMipmapEnabled;

        /// <summary>
        /// Mipmap问题的严重程度。
        /// </summary>
        public GovernanceSeverity MipmapSeverity => mipmapSeverity;

        /// <summary>
        /// 规则要求的Texture压缩类型。
        /// </summary>
        public TextureImporterCompression ExpectedCompression =>
            expectedCompression;

        /// <summary>
        /// Compression问题的严重程度。
        /// </summary>
        public GovernanceSeverity CompressionSeverity =>
            compressionSeverity;

        /// <summary>
        /// 需要检查Override的目标平台。
        /// </summary>
        public string TargetPlatform => targetPlatform;

        /// <summary>
        /// 是否要求目标平台启用Override。
        /// </summary>
        public bool RequirePlatformOverride => requirePlatformOverride;

        /// <summary>
        /// 平台Override问题的严重程度。
        /// </summary>
        public GovernanceSeverity PlatformOverrideSeverity =>
            platformOverrideSeverity;

        /// <summary>
        /// Inspector修改配置后执行基础校验。
        /// 这里只保证配置能够使用，写工具阶段仍需进行更严格的白名单校验。
        /// </summary>
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(ruleVersion))
            {
                ruleVersion = "1.0.0";
            }
            else
            {
                ruleVersion = ruleVersion.Trim();
            }

            maxTextureSize = Mathf.Max(32, maxTextureSize);

            if (string.IsNullOrWhiteSpace(targetPlatform))
            {
                targetPlatform = "Android";
            }
            else
            {
                targetPlatform = targetPlatform.Trim();
            }
        }
    }
}