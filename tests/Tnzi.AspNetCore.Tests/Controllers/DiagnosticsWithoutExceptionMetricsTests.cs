using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Controllers;
using Tnzi.AspNetCore.Mvc.Conventions;

namespace Tnzi.AspNetCore.Tests.Controllers;

/// <summary>
/// <c>AspNetCore:ExceptionHandling:EnableMetrics=false</c> 时不注册异常统计服务。诊断控制器此前把它当必填依赖，
/// 条件控制器提供者于是移除整个控制器：敏感端点清单、模块清单、admin 清单跟着全部 404。
/// </summary>
public class DiagnosticsWithoutExceptionMetricsTests
{
    [Fact]
    public void WithoutExceptionStatistics_TheDiagnosticsControllerStaysInTheRouteTable()
    {
        // 服务集合里没有 IExceptionStatisticsService，等同于 EnableMetrics=false
        var provider = new ConditionalControllerProvider(new ServiceCollection(), new ControllerActivationDiagnostics());
        var type = typeof(DefaultDiagnosticsAdminController).GetTypeInfo();
        var context = new ApplicationModelProviderContext([type]);
        context.Result.Controllers.Add(new ControllerModel(type, []));

        provider.OnProvidersExecuting(context);

        Assert.Single(context.Result.Controllers);
    }

    [Fact]
    public void WithoutExceptionStatistics_TheExceptionEndpointsAnswer501()
    {
        var controller = new DefaultDiagnosticsAdminController(exceptionStatisticsService: null);

        Assert.Equal(501, controller.GetExceptionSummary().Code);
        Assert.Equal(501, controller.GetRecentExceptions().Code);
        Assert.Equal(501, controller.ClearExceptions().Code);
    }

    [Fact]
    public void WithExceptionStatistics_TheExceptionEndpointsStillWork()
    {
        var controller = new DefaultDiagnosticsAdminController(
            new ExceptionStatisticsService(new ExceptionStatistics(maxHistorySize: 10)));

        Assert.Equal(200, controller.GetExceptionSummary().Code);
        Assert.Equal(200, controller.GetRecentExceptions().Code);
        Assert.Equal(200, controller.ClearExceptions().Code);
    }
}
