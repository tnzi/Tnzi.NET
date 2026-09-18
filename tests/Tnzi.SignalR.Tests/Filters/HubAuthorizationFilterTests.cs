using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Security.Authorization;
using Tnzi.SignalR.Authorization;
using Tnzi.SignalR.Tests.TestDoubles;

namespace Tnzi.SignalR.Tests.Filters;

/// <summary>
/// <see cref="HubAuthorizationFilter"/> 的授权覆盖面。
///
/// ★ 关键在于 <see cref="HubAuthorizeAttribute"/> 允许标在**类**上。类级授权的意思是
/// "这个 Hub 只给授权用户用"，而 Hub 最主要的能力是**接收广播** —— 那不需要调用任何
/// 方法。只在 <c>InvokeMethodAsync</c> 上把关，等于未认证客户端照样能建立连接、加入组、
/// 收到服务端推送，只有主动调方法时才被拒。
/// </summary>
public class HubAuthorizationFilterTests
{
    [HubAuthorize]
    private sealed class AuthenticatedOnlyHub : Hub
    {
        public Task Ping() => Task.CompletedTask;
    }

    [HubAuthorize(Roles = "admin")]
    private sealed class AdminRoleHub : Hub
    {
        public Task Ping() => Task.CompletedTask;
    }

    [HubAuthorize("system.signalr.view")]
    private sealed class PermissionHub : Hub
    {
        public Task Ping() => Task.CompletedTask;
    }

    private sealed class OpenHub : Hub
    {
        public Task Ping() => Task.CompletedTask;
    }

    /// <summary>类级特性经继承传下来（<c>Inherited = true</c>）。</summary>
    private sealed class DerivedFromAuthenticatedOnly : AuthenticatedOnlyBase
    {
    }

    [HubAuthorize]
    private class AuthenticatedOnlyBase : Hub
    {
        public Task Ping() => Task.CompletedTask;
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal Authenticated(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    private static HubAuthorizationFilter CreateFilter(IPermissionChecker? checker = null) =>
        new(Mock.Of<ILogger<HubAuthorizationFilter>>(), checker);

    private static HubLifetimeContext Lifetime(Hub hub, ClaimsPrincipal? user) =>
        new(new FakeHubCallerContext(user: user), new ServiceCollection().BuildServiceProvider(), hub);

    private static HubInvocationContext Invocation(Hub hub, ClaimsPrincipal? user) =>
        new(
            new FakeHubCallerContext(user: user),
            new ServiceCollection().BuildServiceProvider(),
            hub,
            hub.GetType().GetMethod("Ping")!,
            []);

    // ---------- 连接期（类级特性） ----------

    [Fact]
    public async Task OnConnected_RejectsAnonymousClient_WhenTheHubClassIsAuthorized()
    {
        var filter = CreateFilter();
        using var hub = new AuthenticatedOnlyHub();
        var nextCalled = false;

        await Should.ThrowAsync<HubException>(() =>
            filter.OnConnectedAsync(Lifetime(hub, Anonymous()), _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }));

        nextCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task OnConnected_RejectsWhenThereIsNoPrincipalAtAll()
    {
        var filter = CreateFilter();
        using var hub = new AuthenticatedOnlyHub();

        await Should.ThrowAsync<HubException>(() =>
            filter.OnConnectedAsync(Lifetime(hub, user: null), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task OnConnected_AllowsAnAuthenticatedClient()
    {
        var filter = CreateFilter();
        using var hub = new AuthenticatedOnlyHub();
        var nextCalled = false;

        await filter.OnConnectedAsync(Lifetime(hub, Authenticated()), _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task OnConnected_HonoursTheInheritedClassAttribute()
    {
        var filter = CreateFilter();
        using var hub = new DerivedFromAuthenticatedOnly();

        await Should.ThrowAsync<HubException>(() =>
            filter.OnConnectedAsync(Lifetime(hub, Anonymous()), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task OnConnected_EnforcesTheRoleRequirement()
    {
        var filter = CreateFilter();
        using var hub = new AdminRoleHub();

        await Should.ThrowAsync<HubException>(() =>
            filter.OnConnectedAsync(Lifetime(hub, Authenticated("user")), _ => Task.CompletedTask));

        var allowed = false;
        await filter.OnConnectedAsync(Lifetime(hub, Authenticated("admin")), _ =>
        {
            allowed = true;
            return Task.CompletedTask;
        });
        allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task OnConnected_EnforcesThePermissionRequirement()
    {
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(c => c.IsGrantedAsync("system.signalr.view")).ReturnsAsync(false);
        var filter = CreateFilter(checker.Object);
        using var hub = new PermissionHub();

        await Should.ThrowAsync<HubException>(() =>
            filter.OnConnectedAsync(Lifetime(hub, Authenticated()), _ => Task.CompletedTask));

        checker.Setup(c => c.IsGrantedAsync("system.signalr.view")).ReturnsAsync(true);
        var allowed = false;
        await filter.OnConnectedAsync(Lifetime(hub, Authenticated()), _ =>
        {
            allowed = true;
            return Task.CompletedTask;
        });
        allowed.ShouldBeTrue();
    }

    /// <summary>
    /// 声明了权限要求却没有权限检查器时必须拒绝 —— 这是"要求写了但检查不了"，
    /// 放行等于把一条授权要求静默变成注释。
    ///
    /// （未加载 Authorization 模块时 DI 里放的是内部的 <c>NullPermissionChecker</c>，
    /// 它对每个码都答 false，与这里的 null 走到同一个拒绝。）
    /// </summary>
    [Fact]
    public async Task OnConnected_RejectsWhenAPermissionIsRequiredButNoCheckerIsAvailable()
    {
        var filter = CreateFilter(checker: null);
        using var hub = new PermissionHub();

        await Should.ThrowAsync<HubException>(() =>
            filter.OnConnectedAsync(Lifetime(hub, Authenticated()), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task OnConnected_LetsUnattributedHubsThrough()
    {
        var filter = CreateFilter();
        using var hub = new OpenHub();
        var nextCalled = false;

        await filter.OnConnectedAsync(Lifetime(hub, Anonymous()), _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.ShouldBeTrue();
    }

    // ---------- 方法调用期（回归：原有行为不变） ----------

    [Fact]
    public async Task InvokeMethod_StillRejectsAnonymousCallersOnAnAuthorizedHub()
    {
        var filter = CreateFilter();
        using var hub = new AuthenticatedOnlyHub();

        await Should.ThrowAsync<HubException>(async () =>
            await filter.InvokeMethodAsync(Invocation(hub, Anonymous()), _ => ValueTask.FromResult<object?>(null)));
    }

    [Fact]
    public async Task InvokeMethod_LetsUnattributedHubsThrough()
    {
        var filter = CreateFilter();
        using var hub = new OpenHub();

        var result = await filter.InvokeMethodAsync(
            Invocation(hub, Anonymous()),
            _ => ValueTask.FromResult<object?>("ok"));

        result.ShouldBe("ok");
    }
}
