using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Tnzi.Hosting;
using Tnzi.Modules;
using Tnzi.System;

namespace Tnzi.AspNetCore.Tests;

/// <summary>
/// 根路径 <c>/</c> 归属：框架欢迎页 vs 宿主自带的 <c>wwwroot/index.html</c>。
/// </summary>
/// <remarks>
/// 欢迎页是一个已匹配的端点，而静态文件中间件在 <c>GetEndpoint()</c> 非空时让路，
/// 于是它恰好挡住、也只挡住根路径。三条用例分别钉住：默认仍归欢迎页（兼容）、
/// 关掉后归 web root 的 index.html、关掉后深链与 API 不受影响。
/// </remarks>
public class WelcomePageRootPathTests
{
    private const string SpaMarker = "data-test-marker=\"app-spa-shell\"";

    [Fact]
    public async Task WelcomePage_ByDefault_OwnsRootPath_AndShadowsWebRootIndex()
    {
        // 默认行为不变：既有宿主升级后 "/" 仍是欢迎页。
        // 同时这条用例证明夹具的静态文件管线是真的接上了 —— 否则下一条用例
        // 「关掉后拿到 index.html」失败时，分不清是开关没生效还是静态文件压根没配。
        await RunAsync(welcomePageEnabled: null, async client =>
        {
            var root = await client.GetAsync("/");
            root.EnsureSuccessStatusCode();
            var rootBody = await root.Content.ReadAsStringAsync();
            Assert.Contains("Tnzi.NET", rootBody);
            Assert.DoesNotContain(SpaMarker, rootBody);

            // 只有 "/" 被挡住：带文件名的请求照常由静态文件服务。
            var indexHtml = await client.GetAsync("/index.html");
            indexHtml.EnsureSuccessStatusCode();
            Assert.Contains(SpaMarker, await indexHtml.Content.ReadAsStringAsync());
        });
    }

    [Fact]
    public async Task WelcomePage_Disabled_LetsWebRootIndexServeRootPath()
    {
        await RunAsync(welcomePageEnabled: false, async client =>
        {
            var root = await client.GetAsync("/");
            root.EnsureSuccessStatusCode();
            var rootBody = await root.Content.ReadAsStringAsync();
            Assert.Contains(SpaMarker, rootBody);
            Assert.DoesNotContain("hero-title", rootBody);
        });
    }

    [Fact]
    public async Task WelcomePage_Disabled_KeepsSpaDeepLinksAndApiWorking()
    {
        await RunAsync(welcomePageEnabled: false, async client =>
        {
            // 深链本来就匹配不到端点，走 SPA 404 处理中间件回落到 index.html。
            var deepLink = await client.GetAsync("/console/settings/profile");
            deepLink.EnsureSuccessStatusCode();
            Assert.Contains(SpaMarker, await deepLink.Content.ReadAsStringAsync());

            var api = await client.GetAsync("/api/e2e/hello");
            api.EnsureSuccessStatusCode();
            Assert.Contains("hello", await api.Content.ReadAsStringAsync());
        });
    }

    /// <summary>
    /// 起一个自带 web root 的宿主：<paramref name="welcomePageEnabled"/> 为 <c>null</c> 表示不写配置（用默认值）。
    /// </summary>
    private static async Task RunAsync(bool? welcomePageEnabled, Func<HttpClient, Task> assertAsync)
    {
        var webRoot = CreateWebRootWithIndexHtml();
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                WebRootPath = webRoot
            });

            builder.WebHost.UseTestServer();

            var settings = new Dictionary<string, string?>
            {
                ["Database:AutoDiscoverDbContexts"] = "false",
                ["AspNetCore:EnableForwardedHeaders"] = "false",
                ["AspNetCore:EnableSPANotFoundHandler"] = "true"
            };

            if (welcomePageEnabled.HasValue)
            {
                settings["AspNetCore:EnableWelcomePage"] = welcomePageEnabled.Value ? "true" : "false";
            }

            builder.Configuration.AddInMemoryCollection(settings);

            var app = await TnziApp.CreateAsync<TestSpaHostStartupModule>(builder);
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
            TryDeleteDirectory(webRoot);
        }
    }

    private static string CreateWebRootWithIndexHtml()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "tnzi-webroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(
            Path.Combine(webRoot, "index.html"),
            $"<!doctype html><html {SpaMarker}><head><title>App Shell</title></head><body></body></html>");
        return webRoot;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不该让用例变红。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// 模拟自带前端的宿主：在自己的模块里注册静态文件管线（时机在 AspNetCoreModule 之后）。
/// </summary>
[DependsOn(typeof(SystemModule))]
public sealed class TestSpaHostStartupModule : HostingModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        HostingTestSetup.RegisterCommonMocks(context.Services);

        return base.ConfigureServicesAsync(context);
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        await base.OnApplicationInitializationAsync(context);

        var app = context.App;
        if (app != null)
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }
    }
}
