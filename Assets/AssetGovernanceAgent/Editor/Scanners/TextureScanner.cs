using System;
using System.Collections.Generic;
using System.IO;
using AssetGovernanceAgent.Editor.Models;
using AssetGovernanceAgent.Editor.Rules;
using UnityEditor;

namespace AssetGovernanceAgent.Editor.Scanners
{
    /// <summary>
    /// Texture导入设置确定性扫描器。
    /// 
    /// 当前最小版本只检查TextureImporter.maxTextureSize。
    /// 违规判断完全由Unity代码和规则配置完成，不依赖大模型。
    /// </summary>
    public sealed class TextureScanner
    {
        /// <summary>
        /// 扫描指定Assets目录中的Texture Max Size问题。
        /// </summary>
        /// <param name="ruleSet">
        /// 必填。Texture治理规则资产，提供规则版本、上限和严重程度。
        /// </param>
        /// <param name="searchPath">
        /// 必填。扫描目录；只允许Assets或Assets/下的相对资源目录。
        /// 禁止绝对路径、“.”和“..”路径段。
        /// </param>
        /// <param name="includeSubdirectories">
        /// 必填。为true时扫描所有子目录；为false时只扫描当前目录。
        /// </param>
        /// <param name="maxResults">
        /// 必填。最多返回的问题数量，合法范围为1～1000。
        /// 达到上限后安全停止，避免一次返回过多数据。
        /// </param>
        /// <returns>
        /// 按资源路径稳定排序的Max Size问题列表。
        /// 没有问题时返回空列表，不返回null。
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// ruleSet为null时抛出。
        /// </exception>
        /// <exception cref="ArgumentException">
        /// searchPath为空、越权、包含非法路径段或不是Unity有效目录时抛出。
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// maxResults不在1～1000范围内时抛出。
        /// </exception>
        public IReadOnlyList<GovernanceIssue> ScanMaxSize(
            TextureGovernanceRuleSet ruleSet, // 必填。当前使用的Texture规则集合。
            string searchPath,                 // 必填。Assets目录或其子目录。
            bool includeSubdirectories,        // 必填。是否递归扫描子目录。
            int maxResults)                    // 必填。最大问题返回数量，范围1～1000。
        {
            if (ruleSet == null)
            {
                throw new ArgumentNullException(
                    nameof(ruleSet),
                    "Texture规则集合不能为空。");
            }

            string normalizedSearchPath = ValidateSearchPath(searchPath);
            ValidateMaxResults(maxResults);

            // AssetDatabase.FindAssets的返回顺序不应被视为稳定顺序。
            // 先转换成路径并排序，保证相同工程每次扫描顺序一致。
            List<TextureAssetEntry> textureAssets = FindTextureAssets(
                normalizedSearchPath,
                includeSubdirectories);

            var issues = new List<GovernanceIssue>();

            foreach (TextureAssetEntry textureAsset in textureAssets)
            {
                if (issues.Count >= maxResults)
                {
                    // 达到调用方指定的返回上限后安全停止。
                    break;
                }

                TextureImporter importer =
                    AssetImporter.GetAtPath(textureAsset.AssetPath)
                    as TextureImporter;

                if (importer == null)
                {
                    // 资源可能在扫描期间被删除或重新导入。
                    // 当前版本安全跳过；后续日志阶段记录结构化警告。
                    continue;
                }

                if (importer.maxTextureSize <= ruleSet.MaxTextureSize)
                {
                    // 当前配置没有超过规则上限，不生成问题。
                    continue;
                }

                GovernanceIssue issue = CreateMaxSizeIssue(
                    ruleSet,
                    textureAsset,
                    importer.maxTextureSize);

                issues.Add(issue);
            }

            return issues;
        }

        /// <summary>
        /// 获取指定目录下的Texture资源，并按资源路径稳定排序。
        /// </summary>
        private static List<TextureAssetEntry> FindTextureAssets(
            string searchPath,          // 已通过校验的Unity目录。
            bool includeSubdirectories) // 是否包含子目录。
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:Texture",
                new[] { searchPath });

            var assets = new List<TextureAssetEntry>();

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                string normalizedAssetPath =
                    assetPath.Replace('\\', '/');

                if (!includeSubdirectories &&
                    !IsDirectChild(normalizedAssetPath, searchPath))
                {
                    // FindAssets默认递归搜索。
                    // 当调用方不需要子目录时，在此过滤非直接子资源。
                    continue;
                }

                assets.Add(new TextureAssetEntry(
                    guid,
                    normalizedAssetPath));
            }

            assets.Sort(
                (left, right) => string.Compare(
                    left.AssetPath,
                    right.AssetPath,
                    StringComparison.Ordinal));

            return assets;
        }

        /// <summary>
        /// 根据Unity扫描事实创建Max Size问题。
        /// </summary>
        private static GovernanceIssue CreateMaxSizeIssue(
            TextureGovernanceRuleSet ruleSet, // 当前使用的规则集合。
            TextureAssetEntry textureAsset,    // Texture的GUID和路径。
            int currentMaxSize)                // Unity实际读取到的Max Size。
        {
            string issueId =
                $"{TextureRuleIds.MaxTextureSize}:{textureAsset.AssetGuid}";

            return new GovernanceIssue(
                issueId: issueId,
                ruleId: TextureRuleIds.MaxTextureSize,
                ruleVersion: ruleSet.RuleVersion,
                assetGuid: textureAsset.AssetGuid,
                assetPath: textureAsset.AssetPath,
                category: GovernanceCategory.Texture,
                severity: ruleSet.MaxSizeSeverity,
                currentValue: $"{currentMaxSize} px",
                expectedValue: $"不超过 {ruleSet.MaxTextureSize} px",
                evidence:
                    $"TextureImporter.maxTextureSize = {currentMaxSize}",
                suggestion:
                    $"建议将Max Size调整为不超过" +
                    $"{ruleSet.MaxTextureSize} px。",
                isAutoFixable: true,
                suggestedToolName: "update_texture_import_settings");
        }

        /// <summary>
        /// 校验并规范化扫描目录。
        /// </summary>
        private static string ValidateSearchPath(
            string searchPath) // 待校验的Unity扫描目录。
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

            string[] pathSegments = normalizedPath.Split('/');

            foreach (string pathSegment in pathSegments)
            {
                if (pathSegment == "." || pathSegment == "..")
                {
                    throw new ArgumentException(
                        "扫描目录不能包含“.”或“..”路径段。",
                        nameof(searchPath));
                }
            }

            if (!AssetDatabase.IsValidFolder(normalizedPath))
            {
                throw new ArgumentException(
                    $"扫描目录不是有效的Unity目录：{normalizedPath}",
                    nameof(searchPath));
            }

            return normalizedPath;
        }

        /// <summary>
        /// 校验最大返回数量，防止无上限返回扫描结果。
        /// </summary>
        private static void ValidateMaxResults(
            int maxResults) // 最大问题数量，合法范围1～1000。
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
        /// 判断资源是否直接位于指定目录中，而不是更深的子目录中。
        /// </summary>
        private static bool IsDirectChild(
            string assetPath, // Texture资源路径。
            string folderPath) // 扫描目录路径。
        {
            string parentPath = Path
                .GetDirectoryName(assetPath)?
                .Replace('\\', '/');

            return string.Equals(
                parentPath,
                folderPath,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// 扫描器内部使用的Texture资源定位信息。
        /// 不直接暴露给Agent或界面层。
        /// </summary>
        private readonly struct TextureAssetEntry
        {
            public string AssetGuid { get; }
            public string AssetPath { get; }

            public TextureAssetEntry(
                string assetGuid, // Unity资源GUID。
                string assetPath) // Unity资源路径。
            {
                AssetGuid = assetGuid;
                AssetPath = assetPath;
            }
        }
    }
}