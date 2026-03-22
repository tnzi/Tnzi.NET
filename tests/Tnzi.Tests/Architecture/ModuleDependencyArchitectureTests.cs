using Tnzi.Hosting;
using Tnzi.TestBase;

namespace Tnzi.Tests.Architecture;

/// <summary>
/// Architecture gate: all cross-module dependencies must be declared via [DependsOn]
/// </summary>
public class ModuleDependencyArchitectureTests
{
    /// <summary>
    /// 测试用启动模块（HostingModule 是 abstract，需要具体子类）
    /// </summary>
    private sealed class TestStartupModule : HostingModule;

    [Fact]
    public void AllCrossModuleDependencies_ShouldBeDeclared()
    {
        // Load all modules from HostingModule
        var (modules, serviceMap) = ModuleTestHelper.LoadAndCollectServiceMap<TestStartupModule>();

        // Run audit
        var violations = ModuleDependencyAuditor.AuditAndReport(modules, serviceMap);

        // Assert
        if (violations.Count > 0)
        {
            var report = string.Join(Environment.NewLine,
                violations.Select(v => $"  - {v.Message}"));
            Assert.Fail(
                $"Found {violations.Count} undeclared cross-module dependency violation(s):{Environment.NewLine}{report}");
        }
    }
}
