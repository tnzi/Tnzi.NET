using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Controllers;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Controllers;

/// <summary>
/// <c>GET admin/diagnostics/admin-manifest</c>：清单里的实体路由是控制器级模板、不带 API 前缀。
/// </summary>
/// <remarks>
/// 前两条跑真实宿主：action 模板经过真实的 <c>RoutePrefixConvention</c>，默认配置下是
/// <c>api/admin/...</c>。只喂手写的 action 描述符证不了这一点，默认配置下清单恒为空的缺陷正是这样漏掉的。
/// </remarks>
public class AdminManifestControllerTests
{
    [Fact]
    public async Task DefaultApiPrefix_ListsAdminControllers_WithControllerLevelRoute()
    {
        var manifest = await GetManifestAsync(apiPathPrefix: null);

        var probe = FindEntity(manifest, typeof(AdminManifestProbeController));
        Assert.NotNull(probe);
        Assert.Equal("admin/manifest-probe", probe!.Route);
        Assert.Equal("manifest-probe", probe.Name);
        Assert.Equal(["DELETE", "GET", "POST", "PUT"], probe.Methods);
        Assert.True(probe.HasFullCrud);
    }

    [Fact]
    public async Task EmptyApiPrefix_EntityRouteIsTheControllerTemplate_NotTheFirstActionTemplate()
    {
        // 探针控制器第一个声明的 action 是 items/{id}：按「第一个 action 的模板」取路由会得到
        // admin/manifest-probe/items/{id}，菜单会指向一个 action 路径。
        var manifest = await GetManifestAsync(apiPathPrefix: "");

        var probe = FindEntity(manifest, typeof(AdminManifestProbeController));
        Assert.NotNull(probe);
        Assert.Equal("admin/manifest-probe", probe!.Route);
        Assert.Equal("manifest-probe", probe.Name);
    }

    [Fact]
    public async Task ControllerTemplateWithRouteParameter_IsNotListed()
    {
        // 子资源（父实体 id 在路由里）没有可直接导航的页面。
        var manifest = await GetManifestAsync(apiPathPrefix: null);

        Assert.Null(FindEntity(manifest, typeof(AdminManifestNestedProbeController)));
        Assert.NotNull(FindEntity(manifest, typeof(AdminManifestProbeController)));
    }

    [Fact]
    public void ControllerWithoutRouteAttribute_FallsBackToTheCommonLiteralPrefixOfItsActions()
    {
        var controller = new DefaultDiagnosticsAdminController(
            new ExceptionStatisticsService(new ExceptionStatistics(maxHistorySize: 1)));
        var app = new Mock<ITnziApplication>();
        app.SetupGet(a => a.Modules).Returns([]);

        var manifest = controller.GetAdminManifest(
            app.Object,
            StubActionProvider.With(
                Descriptor("api/admin/loose/{id}", "GET"),
                Descriptor("api/admin/loose/list", "POST")),
            Microsoft.Extensions.Options.Options.Create(new AspNetCoreOptions { ApiPathPrefix = "/api" })).Data!;

        var entity = Assert.Single(Assert.Single(manifest.Modules).Entities);
        Assert.Equal("admin/loose", entity.Route);
        Assert.Equal("loose", entity.Name);
    }

    [Fact]
    public void SeveralModulesInOneAssembly_FoldIntoOneModuleEntry_EnabledIfAny()
    {
        // 核心程序集 Tnzi 一个就声明了五个模块；按程序集建字典会在任何真实宿主上抛异常。
        var controller = new DefaultDiagnosticsAdminController(
            new ExceptionStatisticsService(new ExceptionStatistics(maxHistorySize: 1)));
        var app = new Mock<ITnziApplication>();
        app.SetupGet(a => a.Modules).Returns(
        [
            ModuleIn(typeof(LooseProbe).Assembly, enabled: false),
            ModuleIn(typeof(LooseProbe).Assembly, enabled: true)
        ]);

        var manifest = controller.GetAdminManifest(
            app.Object,
            StubActionProvider.With(Descriptor("admin/loose", "GET")),
            Microsoft.Extensions.Options.Options.Create(new AspNetCoreOptions { ApiPathPrefix = "" })).Data!;

        var module = Assert.Single(manifest.Modules);
        Assert.True(module.IsEnabled);
        Assert.Equal("admin/loose", Assert.Single(module.Entities).Route);
    }

    private static IModuleDescriptor ModuleIn(Assembly assembly, bool enabled)
    {
        var descriptor = new Mock<IModuleDescriptor>();
        descriptor.SetupGet(d => d.Assembly).Returns(assembly);
        descriptor.SetupGet(d => d.Type).Returns(typeof(AdminManifestProbeStartupModule));
        descriptor.SetupGet(d => d.IsEnabled).Returns(enabled);
        return descriptor.Object;
    }

    private static AdminEntityEntryDto? FindEntity(AdminManifestDto manifest, Type controllerType)
        => manifest.Modules
            .SelectMany(m => m.Entities)
            .SingleOrDefault(e => e.ControllerType == controllerType.FullName);

    private static async Task<AdminManifestDto> GetManifestAsync(string? apiPathPrefix)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        var settings = new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false"
        };
        if (apiPathPrefix != null)
        {
            settings["AspNetCore:ApiPathPrefix"] = apiPathPrefix;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        var app = await TnziApp.CreateAsync<AdminManifestProbeStartupModule>(builder);
        await app.StartAsync();
        try
        {
            var controller = new DefaultDiagnosticsAdminController(
                app.Services.GetRequiredService<IExceptionStatisticsService>());

            var result = controller.GetAdminManifest(
                app.Services.GetRequiredService<ITnziApplication>(),
                app.Services.GetRequiredService<IActionDescriptorCollectionProvider>(),
                app.Services.GetRequiredService<IOptions<AspNetCoreOptions>>());

            Assert.True(result.Success);
            return result.Data!;
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static ControllerActionDescriptor Descriptor(string template, string httpMethod)
        => new()
        {
            MethodInfo = typeof(LooseProbe).GetMethod(nameof(LooseProbe.Action))!,
            ControllerTypeInfo = typeof(LooseProbe).GetTypeInfo(),
            ActionName = nameof(LooseProbe.Action),
            AttributeRouteInfo = new AttributeRouteInfo { Template = template },
            ActionConstraints = [new HttpMethodActionConstraint([httpMethod])]
        };

    /// <summary>没有类级 [Route] 的控制器。</summary>
    public class LooseProbe
    {
        public void Action()
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

[DependsOn(typeof(AspNetCoreModule))]
public sealed class AdminManifestProbeStartupModule : TnziCustomModule
{
}

/// <summary>
/// 第一个声明的 action 带参数且路径比控制器模板长：清单必须报控制器模板。
/// </summary>
[ApiController]
[Route("admin/manifest-probe")]
public sealed class AdminManifestProbeController : ApiControllerBase
{
    [HttpGet("items/{id}")]
    public ApiResult<string> Get(string id) => Ok(id, "Success");

    [HttpGet]
    public ApiResult<string> List() => Ok("list", "Success");

    [HttpPost]
    public ApiResult<string> Create() => Ok("created", "Success");

    [HttpPut("{id}")]
    public ApiResult<string> Update(string id) => Ok(id, "Success");

    [HttpDelete("{id}")]
    public ApiResult<string> Delete(string id) => Ok(id, "Success");
}

[ApiController]
[Route("admin/manifest-probe/{parentId}/children")]
public sealed class AdminManifestNestedProbeController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> List(string parentId) => Ok(parentId, "Success");
}
