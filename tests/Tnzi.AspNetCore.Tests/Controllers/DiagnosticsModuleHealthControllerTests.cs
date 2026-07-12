using Tnzi.AspNetCore.Controllers;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Controllers;

/// <summary>
/// DefaultDiagnosticsAdminController.GetModuleHealth 端点测试
/// 验证端点接线（ModuleHealthChecker + ITnziApplication.Modules）与 DTO 映射
/// （Type -> 简单名、枚举 -> 字符串）。
/// </summary>
public class DiagnosticsModuleHealthControllerTests
{
    [Fact]
    public void GetModuleHealth_WhenDependencyMissing_ReturnsUnhealthyReportWithMappedIssue()
    {
        // 只提供 HealthTestModule，其依赖 HealthTestDepModule 缺失
        var modules = new List<IModuleDescriptor>
        {
            new ModuleDescriptor(typeof(HealthTestModule), new HealthTestModule())
        };
        var app = new Mock<ITnziApplication>();
        app.SetupGet(a => a.Modules).Returns(modules);

        var controller = new DefaultDiagnosticsAdminController(Mock.Of<IExceptionStatisticsService>());

        var result = controller.GetModuleHealth(app.Object, new ModuleHealthChecker());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data!.IsHealthy);
        Assert.Equal(result.Data.Issues.Count, result.Data.IssueCount);

        // 依赖缺失问题被映射为字符串类型名，模块名为简单名
        Assert.Contains(result.Data.Issues,
            i => i.IssueType == nameof(ModuleHealthIssueType.MissingDependency)
                 && i.Module == nameof(HealthTestModule)
                 && i.MissingDependencies.Contains(nameof(HealthTestDepModule)));
    }

    private sealed class HealthTestDepModule : TnziCoreModule
    {
    }

    [DependsOn(typeof(HealthTestDepModule))]
    private sealed class HealthTestModule : TnziCoreModule
    {
    }
}
