using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tnzi.AspNetCore;
using Tnzi.Hangfire.Options;
using Tnzi.Modules;

namespace Tnzi.Hangfire.Tests;

/// <summary>
/// 本模块的默认配置必须能启动，Dashboard 必须挂在认证之后。
///
/// 两条缺陷叠在一起，等于「没有任何一种配置能让 Dashboard 放任何人进去」：
/// ①出厂默认 <c>Dashboard.Enabled=true</c> + <c>EnableAuthorization=false</c> 恰好是验证器的硬错误，
/// 于是只写 <c>[DependsOn(typeof(HangfireModule))]</c> 不配任何节 —— 或者照文档抄样例、或者只写
/// <c>Hangfire:Enabled=false</c> —— 应用在 init 阶段抛 <c>OptionsValidationException</c> 起不来
/// （init 先解析 <c>IOptions.Value</c> 再看 <c>Enabled</c>，禁用也逃不过验证器）；
/// ②模块没有任何 <c>[DependsOn]</c>，绝对加载序 150 早于 AspNetCore 的 200，<c>UseHangfireDashboard</c>
/// 于是排在 <c>UseAuthentication()</c> 之前，角色过滤器看到的永远是匿名主体 —— 配好角色的管理员也拿 401。
/// </summary>
public class HangfireDashboardTests
{
    private static ValidateOptionsResult Validate(HangfireOptions options)
        => new HangfireOptionsValidator().Validate(name: null, options);

    // ---------------------------------------------------------------
    // 验证器
    // ---------------------------------------------------------------

    [Fact]
    public void Validator_WithDefaults_Passes()
    {
        var result = Validate(new HangfireOptions());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Validator_WhenModuleDisabled_IgnoresDashboardRules()
    {
        // 关掉的模块不该因为 Dashboard 的规则起不来：文档写的逃生口必须真的是逃生口。
        var result = Validate(new HangfireOptions
        {
            Enabled = false,
            Dashboard = new DashboardConfigOptions { Enabled = true, EnableAuthorization = false }
        });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Validator_StillRejectsAnEnabledDashboardWithoutAuthorization()
    {
        // 防锈：默认能启动，不等于放弃 02-27 那条硬规则 —— 开着 Dashboard 就必须带授权。
        var result = Validate(new HangfireOptions
        {
            Dashboard = new DashboardConfigOptions { Enabled = true, EnableAuthorization = false }
        });

        Assert.True(result.Failed);
        Assert.Contains("EnableAuthorization", result.FailureMessage);
    }

    // ---------------------------------------------------------------
    // 真实宿主
    // ---------------------------------------------------------------

    [Fact]
    public async Task Host_StartsWithEmptyHangfireConfiguration()
    {
        await using var app = await BuildAsync([], authenticated: false);

        await app.StartAsync();
        await app.StopAsync();
    }

    [Fact]
    public async Task Host_StartsWithOnlyHangfireDisabled()
    {
        await using var app = await BuildAsync(
            new Dictionary<string, string?> { ["Hangfire:Enabled"] = "false" },
            authenticated: false);

        await app.StartAsync();
        await app.StopAsync();
    }

    [Fact]
    public async Task HangfireModule_InitializesAfterAspNetCoreModule()
    {
        // 认证中间件由 AspNetCoreModule 的 init 注册；Dashboard 要排在它后面，本模块的 init 就得排在它后面。
        await using var app = await BuildAsync([], authenticated: false);

        var modules = app.Services.GetRequiredService<ITnziApplication>().Modules;
        var aspNetCoreIndex = IndexOf(modules, typeof(AspNetCoreModule));
        var hangfireIndex = IndexOf(modules, typeof(HangfireModule));

        Assert.True(hangfireIndex > aspNetCoreIndex,
            $"HangfireModule (index {hangfireIndex}) must initialize after AspNetCoreModule (index {aspNetCoreIndex}), "
            + "otherwise the dashboard is mapped ahead of UseAuthentication().");
    }

    [Fact]
    public async Task Dashboard_WithRoleAuthorization_AdmitsAuthenticatedUserWithRole()
    {
        await using var app = await BuildAsync(DashboardWithRole("Admin"), authenticated: true);
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync("/hangfire");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_WithRoleAuthorization_RejectsAnonymous()
    {
        await using var app = await BuildAsync(DashboardWithRole("Admin"), authenticated: false);
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync("/hangfire");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_WithRoleAuthorization_RejectsAuthenticatedUserWithoutTheRole()
    {
        // 防锈：不是「挂到认证后面就放所有登录用户进去」，角色仍然要对。
        await using var app = await BuildAsync(DashboardWithRole("Operator"), authenticated: true);
        await app.StartAsync();

        using var response = await app.GetTestClient().GetAsync("/hangfire");

        // Hangfire 对「已认证但无角色」答 403、对匿名答 401；两者都不是放行。
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, response.StatusCode.ToString());
    }

    private static Dictionary<string, string?> DashboardWithRole(string role) => new()
    {
        ["Hangfire:Enabled"] = "true",
        ["Hangfire:StorageType"] = "Memory",
        ["Hangfire:Dashboard:Enabled"] = "true",
        ["Hangfire:Dashboard:EnableAuthorization"] = "true",
        ["Hangfire:Dashboard:AllowedRoles:0"] = role
    };

    private static int IndexOf(IReadOnlyList<IModuleDescriptor> modules, Type moduleType)
    {
        for (var i = 0; i < modules.Count; i++)
        {
            if (modules[i].Type == moduleType)
            {
                return i;
            }
        }

        return -1;
    }

    private static async Task<WebApplication> BuildAsync(Dictionary<string, string?> hangfireSettings, bool authenticated)
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
        foreach (var (key, value) in hangfireSettings)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        // 注册必须走模块：builder.Services 进不了最终容器。同类测试串行，静态注入点在 finally 复位。
        HangfireDashboardStartupModule.Authenticated = authenticated;
        try
        {
            return await TnziApp.CreateAsync<HangfireDashboardStartupModule>(builder);
        }
        finally
        {
            HangfireDashboardStartupModule.Authenticated = null;
        }
    }
}

/// <summary>
/// 只带 AspNetCore + Hangfire 的最小启动模块，认证方案是一个把每个请求都认成
/// 「Admin 角色的用户」或「匿名」的测试 handler。
/// </summary>
[DependsOn(typeof(AspNetCoreModule), typeof(HangfireModule))]
public sealed class HangfireDashboardStartupModule : TnziCustomModule
{
    internal static bool? Authenticated;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        DashboardProbeAuthHandler.Authenticated = Authenticated ?? false;

        context.Services
            .AddAuthentication(DashboardProbeAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DashboardProbeAuthHandler>(DashboardProbeAuthHandler.SchemeName, null);

        return Task.CompletedTask;
    }
}

public sealed class DashboardProbeAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DashboardProbe";

    internal static bool Authenticated;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Authenticated)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "probe"), new Claim(ClaimTypes.Role, "Admin")],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
