using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Tnzi.Hosting;
using Tnzi.Modules;
using Tnzi.System;

namespace Tnzi.AspNetCore.Tests;

/// <summary>
/// 欢迎页上的链接在宿主位于<b>子路径</b>下时必须仍然可达。
/// </summary>
/// <remarks>
/// 欢迎页挂在 "/" 上，浏览器地址栏因此是 "https://host/api" 这样没有尾斜杠的形式，
/// 相对地址 "swagger/" 按 RFC 3986 会丢掉最后一段、解析成 "https://host/swagger/"，
/// 落在应用之外 404。所以链接必须是带 PathBase 的绝对路径。
///
/// 三条用例按 PathBase 的<b>来源</b>分：配置的 UsePathBase、反向代理的
/// X-Forwarded-Prefix、以及根部署（无子路径）。中间那条是真正承重的 —— 子路径来自
/// IIS 子应用或反向代理时配置里是空的，只有 Request.PathBase 知道真相；一份改用
/// 配置值拼路径的实现会让第一条和第三条通过，只在这一条上红。
/// </remarks>
public class WelcomePagePathBaseTests
{
    [Fact]
    public async Task WelcomePage_UnderConfiguredPathBase_LinksCarryThePathBase()
    {
        await RunAsync(
            settings: new Dictionary<string, string?> { ["AspNetCore:PathBase"] = "/api" },
            requestPath: "/api",
            forwardedPrefix: null,
            assertAsync: async response =>
            {
                var body = await ReadWelcomePageAsync(response);
                AssertLinksArePrefixedWith("/api", body);
            });
    }

    [Fact]
    public async Task WelcomePage_BehindProxyForwardedPrefix_LinksUseTheRuntimePathBase()
    {
        // 配置里没有任何 PathBase —— 子路径只存在于运行时（这里用 X-Forwarded-Prefix
        // 模拟；IIS 子应用下由 ANCM 设置，形态相同）。用配置值拼链接的实现在这里必红。
        await RunAsync(
            settings: new Dictionary<string, string?> { ["AspNetCore:EnableForwardedHeaders"] = "true" },
            requestPath: "/",
            forwardedPrefix: "/api",
            assertAsync: async response =>
            {
                var body = await ReadWelcomePageAsync(response);
                AssertLinksArePrefixedWith("/api", body);

                // 页面自报的路由信息同样要是运行时真值：一个确实挂在 "/api" 之下的部署
                // 在页面上读到 "(none)"，会把排障的人直接引开。
                Assert.DoesNotContain("(none)", body);
            });
    }

    [Fact]
    public async Task WelcomePage_AtRoot_KeepsLinksAbsoluteFromRoot()
    {
        await RunAsync(
            settings: new Dictionary<string, string?>(),
            requestPath: "/",
            forwardedPrefix: null,
            assertAsync: async response =>
            {
                var body = await ReadWelcomePageAsync(response);
                AssertLinksArePrefixedWith(string.Empty, body);
            });
    }

    /// <summary>
    /// 三个链接都必须是以 <paramref name="pathBase"/> 打头的绝对路径，且页面上不再残留
    /// 任何相对写法。
    /// </summary>
    private static void AssertLinksArePrefixedWith(string pathBase, string body)
    {
        Assert.Contains($"href=\"{pathBase}/swagger/\"", body);
        Assert.Contains($"href=\"{pathBase}/swagger/v1/swagger.json\"", body);
        Assert.Contains($"href=\"{pathBase}/health\"", body);

        // 相对写法正是本用例要挡住的形态：它在根部署上恰好能用，一到子路径就 404。
        Assert.DoesNotContain("href=\"swagger/", body);
        Assert.DoesNotContain("href=\"health\"", body);
    }

    private static async Task<string> ReadWelcomePageAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        // 先钉住确实拿到了欢迎页，否则下面的 DoesNotContain 会在任意别的页面上假绿。
        Assert.Contains("hero-title", body);
        return body;
    }

    private static async Task RunAsync(
        IDictionary<string, string?> settings,
        string requestPath,
        string? forwardedPrefix,
        Func<HttpResponseMessage, Task> assertAsync)
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

        var app = await TnziApp.CreateAsync<WelcomePagePathBaseStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
            if (forwardedPrefix != null)
            {
                request.Headers.Add("X-Forwarded-Prefix", forwardedPrefix);
            }

            using var response = await client.SendAsync(request);
            await assertAsync(response);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

/// <summary>
/// 最小宿主：只要欢迎页所在的默认路由管线，不带 web root。
/// </summary>
[DependsOn(typeof(SystemModule))]
public sealed class WelcomePagePathBaseStartupModule : HostingModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        HostingTestSetup.RegisterCommonMocks(context.Services);

        return base.ConfigureServicesAsync(context);
    }
}
