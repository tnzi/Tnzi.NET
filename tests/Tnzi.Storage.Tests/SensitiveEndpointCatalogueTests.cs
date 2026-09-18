using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Mvc;
using Tnzi.Storage.Controllers;
using Tnzi.Storage.Controllers.Admin;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 钉住本模块 <c>[SensitiveEndpoint]</c> 目录：哪些 action 带标记、用的是哪个能力名。
/// </summary>
/// <remarks>
/// 能力名同时是 <c>AspNetCore:ControllerFilter:DisabledEndpoints</c> 的抑制键，所以
/// **同一条能力的每个入口都必须共用一个名字**，关一次全关；漏标一个入口，那一个就成了
/// 刚关掉那条能力的旁路。08-16 标记三个端点时漏掉了 admin 侧的 presigned-url 孪生 ——
/// 它不出现在 <c>GET admin/diagnostics/sensitive-endpoints</c> 里、也不受抑制配置约束，
/// 而它恰恰是唯一一个把 HTTP 动词开放给查询串的入口（<c>?httpMethod=PUT</c> 换到对象存储的
/// 直传 URL）。这个目录测试让「少标一个」从零症状变成一条红。
/// </remarks>
public class SensitiveEndpointCatalogueTests
{
    private static readonly (Type Controller, string Action, string Capability)[] Catalogue =
    [
        (typeof(DefaultStorageController), nameof(DefaultStorageController.GetPresignedUrl), "storage.presigned-url"),
        (typeof(DefaultStorageAdminController), nameof(DefaultStorageAdminController.GetPresignedUrl), "storage.presigned-url"),
        (typeof(DefaultStorageController), nameof(DefaultStorageController.GetAccessToken), "storage.access-token"),
        (typeof(DefaultStorageController), nameof(DefaultStorageController.GetAccessTokens), "storage.access-token"),
        (typeof(DefaultStorageController), nameof(DefaultStorageController.DownloadByShareToken), "storage.share-link"),
    ];

    public static IEnumerable<object[]> CatalogueEntries()
        => Catalogue.Select(entry => new object[] { entry.Controller, entry.Action, entry.Capability });

    [Theory]
    [MemberData(nameof(CatalogueEntries))]
    public void Action_CarriesSensitiveEndpoint_WithCataloguedCapabilityName(Type controller, string action, string capability)
    {
        var attribute = controller.GetMethod(action)!.GetCustomAttribute<SensitiveEndpointAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(capability, attribute.Name);
    }

    [Fact]
    public void NoOtherActionInModule_IsMarkedSensitive()
    {
        // 目录是双向的：新标一个端点也要登记到这里，否则清单与文档就各自漂了。
        var marked = typeof(StorageModule).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<SensitiveEndpointAttribute>() != null)
                .Select(m => (Controller: t, Action: m.Name)))
            .ToHashSet();

        var catalogued = Catalogue.Select(e => (e.Controller, e.Action)).ToHashSet();

        Assert.Equal(catalogued, marked);
    }

    [Fact]
    public void AdminPresignedUrl_DoesNotLetTheCallerChooseTheVerb()
    {
        // 动词决定签出的是读凭据还是写凭据。用户端把它写死成 GET；admin 端此前把它开放给
        // 查询串，而整个 action 只挂类级 storage.file.view。一个 GET 端点签写凭据，
        // 「写端点必须带方法级操作码」的门禁按动词扫描看不见它。
        var parameters = typeof(DefaultStorageAdminController)
            .GetMethod(nameof(DefaultStorageAdminController.GetPresignedUrl))!
            .GetParameters()
            .Select(p => p.Name);

        Assert.DoesNotContain("httpMethod", parameters);
    }
}
