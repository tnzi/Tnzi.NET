using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Net;
using Tnzi.AI.Infrastructure.Widgets;
using Tnzi.Modules;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// The inline widget runner (<c>AI:Widgets</c>), through the real pipeline: the page must carry its own
/// policy (a sandbox with no origin and no network) even where the application's security headers
/// would forbid framing, and the <c>show_widget</c> tool exists exactly when the runner does.
/// </summary>
public class WidgetRunnerEndpointTests
{
    [Fact]
    public async Task Enabled_ServesTheRunnerWithItsOwnPolicy_EvenUnderTheSecurityHeaders()
    {
        var app = await CreateAppAsync(enabled: true);
        try
        {
            using var client = new HttpClient { BaseAddress = BaseAddress(app) };
            using var response = await client.GetAsync(WidgetOptions.DefaultRunnerPath);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");

            var policy = response.Headers.GetValues("Content-Security-Policy").Single();
            policy.ShouldBe(WidgetRunnerEndpoint.BuildContentSecurityPolicy("*"));
            policy.ShouldStartWith("sandbox allow-scripts;");
            policy.ShouldContain("connect-src 'none'");
            policy.ShouldNotContain("allow-same-origin");

            // The security-headers middleware sets DENY on every response; framing is this page's purpose.
            response.Headers.Contains("X-Frame-Options").ShouldBeFalse();

            var html = await response.Content.ReadAsStringAsync();
            html.ShouldContain("ui/notifications/sandbox-proxy-ready");
            html.ShouldContain("sandbox', 'allow-scripts'");

            app.Services.GetRequiredService<IToolRegistry>().GetAllTools()
                .ShouldContain(t => t.Name == WidgetToolDefinition.Name && t.GroupName == WidgetToolDefinition.GroupName);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disabled_NeitherTheRunnerNorTheTool()
    {
        var app = await CreateAppAsync(enabled: false);
        try
        {
            using var client = new HttpClient { BaseAddress = BaseAddress(app) };
            using var response = await client.GetAsync(WidgetOptions.DefaultRunnerPath);

            response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
            app.Services.GetRequiredService<IToolRegistry>().GetAllTools()
                .ShouldNotContain(t => t.Name == WidgetToolDefinition.Name);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void Policy_NarrowsWhoMayFrameIt()
    {
        WidgetRunnerEndpoint.BuildContentSecurityPolicy("https://chat.example.com")
            .ShouldEndWith("frame-ancestors https://chat.example.com");
    }

    [Theory]
    [InlineData("ai/widget-runner", "*")]
    [InlineData("//evil.example/runner", "*")]
    [InlineData("/ai/widget-runner", "*; script-src *")]
    [InlineData("/ai/widget-runner", "")]
    public void Validator_RejectsBadSettings_WhenEnabled(string path, string ancestors)
    {
        var options = new WidgetOptions { Enabled = true, RunnerPath = path, FrameAncestors = ancestors };
        new WidgetOptionsValidator().Validate(null, options).Failed.ShouldBeTrue();

        options.Enabled = false;
        new WidgetOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
    }

    private static Uri BaseAddress(WebApplication app)
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault();
        address.ShouldNotBeNull();
        return new Uri(address!);
    }

    private static async Task<WebApplication> CreateAppAsync(bool enabled)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = false;
            options.ValidateScopes = false;
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AspNetCore:SecurityHeaders:EnableSecurityHeaders"] = "true",
            ["AI:DefaultProvider"] = "Test",
            ["AI:Providers:Test:Enabled"] = "true",
            ["AI:Providers:Test:ApiKey"] = "test-key",
            ["AI:Providers:Test:DefaultModel"] = "test-model",
            ["AI:Providers:DeepSeek:Enabled"] = "false",
            ["AI:Widgets:Enabled"] = enabled ? "true" : "false"
        });

        var app = await TnziApp.CreateAsync<WidgetRunnerStartupModule>(builder);
        await app.StartAsync();
        return app;
    }
}

[DependsOn(typeof(AIModule))]
internal sealed class WidgetRunnerStartupModule : TnziApplicationModule
{
}
