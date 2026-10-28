using Microsoft.AspNetCore.Mvc;
using Tnzi.Identity.Controllers;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// Cookie 交付模式下的登出：刷新 cookie 属于同一浏览器里后来登录的另一位用户时必须保留。
/// </summary>
/// <remarks>
/// cookie 是整个浏览器共享的，访问令牌却留在各自的标签页里。旧标签页（用户 A，访问令牌仍有效）登出时，
/// 无条件清掉刷新 cookie 等于替 A 把用户 B 也登出：B 下一次刷新失败，而两个客户端都无从避免。
/// </remarks>
public class DefaultAuthControllerLogoutCookieTests
{
    private const string CookieName = "tnzi_rt";

    private readonly Guid _caller = Guid.NewGuid();
    private readonly Mock<IAuthService> _authService = new();

    public DefaultAuthControllerLogoutCookieTests()
    {
        _authService.Setup(s => s.LogoutAsync(_caller)).ReturnsAsync(Result<string>.Success("Logout successfully"));
    }

    private DefaultHttpContext BuildContext(string? presentedCookie)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.Id).Returns(_caller);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(currentUser.Object);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (presentedCookie != null)
        {
            httpContext.Request.Headers.Cookie = $"{CookieName}={presentedCookie}";
        }

        return httpContext;
    }

    private DefaultAuthController Build(HttpContext httpContext, TokenDeliveryMode mode = TokenDeliveryMode.Cookie)
    {
        var options = new Mock<IOptionsMonitor<IdentityOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new IdentityOptions
        {
            TokenDelivery = new TokenDeliveryOptions { Mode = mode, CookieName = CookieName },
        });

        return new DefaultAuthController(
            new Mock<ITwoFactorService>().Object,
            _authService.Object,
            new Mock<IRegistrationService>().Object,
            new Mock<IPasswordService>().Object,
            identityOptions: options.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static bool ClearedTheCookie(HttpContext httpContext)
        => httpContext.Response.Headers.SetCookie.Any(v => v != null && v.StartsWith(CookieName + "=", StringComparison.Ordinal));

    [Fact]
    public async Task CookieOfAnotherUser_IsKept_WhileTheCallersSessionIsStillRevoked()
    {
        _authService.Setup(s => s.IsRefreshTokenHeldByAnotherUserAsync("rt-of-b", _caller)).ReturnsAsync(true);
        var httpContext = BuildContext("rt-of-b");

        var result = await Build(httpContext).Logout();

        result.Succeeded.ShouldBeTrue();
        _authService.Verify(s => s.LogoutAsync(_caller), Times.Once);
        ClearedTheCookie(httpContext).ShouldBeFalse("the refresh cookie belongs to the user who signed in later in this browser");
    }

    [Fact]
    public async Task CookieOfTheCaller_OrOneThatNoLongerResolves_IsCleared()
    {
        _authService.Setup(s => s.IsRefreshTokenHeldByAnotherUserAsync("rt-of-a", _caller)).ReturnsAsync(false);
        var httpContext = BuildContext("rt-of-a");

        await Build(httpContext).Logout();

        ClearedTheCookie(httpContext).ShouldBeTrue();
    }

    [Fact]
    public async Task NoCookieOnTheRequest_StillSendsTheDeletion()
    {
        var httpContext = BuildContext(presentedCookie: null);

        await Build(httpContext).Logout();

        ClearedTheCookie(httpContext).ShouldBeTrue();
        _authService.Verify(s => s.IsRefreshTokenHeldByAnotherUserAsync(It.IsAny<string?>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task BearerMode_NeverTouchesCookies()
    {
        var httpContext = BuildContext("rt-of-a");

        await Build(httpContext, TokenDeliveryMode.Bearer).Logout();

        ClearedTheCookie(httpContext).ShouldBeFalse();
        _authService.Verify(s => s.IsRefreshTokenHeldByAnotherUserAsync(It.IsAny<string?>(), It.IsAny<Guid>()), Times.Never);
    }
}
