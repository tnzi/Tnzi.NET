
namespace Tnzi.Tests.Modules;

/// <summary>
/// ModuleHealthChecker 服务级测试
/// 验证依赖完整性检查与初始化状态检查。
/// </summary>
public class ModuleHealthCheckerTests
{
    [Fact]
    public void CheckDependencies_WhenAllDependenciesPresent_IsHealthy()
    {
        // TestModuleB 依赖 TestModuleA；两者都在列表中 → 依赖完整
        var modules = new List<IModuleDescriptor>
        {
            new ModuleDescriptor(typeof(TestModuleA), new TestModuleA()),
            new ModuleDescriptor(typeof(TestModuleB), new TestModuleB())
        };
        var checker = new ModuleHealthChecker();

        var result = checker.CheckDependencies(modules);

        Assert.True(result.IsHealthy);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void CheckDependencies_WhenDependencyMissing_ReportsMissingDependencyIssue()
    {
        // 只提供 TestModuleB，缺失其依赖 TestModuleA
        var modules = new List<IModuleDescriptor>
        {
            new ModuleDescriptor(typeof(TestModuleB), new TestModuleB())
        };
        var checker = new ModuleHealthChecker();

        var result = checker.CheckDependencies(modules);

        Assert.False(result.IsHealthy);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ModuleHealthIssueType.MissingDependency, issue.IssueType);
        Assert.Equal(typeof(TestModuleB), issue.ModuleType);
        Assert.NotNull(issue.MissingDependencies);
        Assert.Contains(typeof(TestModuleA), issue.MissingDependencies!);
    }

    [Fact]
    public void CheckInitializationStatus_WhenModulesNotInitialized_ReportsNotInitialized()
    {
        // 新建描述符尚未初始化（IsEnabled=false, InitializationEndTime=null）
        var modules = new List<IModuleDescriptor>
        {
            new ModuleDescriptor(typeof(TestModuleA), new TestModuleA())
        };
        var checker = new ModuleHealthChecker();

        var result = checker.CheckInitializationStatus(modules);

        Assert.False(result.IsHealthy);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ModuleHealthIssueType.NotInitialized, issue.IssueType);
    }

    [Fact]
    public void CheckAll_WhenDependencyMissing_IsUnhealthyAndContainsDependencyIssue()
    {
        var modules = new List<IModuleDescriptor>
        {
            new ModuleDescriptor(typeof(TestModuleB), new TestModuleB())
        };
        var checker = new ModuleHealthChecker();

        var result = checker.CheckAll(modules);

        Assert.False(result.IsHealthy);
        Assert.Contains(result.Issues, i => i.IssueType == ModuleHealthIssueType.MissingDependency);
    }
}
