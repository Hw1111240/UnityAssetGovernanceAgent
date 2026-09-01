using System;
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
        /// <summary>
        /// Unity支持的Max Size白名单。
        /// 即使Agent传入其他整数，也不能直接用于修改Importer。
        /// </summary>
        private static readonly int[] SupportedMaxSizes =
        {
            32,
            64,
            128,
            256,
            512,
            1024,
            2048,
            4096,
            8192
        };

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
            string operationId,           // 必填。标准GUID格式的操作编号。
            string issueId,               // 必填。扫描器生成的问题编号。
            string ruleId,                // 必填。对应的确定性规则编号。
            string ruleVersion,           // 必填。生成请求时的规则版本。
            string assetGuid,              // 必填。Unity资源GUID。
            string assetPath,              // 必填。Assets/下的资源路径。
            int expectedCurrentMaxSize,    // 必填。扫描时观察到的Max Size。
            int targetMaxSize)             // 必填。需要修改到的Max Size。
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
            operationId = RequireText(
                operationId,
                nameof(operationId));

            if (!Guid.TryParse(operationId, out _))
            {
                throw new ArgumentException(
                    "operationId必须是标准GUID格式。",
                    nameof(operationId));
            }

            issueId = RequireText(issueId, nameof(issueId));
            ruleId = RequireText(ruleId, nameof(ruleId));
            ruleVersion = RequireText(ruleVersion, nameof(ruleVersion));
            assetGuid = RequireText(assetGuid, nameof(assetGuid));
            assetPath = ValidateAssetPath(assetPath);

            ValidateMaxSize(
                expectedCurrentMaxSize,
                nameof(expectedCurrentMaxSize));

            ValidateMaxSize(
                targetMaxSize,
                nameof(targetMaxSize));

            if (targetMaxSize > expectedCurrentMaxSize)
            {
                throw new ArgumentException(
                    "治理修复不能将Max Size调整得比扫描值更大。",
                    nameof(targetMaxSize));
            }
        }

        /// <summary>
        /// 验证必填文本并删除首尾空格。
        /// </summary>
        private static string RequireText(
            string value,         // 需要验证的文本。
            string parameterName) // 字段不合法时对应的参数名称。
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
        /// 验证资源路径只能位于Assets目录中。
        /// </summary>
        private static string ValidateAssetPath(
            string assetPath) // 需要验证和规范化的Unity资源路径。
        {
            string normalizedPath = RequireText(
                assetPath,
                nameof(assetPath))
                .Replace('\\', '/');

            bool isInsideAssets = normalizedPath.StartsWith(
                "Assets/",
                StringComparison.Ordinal);

            string[] pathSegments = normalizedPath.Split('/');

            foreach (string pathSegment in pathSegments)
            {
                if (pathSegment == "." || pathSegment == "..")
                {
                    throw new ArgumentException(
                        "资源路径不能包含“.”或“..”路径段。",
                        nameof(assetPath));
                }
            }

            if (!isInsideAssets)
            {
                throw new ArgumentException(
                    "资源路径必须位于Assets/目录下。",
                    nameof(assetPath));
            }

            return normalizedPath;
        }

        /// <summary>
        /// 验证Max Size必须属于Unity支持的白名单。
        /// </summary>
        private static void ValidateMaxSize(
            int value,            // 需要验证的Max Size。
            string parameterName) // 不合法时对应的参数名称。
        {
            if (Array.IndexOf(SupportedMaxSizes, value) >= 0)
            {
                return;
            }

            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Max Size必须是32、64、128、256、512、1024、2048、4096或8192。");
        }
    }
}