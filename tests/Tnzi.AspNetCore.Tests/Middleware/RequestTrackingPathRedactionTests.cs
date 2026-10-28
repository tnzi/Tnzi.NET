using static Tnzi.AspNetCore.Tests.Middleware.RequestTrackingTestSupport;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 请求日志的 <c>Path</c> 与 <c>QueryString</c>：路径与查询串里的凭据不得原样进日志。
/// </summary>
/// <remarks>
/// <para>
/// 请求日志默认开着、Information 级、写进文件，经 <c>admin/logs</c> 给持 <c>system.log.view</c> 的人看。
/// 签署链接 <c>signing/{token}</c> 与分享链接 <c>files/share/{token}</c> 把令牌放在路径里：查询串的凭据早已按
/// <c>SensitiveQueryKeys</c> 脱敏，路径段从未处理，同一个令牌放在 <c>?token=</c> 里被掩、放在 <c>/{token}</c>
/// 里原样写进日志文件。路由参数按同一份名单判定。
/// </para>
/// <para>
/// 路由在本中间件内侧匹配，路由值要到下游管线里才有：用例按真实顺序在 next 里设上。
/// 中间件若在调用 next 之前就取路径，拿到的是还没有路由值、原样未掩的路径。
/// </para>
/// </remarks>
public class RequestTrackingPathRedactionTests
{
    private const string Token = "Zq3x-SECRET-LINK-TOKEN-0123456789abcdef";

    private static async Task<RequestLogEntry> LogOfAsync(
        string path, string query, RequestTrackingOptions options, params (string Name, string Value)[] routeValues)
    {
        var context = NewContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        var logger = new CapturingLogger();
        var middleware = NewMiddleware(
            ctx =>
            {
                foreach (var (name, value) in routeValues)
                {
                    ctx.Request.RouteValues[name] = value;
                }

                return Task.CompletedTask;
            },
            logger,
            options);

        await middleware.InvokeAsync(context);
        return Assert.Single(logger.Entries);
    }

    [Theory]
    [InlineData("/api/signing/" + Token + "/document", "/api/signing/***/document")]
    [InlineData("/api/files/share/" + Token + "/info", "/api/files/share/***/info")]
    public async Task ATokenRouteValue_IsMaskedInTheLoggedPath(string path, string expected)
    {
        var entry = await LogOfAsync(path, string.Empty, new RequestTrackingOptions(), ("token", Token), ("controller", "X"), ("action", "Y"));

        Assert.Equal(expected, entry.Path);
    }

    [Fact]
    public async Task ADeploymentsOwnKeyList_AppliesToRouteValuesToo()
    {
        var options = new RequestTrackingOptions { SensitiveQueryKeys = ["linkCode"] };

        var entry = await LogOfAsync($"/api/signing/{Token}/document", string.Empty, options, ("linkCode", Token));

        Assert.Equal("/api/signing/***/document", entry.Path);
    }

    [Fact]
    public async Task TheOAuthLinkToken_IsRedactedFromTheQueryString()
    {
        var entry = await LogOfAsync(
            "/api/auth/oauth/github/login", "?returnUrl=%2Fme&linkToken=" + Token, new RequestTrackingOptions(), ("provider", "github"));

        Assert.DoesNotContain(Token, entry.QueryString);
        Assert.Contains("linkToken=***", entry.QueryString);
        Assert.Contains("returnUrl=", entry.QueryString);
    }

    [Fact]
    public async Task APathWithoutCredentials_IsLoggedVerbatim()
    {
        var entry = await LogOfAsync("/api/users/42", string.Empty, new RequestTrackingOptions(), ("id", "42"));

        Assert.Equal("/api/users/42", entry.Path);
    }
}
