using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Tnzi.AspNetCore.Controllers;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// 自省清单：把"这个部署开着哪些敏感端点"从一次人工审查变成一次查询。
/// </summary>
public class SensitiveEndpointDiagnosticsTests
{
    [Fact]
    public void Report_ListsOnlyMarkedEndpoints_WithTheirReason()
    {
        var controller = CreateController();

        var report = controller.GetSensitiveEndpoints(
            StubActionProvider.With(
                Descriptor(nameof(Probe.MintToken), "files/{id}/access-token", "GET"),
                Descriptor(nameof(Probe.Ordinary), "files/{id}", "GET"))).Data!;

        var only = Assert.Single(report.Endpoints);
        Assert.Equal(1, report.TotalCount);
        Assert.Equal("test.portable", only.Name);
        Assert.Equal("files/{id}/access-token", only.Route);
        Assert.Equal("GET", only.HttpMethod);
        // 理由必须随清单一起出来：一行只有名字的清单没法支撑安全评审。
        Assert.Contains("leave the controlled environment", only.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_ReflectsTheLiveRouteTable_NotAssemblyScan()
    {
        var controller = CreateController();

        // 模拟"这个端点已被 ControllerFilter:DisabledEndpoints 摘掉"：
        // 它不在生效的路由表里，因此也不该出现在清单里。
        // ★ 这条锁住的是数据源的选择：如果改成扫程序集找 [SensitiveEndpoint]，
        // 被抑制的端点会照样列出来，"我以为我关掉了"就再也无法被证伪。
        var report = controller.GetSensitiveEndpoints(
            StubActionProvider.With(Descriptor(nameof(Probe.Ordinary), "files/{id}", "GET"))).Data!;

        Assert.Empty(report.Endpoints);
    }

    [Fact]
    public void Report_FlagsAnonymouslyReachableEndpoints()
    {
        var controller = CreateController();

        var report = controller.GetSensitiveEndpoints(
            StubActionProvider.With(
                Descriptor(nameof(Probe.ShareLink), "files/share/{token}", "GET", anonymous: true))).Data!;

        // 匿名可达的敏感端点是清单里最该先看的一行。
        Assert.True(Assert.Single(report.Endpoints).AllowsAnonymous);
    }

    private static DefaultDiagnosticsAdminController CreateController()
        => new(new ExceptionStatisticsService(new ExceptionStatistics(maxHistorySize: 1)));

    private static ControllerActionDescriptor Descriptor(
        string methodName,
        string template,
        string httpMethod,
        bool anonymous = false)
        => new()
        {
            MethodInfo = typeof(Probe).GetMethod(methodName)!,
            ControllerTypeInfo = typeof(Probe).GetTypeInfo(),
            ActionName = methodName,
            AttributeRouteInfo = new AttributeRouteInfo { Template = template },
            ActionConstraints = [new HttpMethodActionConstraint([httpMethod])],
            EndpointMetadata = anonymous ? [new AllowAnonymousAttribute()] : []
        };

    public class Probe
    {
        [SensitiveEndpoint("test.portable", "Issues a credential that can leave the controlled environment.")]
        public void MintToken()
        {
        }

        [SensitiveEndpoint("test.share", "Serves content to unauthenticated callers.")]
        public void ShareLink()
        {
        }

        public void Ordinary()
        {
        }
    }

    private sealed class StubActionProvider : IActionDescriptorCollectionProvider
    {
        private StubActionProvider(ActionDescriptorCollection collection) => ActionDescriptors = collection;

        public ActionDescriptorCollection ActionDescriptors { get; }

        public static StubActionProvider With(params ActionDescriptor[] descriptors)
            => new(new ActionDescriptorCollection(descriptors, version: 1));
    }
}
