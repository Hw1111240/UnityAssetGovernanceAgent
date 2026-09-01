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
        private const string TestFolder =
            "Assets/AssetGovernanceAgent/TestAssetsGenerated";

        private const string TestTexturePath =
            TestFolder + "/OversizedTexture.png";

        private const int RuleMaxSize = 2048;
        private const int ImporterMaxSize = 4096;

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
                TestTexturePath,   // 自动生成的测试Texture路径。
                ImporterMaxSize);  // 设置为4096，故意超过2048规则。
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
                    ruleSet,        // 使用测试专用规则。
                    TestFolder,      // 只扫描自动生成的测试目录。
                    true,            // 允许扫描子目录。
                    100);            // 最多返回100条问题。

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
            string assetPath,      // 测试资源路径。
            int importerMaxSize)   // 测试Importer Max Size。
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