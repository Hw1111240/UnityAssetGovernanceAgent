using System;
using UnityEditor;

namespace AssetGovernanceAgent.Editor.Validation
{
    /// <summary>
    /// 资源治理模块统一输入校验器。
    ///
    /// 集中管理模型、扫描器和后续写工具共用的输入边界，
    /// 防止不同业务脚本复制校验代码后出现规则不一致。
    /// internal：表示只能被“同一个程序集”中的代码访问。
    /// </summary>
    internal static class GovernanceInputValidator
    {
        /// <summary>
        /// Unity TextureImporter支持的Max Size白名单。
        /// Agent或外部接口传入的其他整数不能用于修改资源。
        /// </summary>
        private static readonly int[] SupportedTextureMaxSizes =
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

        /// <summary>
        /// 验证必填文本，并删除文本首尾空格。
        /// </summary>
        /// <param name="value">
        /// 必填。不能为null、空字符串或只包含空白字符。
        /// </param>
        /// <param name="parameterName">
        /// 必填。验证失败时写入异常的参数名称。
        /// </param>
        /// <returns>删除首尾空格后的有效文本。</returns>
        internal static string RequireText(
            string value,         // 需要验证的原始文本。
            string parameterName) // 异常中显示的参数名称。
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
        /// 验证并规范化Unity资源文件路径。
        /// </summary>
        /// <param name="assetPath">
        /// 必填。必须位于Assets/目录下，禁止绝对路径以及“.”、“..”路径段。
        /// </param>
        /// <returns>使用正斜杠的规范化资源路径。</returns>
        internal static string ValidateAssetPath(
            string assetPath) // 需要验证的Unity资源路径。
        {
            string normalizedPath = RequireText(
                assetPath,
                nameof(assetPath))
                .Replace('\\', '/');

            if (!normalizedPath.StartsWith(
                    "Assets/",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "资源路径必须位于Assets/目录下。",
                    nameof(assetPath));
            }

            ValidatePathSegments(
                normalizedPath,
                nameof(assetPath));

            return normalizedPath;
        }

        /// <summary>
        /// 验证并规范化Unity扫描目录。
        /// </summary>
        /// <param name="searchPath">
        /// 必填。只能是Assets根目录或Assets/下的有效目录，
        /// 禁止绝对路径以及“.”、“..”路径段。
        /// </param>
        /// <returns>通过校验并使用正斜杠的扫描目录。</returns>
        internal static string ValidateSearchPath(
            string searchPath) // 需要验证的Unity扫描目录。
        {
            if (string.IsNullOrWhiteSpace(searchPath))
            {
                throw new ArgumentException(
                    "扫描目录不能为空。",
                    nameof(searchPath));
            }

            string normalizedPath = searchPath
                .Trim()
                .Replace('\\', '/')
                .TrimEnd('/');

            bool isAssetsRoot = string.Equals(
                normalizedPath,
                "Assets",
                StringComparison.Ordinal);

            bool isInsideAssets = normalizedPath.StartsWith(
                "Assets/",
                StringComparison.Ordinal);

            if (!isAssetsRoot && !isInsideAssets)
            {
                throw new ArgumentException(
                    "扫描目录只能是Assets或Assets/下的目录。",
                    nameof(searchPath));
            }

            ValidatePathSegments(
                normalizedPath,
                nameof(searchPath));

            if (!AssetDatabase.IsValidFolder(normalizedPath))
            {
                throw new ArgumentException(
                    $"扫描目录不是有效的Unity目录：{normalizedPath}",
                    nameof(searchPath));
            }

            return normalizedPath;
        }

        /// <summary>
        /// 验证扫描器允许返回的问题数量。
        /// </summary>
        /// <param name="maxResults">
        /// 必填。合法范围为1～1000，防止无上限返回问题数据。
        /// </param>
        internal static void ValidateMaxResults(
            int maxResults) // 最大问题返回数量，合法范围1～1000。
        {
            if (maxResults < 1 || maxResults > 1000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxResults),
                    maxResults,
                    "最大返回数量必须在1～1000之间。");
            }
        }

        /// <summary>
        /// 验证并规范化操作唯一编号。
        /// </summary>
        /// <param name="operationId">
        /// 必填。必须能够解析为GUID，用于日志关联和幂等控制。
        /// </param>
        /// <returns>删除首尾空格后的GUID文本。</returns>
        internal static string ValidateOperationId(
            string operationId) // 需要验证的操作唯一编号。
        {
            string normalizedOperationId = RequireText(
                operationId,
                nameof(operationId));

            if (!Guid.TryParse(normalizedOperationId, out _))
            {
                throw new ArgumentException(
                    "operationId必须是标准GUID格式。",
                    nameof(operationId));
            }

            return normalizedOperationId;
        }

        /// <summary>
        /// 验证Texture Max Size修改前后的值及修改方向。
        /// </summary>
        /// <param name="expectedCurrentMaxSize">
        /// 必填。扫描问题时观察到的当前Max Size。
        /// </param>
        /// <param name="targetMaxSize">
        /// 必填。请求修改到的目标Max Size，不能大于当前值。
        /// </param>
        internal static void ValidateTextureMaxSizeChange(
            int expectedCurrentMaxSize, // 扫描时观察到的Max Size。
            int targetMaxSize)          // 请求修改到的目标Max Size。
        {
            ValidateTextureMaxSize(
                expectedCurrentMaxSize,
                nameof(expectedCurrentMaxSize));

            ValidateTextureMaxSize(
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
        /// 验证Max Size必须属于Unity支持的白名单。
        /// </summary>
        internal static void ValidateTextureMaxSize(
            int value,            // 需要验证的Max Size。
            string parameterName) // 不合法时对应的参数名称。
        {
            if (Array.IndexOf(SupportedTextureMaxSizes, value) >= 0)
            {
                return;
            }

            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Max Size必须是32、64、128、256、512、1024、2048、4096或8192。");
        }

        /// <summary>
        /// 拒绝可能造成目录跳转的“.”和“..”路径段。
        /// </summary>
        private static void ValidatePathSegments(
            string normalizedPath, // 已统一为正斜杠的路径。
            string parameterName)  // 不合法时对应的参数名称。
        {
            string[] pathSegments = normalizedPath.Split('/');

            foreach (string pathSegment in pathSegments)
            {
                if (pathSegment == "." || pathSegment == "..")
                {
                    throw new ArgumentException(
                        "路径不能包含“.”或“..”路径段。",
                        parameterName);
                }
            }
        }
    }
}
