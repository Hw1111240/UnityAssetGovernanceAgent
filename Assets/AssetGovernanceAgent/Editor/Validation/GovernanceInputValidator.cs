using System;

namespace AssetGovernanceAgent.Editor.Validation
{
    /// <summary>
    /// 资源治理模块共用的输入校验器。
    ///
    /// 只保存跨模型通用的纯参数校验，
    /// 不访问AssetDatabase，也不执行任何资源修改。
    /// </summary>
    internal static class GovernanceInputValidator
    {
        /// <summary>
        /// 验证必填文本，并删除文本首尾空格。
        /// </summary>
        /// <param name="value">
        /// 必填。需要验证的文本，不能为null、空字符串或纯空格。
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
        /// 必填。必须是Assets/下的资源路径，不能是绝对路径，
        /// 也不能包含“.”或“..”路径段。
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

            return normalizedPath;
        }
    }
}