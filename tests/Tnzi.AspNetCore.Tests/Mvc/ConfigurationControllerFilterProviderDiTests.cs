using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// <c>ControllerFilter.ControllerPredicate</c> 只能用代码设（委托绑不出来），文档说的是
/// <c>PostConfigure&lt;AspNetCoreOptions&gt;</c>；而提供者此前拿的是模块自己从 IConfiguration
/// 绑出来的一份独立实例 —— 与 options 管线管的那个对象没有任何关系，谓词永远为 null。
/// 更彻底的一种：appsettings 里没有 <c>AspNetCore:ControllerFilter</c> 节时提供者连注册都没有。
/// 症状是「全部控制器照常挂在路由上」，启动日志与诊断端点一切正常。
/// </summary>
/// <remarks>
/// 既有的 <c>ConfigurationControllerFilterProviderTests</c> 直接 <c>new</c> 提供者，一次都没经过容器，
/// 所以看不见这条断线。这里起真实 TestServer，谓词经 PostConfigure 注入。
/// </remarks>
public class ConfigurationControllerFilterProviderDiTests
{
    [Fact]
    public async Task ControllerPredicate_SetViaPostConfigure_RemovesController()
    {
        await RunAsync(
            settings: [],
            predicate: type => type != typeof(PredicateVictimController),
            async client =>
            {
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/e2e/predicate-victim")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/predicate-survivor")).StatusCode);
            });
    }

    [Fact]
    public async Task ControllerPredicate_WorksWithoutAnyControllerFilterSection()
    {
        // 上一条已经没有配置节；这一条把意图写明：不需要在 appsettings 里放一个空节来「打开」提供者。
        await RunAsync(
            settings: new Dictionary<string, string?> { ["AspNetCore:EnableWelcomePage"] = "false" },
            predicate: type => !type.Name.StartsWith("PredicateVictim", StringComparison.Ordinal),
            async client =>
            {
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/e2e/predicate-victim")).StatusCode);
            });
    }

    [Fact]
    public async Task ConfigurationDisabledControllers_StillApplied()
    {
        // 防锈：改从 options 管线取值之后，配置驱动的过滤不能掉。
        await RunAsync(
            settings: new Dictionary<string, string?>
            {
                ["AspNetCore:ControllerFilter:DisabledControllers:0"] = "PredicateVictim*"
            },
            predicate: null,
            async client =>
            {
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/e2e/predicate-victim")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/predicate-survivor")).StatusCode);
            });
    }

    [Fact]
    public async Task NoFilterAtAll_KeepsEveryController()
    {
        await RunAsync(
            settings: [],
            predicate: null,
            async client =>
            {
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/predicate-victim")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/predicate-survivor")).StatusCode);
            });
    }

    private static async Task RunAsync(
        Dictionary<string, string?> settings, Func<Type, bool>? predicate, Func<HttpClient, Task> assertAsync)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();

        var allSettings = new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false"
        };
        foreach (var (key, value) in settings)
        {
            allSettings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(allSettings);

        // 注册必须走模块：builder.Services 进不了最终容器。同类测试串行，用 finally 复位。
        ControllerPredicateStartupModule.Predicate = predicate;
        try
        {
            var app = await TnziApp.CreateAsync<ControllerPredicateStartupModule>(builder);
            await app.StartAsync();
            try
            {
                await assertAsync(app.GetTestClient());
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            ControllerPredicateStartupModule.Predicate = null;
        }
    }
}

/// <summary>只依赖 AspNetCore 的最小启动模块，按文档的写法经 PostConfigure 设谓词。</summary>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class ControllerPredicateStartupModule : TnziCustomModule
{
    internal static Func<Type, bool>? Predicate;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var predicate = Predicate;
        if (predicate != null)
        {
            context.Services.PostConfigure<AspNetCoreOptions>(o => o.ControllerFilter.ControllerPredicate = predicate);
        }

        return Task.CompletedTask;
    }
}

[ApiController]
[Route("e2e/predicate-victim")]
public sealed class PredicateVictimController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get() => Ok("victim", "Success");
}

[ApiController]
[Route("e2e/predicate-survivor")]
public sealed class PredicateSurvivorController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get() => Ok("survivor", "Success");
}
