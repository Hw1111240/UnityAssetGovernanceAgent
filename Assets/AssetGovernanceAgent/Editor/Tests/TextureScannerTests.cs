using System.Collections.Generic;
using System.IO;
using AssetGovernanceAgent.Editor.Models;
using AssetGovernanceAgent.Editor.Rules;
using AssetGovernanceAgent.Editor.Scanners;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System;
using AssetGovernanceAgent.Editor.Services;
using Object = UnityEngine.Object;
using AssetGovernanceAgent.Editor.Approvals;

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
                ImporterMaxSize); // importerMaxSize：设置为4096，超过2048规则。

            var scanner = new TextureScanner();

            // 先证明测试目录中确实存在两张违规Texture。
            IReadOnlyList<GovernanceIssue> allIssues =
                scanner.ScanMaxSize(
                    ruleSet, // ruleSet：测试规则，Max Size上限为2048。
                    TestFolder, // searchPath：测试资源所在目录。
                    true, // includeSubdirectories：允许扫描子目录。
                    100); // maxResults：最多返回100条，确保本次结果不被截断。

            Assert.That(
                allIssues.Count,
                Is.EqualTo(2),
                "测试目录中应该存在两张违规Texture。");

            // 将返回上限设置为1，验证扫描器会限制结果数量。
            IReadOnlyList<GovernanceIssue> limitedIssues =
                scanner.ScanMaxSize(
                    ruleSet, // ruleSet：使用同一份测试规则。
                    TestFolder, // searchPath：扫描同一个测试目录。
                    true, // includeSubdirectories：允许扫描子目录。
                    1); // maxResults：最多只能返回1条问题。

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
        /// 验证Dry Run通过后只会返回等待审批，
        /// 不会修改Texture Importer的Max Size。
        /// </summary>
        [Test]
        public void DryRun_WhenRequestIsValid_DoesNotModifyImporter()
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(TestTexturePath)
                    as TextureImporter;

            Assert.That(
                importer,
                Is.Not.Null,
                "测试Texture应该能够取得TextureImporter。");

            int maxSizeBeforeDryRun =
                importer.maxTextureSize;

            var request = CreateFixRequest(
                RuleMaxSize); // 将4096修复到规则上限2048。

            var service = new TextureMaxSizeFixService();

            TextureMaxSizeFixResult result =
                service.DryRun(
                    request, // 已验证的强类型修复请求。
                    ruleSet); // 当前实际使用的规则。

            TextureImporter importerAfterDryRun =
                AssetImporter.GetAtPath(TestTexturePath)
                    as TextureImporter;

            Assert.That(
                result.Status,
                Is.EqualTo(TextureMaxSizeFixStatus.AwaitingApproval),
                "有效违规请求在Dry Run后应等待本地审批。");

            Assert.That(
                result.RequiresApproval,
                Is.True,
                "Dry Run通过后应该要求本地审批。");

            Assert.That(
                result.WasModified,
                Is.False,
                "Dry Run不能修改任何资源。");

            Assert.That(
                result.ObservedMaxSize,
                Is.EqualTo(ImporterMaxSize),
                "Dry Run应该返回Unity实际读取到的当前Max Size。");

            Assert.That(
                importerAfterDryRun.maxTextureSize,
                Is.EqualTo(maxSizeBeforeDryRun),
                "Dry Run前后Importer Max Size必须完全不变。");
        }

        /// <summary>
        /// 验证旧请求在资源已经达到目标值时，
        /// 返回NoChange且不再次修改、重新导入资源。
        /// </summary>
        [Test]
        public void DryRun_WhenTextureAlreadyMatchesTarget_ReturnsNoChange()
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(TestTexturePath)
                    as TextureImporter;

            Assert.That(
                importer,
                Is.Not.Null,
                "测试Texture应该能够取得TextureImporter。");

            // 模拟其他操作已经将4096修复为2048，
            // 当前Dry Run收到的是一条旧的修复请求。
            importer.maxTextureSize = RuleMaxSize;
            importer.SaveAndReimport();

            var request = CreateFixRequest(
                RuleMaxSize); // 旧请求的目标仍然是2048。

            var service = new TextureMaxSizeFixService();

            TextureMaxSizeFixResult result =
                service.DryRun(
                    request, // 原始修复请求。
                    ruleSet); // 当前规则。

            TextureImporter importerAfterDryRun =
                AssetImporter.GetAtPath(TestTexturePath)
                    as TextureImporter;

            Assert.That(
                result.Status,
                Is.EqualTo(TextureMaxSizeFixStatus.NoChange),
                "资源已经符合目标值时，应返回NoChange。");

            Assert.That(
                result.IsSuccessful,
                Is.True,
                "NoChange表示目标已经达到，属于成功结果。");

            Assert.That(
                result.WasModified,
                Is.False,
                "NoChange不能再次修改或重新导入资源。");

            Assert.That(
                importerAfterDryRun.maxTextureSize,
                Is.EqualTo(RuleMaxSize),
                "Dry Run后资源仍应保持目标Max Size。");
        }


        /// <summary>
        /// 验证通过Dry Run但尚未批准的请求，
        /// 不能被审批仓库视为可执行请求。
        /// </summary>
        [Test]
        public void ApprovalStore_WhenRequestIsPending_IsNotExecutable()
        {
            var request = CreateFixRequest(
                RuleMaxSize); // 目标值为2048。

            var fixService = new TextureMaxSizeFixService();

            TextureMaxSizeFixResult dryRunResult =
                fixService.DryRun(
                    request, // 已生成的修复请求。
                    ruleSet); // 当前测试规则。

            TextureMaxSizeFixApprovalRecord pendingRecord =
                TextureMaxSizeFixApprovalStore.RegisterPending(
                    dryRunResult); // Dry Run通过后登记待审批记录。

            bool canExecute =
                TextureMaxSizeFixApprovalStore.TryGetApproved(
                    request, // 尚未批准的原始请求。
                    out TextureMaxSizeFixApprovalRecord approvedRecord);

            Assert.That(
                pendingRecord.Decision,
                Is.EqualTo(TextureMaxSizeApprovalDecision.Pending),
                "新登记的审批记录应该处于Pending状态。");

            Assert.That(
                canExecute,
                Is.False,
                "Pending状态不能取得可执行授权。");

            Assert.That(
                approvedRecord,
                Is.Null,
                "未批准请求不应该返回已批准记录。");
        }

        /// <summary>
        /// 验证用户批准一条请求后，
        /// 使用相同OperationId但篡改目标值的请求仍不能取得授权。
        /// </summary>
        [Test]
        public void ApprovalStore_WhenApprovedRequestIsChanged_RejectsExecution()
        {
            var approvedRequest = CreateFixRequest(
                RuleMaxSize); // 原始批准目标为2048。

            var fixService = new TextureMaxSizeFixService();

            TextureMaxSizeFixResult dryRunResult =
                fixService.DryRun(
                    approvedRequest, // 原始修复请求。
                    ruleSet); // 当前测试规则。

            TextureMaxSizeFixApprovalStore.RegisterPending(
                dryRunResult); // 先登记为待审批。

            TextureMaxSizeFixApprovalRecord approvedRecord =
                TextureMaxSizeFixApprovalStore.Approve(
                    approvedRequest, // 只批准当前这条完整请求。
                    "EditMode测试批准"); // 测试备注。

            var changedRequest = new TextureMaxSizeFixRequest(
                approvedRequest.OperationId, // 故意复用相同操作编号。
                approvedRequest.IssueId, // 保持相同问题编号。
                approvedRequest.RuleId, // 保持相同规则编号。
                approvedRequest.RuleVersion, // 保持相同规则版本。
                approvedRequest.AssetGuid, // 保持相同资源GUID。
                approvedRequest.AssetPath, // 保持相同资源路径。
                approvedRequest.ExpectedCurrentMaxSize, // 保持扫描快照值4096。
                1024); // 故意把目标值从2048篡改为1024。

            bool canExecuteChangedRequest =
                TextureMaxSizeFixApprovalStore.TryGetApproved(
                    changedRequest, // 与已批准请求参数不一致的候选请求。
                    out TextureMaxSizeFixApprovalRecord matchedRecord);

            Assert.That(
                approvedRecord.CanExecute,
                Is.True,
                "原始批准记录应该允许执行。");

            Assert.That(
                canExecuteChangedRequest,
                Is.False,
                "即使OperationId相同，目标值被篡改后也必须拒绝执行。");

            Assert.That(
                matchedRecord,
                Is.Null,
                "参数不一致的请求不应该返回已批准记录。");
        }


        /// <summary>
        /// 待审批或已拒绝的请求都不能写入。
        /// </summary>
        [TestCase(false)] // 保持待审批。
        [TestCase(true)] // 模拟用户拒绝。
        public void ExecuteApproved_WhenNotApproved_DoesNotWrite(
            bool rejectRequest) // 是否先拒绝请求。
        {
            var service = new TextureMaxSizeFixService();
            var request = CreateFixRequest(RuleMaxSize); // 目标2048。

            var preview = service.DryRun(
                request, // 当前请求。
                ruleSet); // 当前规则。

            TextureMaxSizeFixApprovalStore.RegisterPending(preview);

            if (rejectRequest)
            {
                TextureMaxSizeFixApprovalStore.Reject(
                    request, // 待审批请求。
                    "测试拒绝"); // 拒绝原因。
            }

            var result = service.ExecuteApproved(
                request, // 尚未获得批准。
                ruleSet); // 当前规则。

            Assert.That(result.Status,
                Is.EqualTo(TextureMaxSizeFixStatus.Failed));

            Assert.That(result.ErrorCode,
                Is.EqualTo("APPROVAL_REQUIRED_OR_MISMATCH"));

            Assert.That(result.WriteAttempted, Is.False,
                "未批准时不能进入写入阶段。");

            Assert.That(GetTestImporter().maxTextureSize,
                Is.EqualTo(ImporterMaxSize),
                "Max Size应保持4096。");
        }

        /// <summary>
        /// 批准后修复成功，重复调用不再写入。
        /// </summary>
        [Test]
        public void ExecuteApproved_WhenApproved_AppliesAndRepeatReturnsNoChange()
        {
            var service = new TextureMaxSizeFixService();
            var request = CreateFixRequest(RuleMaxSize); // 目标2048。

            var preview = service.DryRun(
                request, // 当前请求。
                ruleSet); // 当前规则。

            TextureMaxSizeFixApprovalStore.RegisterPending(preview);

            // 测试中模拟用户批准；正式流程由Unity窗口触发。
            TextureMaxSizeFixApprovalStore.Approve(
                request, // 批准完整请求。
                "测试批准"); // 审批备注。

            var firstResult = service.ExecuteApproved(
                request, // 已批准请求。
                ruleSet); // 当前规则。

            Assert.That(firstResult.Status,
                Is.EqualTo(TextureMaxSizeFixStatus.Applied),
                firstResult.Message);

            Assert.That(firstResult.WriteAttempted, Is.True);
            Assert.That(firstResult.HasObservedMaxSize, Is.True);
            Assert.That(firstResult.ObservedMaxSize, Is.EqualTo(RuleMaxSize));

            Assert.That(GetTestImporter().maxTextureSize,
                Is.EqualTo(RuleMaxSize),
                "实际Importer应已修改为2048。");

            // 独立调用扫描器，确认违规问题已消失。
            var issues = new TextureScanner().ScanMaxSize(
                ruleSet, // 当前规则。
                TestFolder, // 仅检查测试目录。
                true, // 包含子目录。
                100); // 返回上限。

            Assert.That(issues, Is.Empty, "修复后不应再报告Max Size违规。");

            // 复用同一请求，不生成新的OperationId。
            var secondResult = service.ExecuteApproved(
                request, // 重复请求。
                ruleSet); // 同一规则。

            Assert.That(secondResult.Status,
                Is.EqualTo(TextureMaxSizeFixStatus.NoChange));

            Assert.That(secondResult.WriteAttempted, Is.False,
                "第二次调用不应再次进入写入阶段。");

            Assert.That(GetTestImporter().maxTextureSize,
                Is.EqualTo(RuleMaxSize));
        }

        /// <summary>
        /// 批准后资源变化，旧计划不能覆盖新配置。
        /// </summary>
        [Test]
        public void ExecuteApproved_WhenStateChangesAfterApproval_DoesNotWrite()
        {
            var service = new TextureMaxSizeFixService();
            var request = CreateFixRequest(RuleMaxSize); // 快照4096，目标2048。

            var preview = service.DryRun(
                request, // 当前请求。
                ruleSet); // 当前规则。

            TextureMaxSizeFixApprovalStore.RegisterPending(preview);

            TextureMaxSizeFixApprovalStore.Approve(
                request, // 批准当前计划。
                "测试批准"); // 审批备注。

            // 模拟批准后，其他操作把配置改成8192。
            var importer = GetTestImporter();
            importer.maxTextureSize = 8192;
            importer.SaveAndReimport();

            var result = service.ExecuteApproved(
                request, // 仍携带4096的旧快照。
                ruleSet); // 当前规则。

            Assert.That(result.Status,
                Is.EqualTo(TextureMaxSizeFixStatus.Failed));

            Assert.That(result.ErrorCode,
                Is.EqualTo("RESOURCE_STATE_CHANGED"));

            Assert.That(result.WriteAttempted, Is.False,
                "旧计划不能进入写入阶段。");

            Assert.That(result.HasObservedMaxSize, Is.True);
            Assert.That(result.ObservedMaxSize, Is.EqualTo(8192));

            Assert.That(GetTestImporter().maxTextureSize,
                Is.EqualTo(8192),
                "不能把新配置覆盖成旧计划的目标值。");
        }

        /// <summary>
        /// 取得当前测试纹理的Importer。
        /// </summary>
        private static TextureImporter GetTestImporter()
        {
            var importer =
                AssetImporter.GetAtPath(TestTexturePath) as TextureImporter;

            Assert.That(importer, Is.Not.Null, "测试纹理Importer不存在。");

            return importer;
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

        /// <summary>
        /// 创建针对当前测试Texture的Max Size修复请求。
        /// 请求快照始终记录扫描阶段的4096，
        /// 用于模拟真实项目中“先扫描，再生成修复计划”的流程。
        /// </summary>
        private TextureMaxSizeFixRequest CreateFixRequest(
            int targetMaxSize) // 必填。期望修复到的Max Size，例如2048。
        {
            string assetGuid =
                AssetDatabase.AssetPathToGUID(TestTexturePath);

            return new TextureMaxSizeFixRequest(
                Guid.NewGuid().ToString(), // operationId：每次请求使用新的操作编号。
                $"{TextureRuleIds.MaxTextureSize}:{assetGuid}", // issueId：规则与资源GUID组成的问题编号。
                TextureRuleIds.MaxTextureSize, // ruleId：当前只处理Max Size规则。
                ruleSet.RuleVersion, // ruleVersion：使用测试规则的实际版本。
                assetGuid, // assetGuid：测试Texture的稳定资源标识。
                TestTexturePath, // assetPath：生成请求时记录的路径快照。
                ImporterMaxSize, // expectedCurrentMaxSize：扫描时记录的4096。
                targetMaxSize); // targetMaxSize：本次希望修复到的目标值。
        }
    }
}