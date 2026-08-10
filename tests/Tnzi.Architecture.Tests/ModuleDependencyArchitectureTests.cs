using Tnzi.Modules.Diagnostics;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 架构门禁：跨模块依赖必须经 <c>[DependsOn]</c> 声明。
/// </summary>
/// <remarks>
/// 这道门禁曾经是<b>假绿</b>的：它从 <c>HostingModule</c> 出发加载模块，而 Hosting 对
/// 30 个业务模块用的是 <c>[OptionalDependsOn]</c>（只排序不发现），于是实际只加载 12 个模块，
/// 其中还有 2 个因夹具异常被静默跳过 —— 真实审计面是 10 个基础设施模块，
/// Identity / Storage / Finance / AI / Signing 从未被审计过一次。
/// 现在改用 <see cref="AllModulesStartupModule"/>（<c>[DependsOn]</c> 全量）。
/// </remarks>
public class ModuleDependencyArchitectureTests
{
    [Fact]
    public void AllCrossModuleDependencies_ShouldBeDeclared()
    {
        var result = ArchitectureModuleGraph.Load();

        AssertNoConfigurationFailures(result);

        var violations = ModuleDependencyAuditor.AuditAndReport(result.Modules, result.ServiceMap);

        if (violations.Count > 0)
        {
            var report = string.Join(Environment.NewLine,
                violations.Select(v => $"  - {v.Message}"));
            Assert.Fail(
                $"Found {violations.Count} undeclared cross-module dependency violation(s):{Environment.NewLine}{report}");
        }
    }

    /// <summary>
    /// 配置阶段的异常必须让测试变红，而不是让被跳过的模块悄悄退出审计。
    /// </summary>
    /// <remarks>
    /// 单列一个用例而不是只在上面断言：模块配置失败与依赖声明缺失是两类问题，
    /// 混在一条断言里会让「夹具坏了」被读成「有依赖违规」。
    /// </remarks>
    [Fact]
    public void EveryModule_ConfiguresWithoutThrowing()
    {
        var result = ArchitectureModuleGraph.Load();
        AssertNoConfigurationFailures(result);
    }

    private static void AssertNoConfigurationFailures(ModuleLoadResult result)
    {
        if (result.Failures.Count == 0)
            return;

        var report = string.Join(Environment.NewLine, result.Failures.Select(f => $"  - {f}"));
        Assert.Fail(
            $"{result.Failures.Count} module(s) failed to configure - they would silently drop out of "
            + $"every audit that consumes this service map:{Environment.NewLine}{report}");
    }
}
