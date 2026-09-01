using System.Collections.Generic;
using System.IO;
using AssetGovernanceAgent.Editor.Models;
using AssetGovernanceAgent.Editor.Rules;
using AssetGovernanceAgent.Editor.Scanners;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;


namespace AssetGovernanceAgent.Editor.Tests
{
    /// <summary>
    /// TextureScanner的EditMode测试。
    /// 测试资源由代码自动创建，测试结束后自动清理，
    /// 避免依赖人工准备的Texture和Importer配置。
    /// </summary>
    [TestFixture]
    public sealed class TextureScannerTests
    {
        // 测试资源的根目录，由SetUp自动创建，TearDown自动清理。
        private const string TestFolder =
            "Assets/AssetGovernanceAgent/TestAssetsGenerated";

        // 根目录下的测试纹理路径，Importer Max Size被设为4096以触发违规。
        private const string TestTexturePath =
            TestFolder + "/OversizedTexture.png";

        // TestFolder的子目录，用于验证递归扫描功能。
        private const string NestedTestFolder =
            TestFolder + "/Nested";

        // 嵌套子目录中的测试纹理路径，用于验证子目录内的扫描结果。
        private const string NestedTestTexturePath =
            NestedTestFolder + "/NestedOversizedTexture.png";

        // 根目录下的第二张测试纹理路径，用于验证扫描器处理多个违规纹理的场景。
        private const string SecondTestTexturePath =
            TestFolder + "/SecondOversizedTexture.png";
        
        // 规则集中定义的最大纹理尺寸上限，超过此值即视为违规。
        private const int RuleMaxSize = 2048;

        // 测试纹理的Importer Max Size，故意设为大于RuleMaxSize以触发违规检测。
        private const int ImporterMaxSize = 4096;

        // 每个测试用例使用的规则集，由SetUp创建、TearDown销毁。
        private TextureGovernanceRuleSet ruleSet;


        /// <summary>
        /// 每个测试执行前创建独立规则和测试Texture。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            DeleteGeneratedTestFolder();

            AssetDatabase.CreateFolder(
                "Assets/AssetGovernanceAgent",
                "TestAssetsGenerated");

            ruleSet = CreateTestRuleSet();

            CreateTestTexture(
                TestTexturePath, // 自动生成的测试Texture路径。
                ImporterMaxSize); // 设置为4096，故意超过2048规则。
        }

        /// <summary>
        /// 每个测试结束后删除临时资源和内存中的规则对象。
        /// 即使测试失败，NUnit通常仍会调用TearDown。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (ruleSet != null)
            {
                Object.DestroyImmediate(ruleSet);
                ruleSet = null;
            }

            DeleteGeneratedTestFolder();
        }

        /// <summary>
        /// 验证Importer Max Size超过规则时，
        /// 扫描器能够生成字段正确的GovernanceIssue。
        /// </summary>
        [Test]
        public void ScanMaxSize_WhenImporterExceedsRule_ReturnsIssue()
        {
            var scanner = new TextureScanner();

            IReadOnlyList<GovernanceIssue> issues =
                scanner.ScanMaxSize(
                    ruleSet, // 使用测试专用规则。
                    TestFolder, // 只扫描自动生成的测试目录。
                    true, // 允许扫描子目录。
                    100); // 最多返回100条问题。

            Assert.That(
                issues.Count,
                Is.EqualTo(1),
                "4096 Max Size超过2048规则时，应生成一条问题。");

            GovernanceIssue issue = issues[0];

            Assert.That(
                issue.RuleId,
                Is.EqualTo(TextureRuleIds.MaxTextureSize));

            Assert.That(
                issue.RuleVersion,
                Is.EqualTo("test-1.0.0"));

            Assert.That(
                issue.AssetPath,
                Is.EqualTo(TestTexturePath));

            Assert.That(
                issue.Category,
                Is.EqualTo(GovernanceCategory.Texture));

            Assert.That(
                issue.CurrentValue,
                Is.EqualTo("4096 px"));

            Assert.That(
                issue.ExpectedValue,
                Is.EqualTo("不超过 2048 px"));

            Assert.That(
                issue.Evidence,
                Is.EqualTo(
                    "TextureImporter.maxTextureSize = 4096"));

            Assert.That(
                issue.IsAutoFixable,
                Is.True);

            Assert.That(
                issue.SuggestedToolName,
                Is.EqualTo("update_texture_import_settings"));
        }

        /// <summary>
        /// 验证纹理的 Max Size 等于规则上限时，
        /// 扫描器不会将合规资源误判为违规资源。
        /// </summary>
        [Test]
        public void ScanMaxSize_WhenImporterMeetsRule_ReturnsEmpty()
        {
            TextureImporter importer = AssetImporter.GetAtPath(TestTexturePath) as TextureImporter;

            Assert.That(
                importer,
                Is.Not.Null,
                "测试纹理应该能够取得 TextureImporter。");

            importer.maxTextureSize = RuleMaxSize;
            importer.SaveAndReimport(); // 保存设置并重新导入纹理。

            var scanner = new TextureScanner();

            IReadOnlyList<GovernanceIssue> issues =
                scanner.ScanMaxSize(
                    ruleSet, // ruleSet：测试规则，Max Size上限为2048，必填。
                    TestFolder, // rootPath：临时测试目录，必须位于Assets下。
                    true, // recursive：扫描其全部子目录。
                    100); // maxResults：最多返回100条问题，必须大于0。

            Assert.That(
                issues,
                Is.Empty,
                "Max Size等于规则上限时不应该产生违规问题。");
        }

        /// <summary>
        /// 验证关闭递归扫描时，只返回当前目录中的违规Texture，
        /// 不返回更深层子目录中的违规Texture。
        /// </summary>
        [Test]
        public void ScanMaxSize_WhenSubdirectoriesDisabled_ExcludesNestedTexture()
        {
            string nestedFolderGuid = AssetDatabase.CreateFolder(
                TestFolder,
                "Nested"
            );

            Assert.That(
                nestedFolderGuid,
                Is.Not.Empty,
                "测试子目录应该创建成功。"
            );

            CreateTestTexture(NestedTestTexturePath, ImporterMaxSize);

            var scanner = new TextureScanner();

            // 先执行递归扫描，证明根目录和子目录中的两张Texture都能被发现。
            IReadOnlyList<GovernanceIssue> recursiveIssues =
                scanner.ScanMaxSize(
                    ruleSet, // ruleSet：测试规则，Max Size上限为2048。
                    TestFolder, // searchPath：自动生成的测试根目录。
                    true, // includeSubdirectories：包含全部子目录。
                    100); // maxResults：最多返回100条问题，范围1～1000。

            Assert.That(recursiveIssues.Count,
                Is.EqualTo(2),
                "递归扫描时应该发现根目录和子目录中的两张违规Texture。");


            // 再关闭递归扫描，验证子目录中的Texture会被过滤。
            IReadOnlyList<GovernanceIssue> directIssues =
                scanner.ScanMaxSize(
                    ruleSet, // ruleSet：使用同一份测试规则。
                    TestFolder, // searchPath：只扫描测试根目录。
                    false, // includeSubdirectories：关闭子目录扫描。
                    100); // maxResults：最多返回100条问题。

            Assert.That(
                directIssues[0].AssetPath,
                Is.EqualTo(TestTexturePath),
                "关闭递归扫描后，不应该返回子目录中的Texture。");
        }
        
        /// <summary>
        /// 验证违规资源数量超过maxResults时，
        /// 扫描器只返回调用方允许的最大问题数量。
        /// </summary>
        [Test]
        public void ScanMaxSize_WhenIssuesExceedMaxResults_LimitsResultCount()
        {
            CreateTestTexture(
                SecondTestTexturePath, // assetPath：第二张违规Texture的资源路径。
                ImporterMaxSize);      // importerMaxSize：设置为4096，超过2048规则。

            var scanner = new TextureScanner();

            // 先证明测试目录中确实存在两张违规Texture。
            IReadOnlyList<GovernanceIssue> allIssues =
                scanner.ScanMaxSize(
                    ruleSet,    // ruleSet：测试规则，Max Size上限为2048。
                    TestFolder, // searchPath：测试资源所在目录。
                    true,       // includeSubdirectories：允许扫描子目录。
                    100);       // maxResults：最多返回100条，确保本次结果不被截断。

            Assert.That(
                allIssues.Count,
                Is.EqualTo(2),
                "测试目录中应该存在两张违规Texture。");

            // 将返回上限设置为1，验证扫描器会限制结果数量。
            IReadOnlyList<GovernanceIssue> limitedIssues =
                scanner.ScanMaxSize(
                    ruleSet,    // ruleSet：使用同一份测试规则。
                    TestFolder, // searchPath：扫描同一个测试目录。
                    true,       // includeSubdirectories：允许扫描子目录。
                    1);         // maxResults：最多只能返回1条问题。

            Assert.That(
                limitedIssues.Count,
                Is.EqualTo(1),
                "maxResults为1时，扫描器最多只能返回一条问题。");

            Assert.That(
                limitedIssues[0].AssetPath,
                Is.EqualTo(TestTexturePath),
                "达到返回上限时，应该保留稳定排序后的第一条问题。");
        }

        /// <summary>
        /// 创建测试专用的内存规则。
        /// 不修改项目中的DefaultUiTextureRules.asset，
        /// 保证测试结果不受开发者当前Inspector配置影响。
        /// </summary>
        private static TextureGovernanceRuleSet CreateTestRuleSet()
        {
            TextureGovernanceRuleSet testRuleSet =
                ScriptableObject.CreateInstance<TextureGovernanceRuleSet>();

            // SerializedObject允许测试设置规则对象的私有序列化字段，
            // 不需要为了测试把生产字段改成public。
            var serializedRuleSet =
                new SerializedObject(testRuleSet);

            SerializedProperty versionProperty =
                serializedRuleSet.FindProperty("ruleVersion");

            SerializedProperty maxSizeProperty =
                serializedRuleSet.FindProperty("maxTextureSize");

            Assert.That(
                versionProperty,
                Is.Not.Null,
                "没有找到ruleVersion序列化字段。");

            Assert.That(
                maxSizeProperty,
                Is.Not.Null,
                "没有找到maxTextureSize序列化字段。");

            versionProperty.stringValue = "test-1.0.0";
            maxSizeProperty.intValue = RuleMaxSize;

            serializedRuleSet.ApplyModifiedPropertiesWithoutUndo();

            return testRuleSet;
        }

        /// <summary>
        /// 创建一张很小的PNG，并设置它的Importer Max Size。
        /// PNG实际尺寸只有4×4；测试检查的是Importer配置，
        /// 并不是图片的实际像素尺寸或磁盘大小。
        /// </summary>
        /// <param name="assetPath">
        /// 必填。测试Texture在Unity工程中的Assets路径。
        /// </param>
        /// <param name="importerMaxSize">
        /// 必填。需要写入TextureImporter的Max Size测试值。
        /// </param>
        private static void CreateTestTexture(
            string assetPath, // 测试资源路径。
            int importerMaxSize) // 测试Importer Max Size。
        {
            var texture = new Texture2D(
                4,
                4,
                TextureFormat.RGBA32,
                false);

            try
            {
                Color[] pixels = new Color[4 * 4];

                for (int index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = Color.white;
                }

                texture.SetPixels(pixels);
                texture.Apply();

                byte[] pngBytes = texture.EncodeToPNG();

                // 将Unity资源路径转换为系统绝对路径后写入PNG。
                string absolutePath = Path.GetFullPath(assetPath);
                File.WriteAllBytes(absolutePath, pngBytes);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath)
                    as TextureImporter;

            Assert.That(
                importer,
                Is.Not.Null,
                "测试PNG导入后应该能够取得TextureImporter。");

            importer.maxTextureSize = importerMaxSize;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// 删除测试自动生成的目录，避免测试数据残留在项目中。
        /// </summary>
        private static void DeleteGeneratedTestFolder()
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
            {
                return;
            }

            bool deleted = AssetDatabase.DeleteAsset(TestFolder);

            Assert.That(
                deleted,
                Is.True,
                "测试结束后应成功删除自动生成的资源目录。");

            AssetDatabase.Refresh();
        }
    }
}