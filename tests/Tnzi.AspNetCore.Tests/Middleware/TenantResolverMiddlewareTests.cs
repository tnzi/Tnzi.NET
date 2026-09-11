using System.Security.Claims;
using Tnzi.MultiTenancy;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 租户解析：已认证请求的租户由令牌里的 claim 决定，外部来源说了不算。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 守的是一条跨租户读写：中间件跑在 <c>UseAuthentication()</c> 之后（主体与
/// <c>tenant_id</c> 都已就绪），此前却<b>两个都不看</b> —— 只校验「这个租户存不存在、启没启用」。
/// 而默认解析顺序把 Header 排在 Claims 之前，于是 A 租户的用户加一个
/// <c>X-Tenant-Id: B</c>，后面的 EF 全局过滤器就整条请求按 B 执行：
/// 读得到 B 的数据，写进去的新行也落在 B 名下。
/// </para>
/// <para>
/// 变异验证：删掉 <c>InvokeAsync</c> 里那段 <c>IsAuthenticated</c> 分支，
/// <c>CrossTenantHeader_*</c> 两条会红。
/// </para>
/// </remarks>
public class TenantResolverMiddlewareTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class RecordingTenant : ICurrentTenant
    {
        public Guid? Id { get; private set; }
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;

        public IDisposable Change(Guid? id, string? tenantName = null)
        {
            var previous = Id;
            Id = id;
            return new Restore(() => Id = previous);
        }

        private sealed class Restore(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }

    private static HttpContext BuildContext(Guid? headerTenant, Guid? claimTenant, bool authenticated)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (headerTenant.HasValue)
        {
            context.Request.Headers["X-Tenant-Id"] = headerTenant.Value.ToString();
        }

        var claims = new List<Claim>();
        if (claimTenant.HasValue)
        {
            claims.Add(new Claim("tenant_id", claimTenant.Value.ToString()));
        }

        // authenticationType 非空 = IsAuthenticated 为真，这正是本中间件的分支判据。
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null));
        return context;
    }

    private static async Task<(Guid? Resolved, int StatusCode, bool NextCalled)> RunAsync(
        HttpContext context, TenantResolverOptions? options = null)
    {
        var tenant = new RecordingTenant();
        Guid? seen = null;
        var nextCalled = false;

        var middleware = new TenantResolverMiddleware(
            _ =>
            {
                nextCalled = true;
                seen = tenant.Id;
                return Task.CompletedTask;
            },
            Microsoft.Extensions.Options.Options.Create(options ?? new TenantResolverOptions()),
            new Mock<ILogger<TenantResolverMiddleware>>().Object);

        await middleware.InvokeAsync(context, tenant);
        return (seen, context.Response.StatusCode, nextCalled);
    }

    /// <summary>★★★ 已认证 + 异租户头 = 403，且请求不往下走。</summary>
    [Fact]
    public async Task CrossTenantHeader_OnAuthenticatedRequest_IsRejected()
    {
        var context = BuildContext(headerTenant: TenantB, claimTenant: TenantA, authenticated: true);

        var (resolved, status, nextCalled) = await RunAsync(context);

        Assert.Equal(403, status);
        Assert.False(nextCalled);
        Assert.Null(resolved);
    }

    /// <summary>对照组：头与 claim 一致时照常放行，解析结果是那个租户。</summary>
    [Fact]
    public async Task MatchingHeader_OnAuthenticatedRequest_IsAllowed()
    {
        var context = BuildContext(headerTenant: TenantA, claimTenant: TenantA, authenticated: true);

        var (resolved, status, nextCalled) = await RunAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(200, status);
        Assert.Equal(TenantA, resolved);
    }

    /// <summary>没给头时用 claim —— 这是绝大多数请求走的那条路。</summary>
    [Fact]
    public async Task NoHeader_OnAuthenticatedRequest_UsesTheClaim()
    {
        var context = BuildContext(headerTenant: null, claimTenant: TenantA, authenticated: true);

        var (resolved, _, nextCalled) = await RunAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(TenantA, resolved);
    }

    /// <summary>
    /// ★★ 不绑租户的账号带着头进来 → 忽略那个头，而不是让它自己挑一个租户进去。
    /// 这是同一个洞的另一半：没有 claim 可比对时，「比不了」不能当成「随便你」。
    /// </summary>
    [Fact]
    public async Task AuthenticatedWithoutTenantClaim_IgnoresTheHeader()
    {
        var context = BuildContext(headerTenant: TenantB, claimTenant: null, authenticated: true);

        var (resolved, status, nextCalled) = await RunAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(200, status);
        Assert.Null(resolved);
    }

    /// <summary>
    /// 匿名请求仍按外部来源解析：按租户分流的登录页在拿到令牌之前没有 claim 可读。
    /// </summary>
    [Fact]
    public async Task AnonymousRequest_StillResolvesFromHeader()
    {
        var context = BuildContext(headerTenant: TenantB, claimTenant: null, authenticated: false);

        var (resolved, _, nextCalled) = await RunAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(TenantB, resolved);
    }

    /// <summary>
    /// 出厂解析顺序把 Claims 排在最前 —— 一个只看默认值就上线的部署也该拿到安全行为。
    /// </summary>
    [Fact]
    public void DefaultResolutionOrder_PutsClaimsFirst()
        => Assert.Equal(TenantResolutionSource.Claims, new TenantResolverOptions().ResolutionOrder[0]);
}
