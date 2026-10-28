using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Tnzi.AspNetCore.Options;
using Tnzi.Modules;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 浏览器的 WebSocket / SSE 握手只能把 JWT 放进 <c>?access_token=</c>：JWT 处理器只在登记过的路径上读它。
/// </summary>
/// <remarks>
/// 此前路径写死为 <c>/hubs</c>：挂在别处的浏览器实时端点（如 AI 渠道网关的 <c>/ws/gateway</c>）上，
/// 带着有效令牌的浏览器客户端照样是匿名的，端点一开认证就一律 401。白名单之外的路径必须仍然只认请求头。
/// </remarks>
public class JwtQueryStringTokenTests
{
    [Theory]
    [InlineData("/hubs/chat", true)]
    [InlineData("/ws/gateway", true)]
    [InlineData("/api/users/me", false)]
    [InlineData("/ws/gatewayx", false)]
    public async Task QueryToken_IsReadOnlyOnRegisteredPaths(string path, bool accepted)
    {
        var services = await IdentityServicesAsync(o => o.PathPrefixes.Add("/ws/gateway"));
        var context = await OnMessageReceivedAsync(services, path, "?access_token=header.payload.signature");

        if (accepted)
            context.Token.ShouldBe("header.payload.signature");
        else
            context.Token.ShouldBeNull();
    }

    [Fact]
    public async Task QueryToken_WithNothingRegistered_StillWorksForHubsOnly()
    {
        var services = await IdentityServicesAsync(configure: null);

        (await OnMessageReceivedAsync(services, "/hubs/presence", "?access_token=t1")).Token.ShouldBe("t1");
        (await OnMessageReceivedAsync(services, "/ws/gateway", "?access_token=t1")).Token.ShouldBeNull();
    }

    private static async Task<ServiceProvider> IdentityServicesAsync(Action<QueryStringTokenOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("Development");
        services.AddSingleton(environment.Object);

        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());
        var module = new IdentityModule();
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        if (configure != null)
        {
            services.Configure(configure);
        }

        return services.BuildServiceProvider();
    }

    private static async Task<MessageReceivedContext> OnMessageReceivedAsync(IServiceProvider services, string path, string query)
    {
        var options = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);

        var context = new MessageReceivedContext(
            http, new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)), options);
        await options.Events.OnMessageReceived(context);
        return context;
    }
}
