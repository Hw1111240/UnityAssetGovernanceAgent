using System.Runtime.CompilerServices;

// 只允许当前EditMode测试程序集访问internal代码。
// 不会把审批仓库开放给运行时代码或Agent。
[assembly: InternalsVisibleTo(
    "AssetGovernanceAgent.Editor.Tests")]