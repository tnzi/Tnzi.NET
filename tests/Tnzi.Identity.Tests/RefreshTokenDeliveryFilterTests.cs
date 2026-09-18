using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using System.Reflection;
using Tnzi.AspNetCore.Extensions;
using Tnzi.AspNetCore.Models;
using Tnzi.Identity.Mvc;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// Cookie 交付：响应体里的刷新令牌必须被搬进 <c>HttpOnly</c> cookie。
/// </summary>
/// <remarks>
/// ★ <b>这一组存在的理由是「逐个端点记得调一次」已经漏过一次</b>：OAuth 回调不返回 JSON，
/// 于是它天然绕开了控制器里那个共享出口。把判据改成<b>响应载荷的形状</b>之后，
/// 谁产生这个响应就不再重要 —— 包括消费方覆写 <c>[DefaultController]</c> 之后自己写的端点。
/// </remarks>
public class RefreshTokenDeliveryFilterTests
{
    private static ResultExecutingContext CreateContext(HttpContext httpContext, object value)
    {
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(
            actionContext, [], new ObjectResult(value), controller: new object());
    }

    private static RefreshTokenDeliveryFilter CreateFilter(TokenDeliveryMode mode, string cookieName = "tnzi_rt")
    {
        var options = new IdentityOptions
        {
            TokenDelivery = new TokenDeliveryOptions { Mode = mode, CookieName = cookieName },
        };

        var monitor = new Mock<IOptionsMonitor<IdentityOptions>>();
        monitor.Setup(x => x.CurrentValue).Returns(options);
        return new RefreshTokenDeliveryFilter(monitor.Object);
    }

    private static string? ReadSetCookie(HttpContext httpContext, string name)
        => httpContext.Response.Headers.SetCookie
            .FirstOrDefault(v => v != null && v.StartsWith(name + "=", StringComparison.Ordinal));

    [Fact]
    public void CookieMode_MovesRefreshTokenOutOfTheBody()
    {
        var httpContext = new DefaultHttpContext();
        var payload = new TokenResult
        {
            AccessToken = "access",
            RefreshToken = "the-refresh-token",
            ExpiresIn = 3600,
            RefreshTokenExpiresIn = 604800,
        };
        var context = CreateContext(httpContext, ApiResult<TokenResult>.Success(payload));

        CreateFilter(TokenDeliveryMode.Cookie).OnResultExecuting(context);

        Assert.Equal(string.Empty, payload.RefreshToken);
        var setCookie = ReadSetCookie(httpContext, "tnzi_rt");
        Assert.NotNull(setCookie);
        Assert.Contains("the-refresh-token", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>验证码登录有自己的结果 DTO —— 同样受管辖，不需要为它补一行名单。</summary>
    [Fact]
    public void CookieMode_CoversCodeLoginPayloadToo()
    {
        var httpContext = new DefaultHttpContext();
        var payload = new CodeLoginResultDto
        {
            AccessToken = "access",
            RefreshToken = "code-login-refresh",
            ExpiresIn = 3600,
            RefreshTokenExpiresIn = 604800,
        };
        var context = CreateContext(httpContext, ApiResult<CodeLoginResultDto>.Success(payload));

        CreateFilter(TokenDeliveryMode.Cookie).OnResultExecuting(context);

        Assert.Null(payload.RefreshToken);
        Assert.NotNull(ReadSetCookie(httpContext, "tnzi_rt"));
    }

    /// <summary>★ 对照组：默认的 Bearer 模式下一个字节都不动。</summary>
    [Fact]
    public void BearerMode_LeavesTheResponseUntouched()
    {
        var httpContext = new DefaultHttpContext();
        var payload = new TokenResult { AccessToken = "access", RefreshToken = "stays-in-body" };
        var context = CreateContext(httpContext, ApiResult<TokenResult>.Success(payload));

        CreateFilter(TokenDeliveryMode.Bearer).OnResultExecuting(context);

        Assert.Equal("stays-in-body", payload.RefreshToken);
        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
    }

    /// <summary>没有刷新令牌的响应（部署关掉了它）不该凭空写出一枚空 cookie。</summary>
    [Fact]
    public void CookieMode_WithoutARefreshToken_WritesNothing()
    {
        var httpContext = new DefaultHttpContext();
        var payload = new TokenResult { AccessToken = "access", RefreshToken = string.Empty };
        var context = CreateContext(httpContext, ApiResult<TokenResult>.Success(payload));

        CreateFilter(TokenDeliveryMode.Cookie).OnResultExecuting(context);

        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
    }

    /// <summary>与令牌无关的响应原样通过。</summary>
    [Fact]
    public void CookieMode_IgnoresUnrelatedPayloads()
    {
        var httpContext = new DefaultHttpContext();
        var context = CreateContext(httpContext, ApiResult<string>.Success("hello"));

        CreateFilter(TokenDeliveryMode.Cookie).OnResultExecuting(context);

        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
    }

    /// <summary>
    /// ★ 控制器实际返回的是 <c>result.ToApiResult()</c> 的产物 —— 必须用<b>那个</b>形状再验一遍。
    /// </summary>
    /// <remarks>
    /// 这条是被一次真实的假绿逼出来的：其余用例构造的是 <c>ApiResult&lt;T&gt;.Success(...)</c>，
    /// 而那个静态方法返回的其实是继承来的 <c>Result&lt;T&gt;</c>，与生产路径上的信封类型不是一个。
    /// 「测试用的信封」与「线上用的信封」不同，是一类特别容易通过的假绿。
    /// </remarks>
    [Fact]
    public void CookieMode_CoversTheEnvelopeControllersActuallyReturn()
    {
        var httpContext = new DefaultHttpContext();
        var payload = new TokenResult
        {
            AccessToken = "access",
            RefreshToken = "production-shape",
            RefreshTokenExpiresIn = 604800,
        };
        var apiResult = Result<TokenResult>.Success(payload).ToApiResult();
        Assert.IsAssignableFrom<IApiResult>(apiResult);

        var context = CreateContext(httpContext, apiResult);

        CreateFilter(TokenDeliveryMode.Cookie).OnResultExecuting(context);

        Assert.Equal(string.Empty, payload.RefreshToken);
        Assert.NotNull(ReadSetCookie(httpContext, "tnzi_rt"));
    }

    /// <summary>cookie 名可配置 —— 同域跑多个本框架应用时必须能各取一个。</summary>
    [Fact]
    public void CookieMode_HonoursTheConfiguredCookieName()
    {
        var httpContext = new DefaultHttpContext();
        var payload = new TokenResult { AccessToken = "a", RefreshToken = "r" };
        var context = CreateContext(httpContext, ApiResult<TokenResult>.Success(payload));

        CreateFilter(TokenDeliveryMode.Cookie, cookieName: "app_rt").OnResultExecuting(context);

        Assert.NotNull(ReadSetCookie(httpContext, "app_rt"));
    }

    /// <summary>
    /// ★ 待办办完之后签发的会话把 <c>TokenResult</c> 嵌在 <c>Token</c> 里：过滤器看的是最外层载荷的形状，
    /// 嵌套一层它就看不见 —— 刷新令牌照样落进 JSON，cookie 一枚没写，access token 到期就掉线。
    /// </summary>
    [Fact]
    public void CookieMode_CoversPendingActionPayload()
    {
        var httpContext = new DefaultHttpContext();
        var token = new TokenResult { AccessToken = "access", RefreshToken = "pending-refresh", RefreshTokenExpiresIn = 604800 };
        var payload = new PendingActionResultDto { Completed = true, Token = token };
        var context = CreateContext(httpContext, Result<PendingActionResultDto>.Success(payload).ToApiResult());

        CreateFilter(TokenDeliveryMode.Cookie).OnResultExecuting(context);

        Assert.Equal(string.Empty, token.RefreshToken);
        var setCookie = ReadSetCookie(httpContext, "tnzi_rt");
        Assert.NotNull(setCookie);
        Assert.Contains("pending-refresh", setCookie);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>接受邀请后 <c>SignInAfterAccept</c> 签发的会话，同一形态。</summary>
    [Fact]
    public void CookieMode_CoversAcceptInvitationPayload()
    {
        var httpContext = new DefaultHttpContext();
        var token = new TokenResult { AccessToken = "access", RefreshToken = "invitation-refresh", RefreshTokenExpiresIn = 604800 };
        var payload = new AcceptInvitationResultDto { Completed = true, Token = token };
        var context = CreateContext(httpContext, Result<AcceptInvitationResultDto>.Success(payload).ToApiResult());

        CreateFilter(TokenDeliveryMode.Cookie).OnResultExecuting(context);

        Assert.Equal(string.Empty, token.RefreshToken);
        Assert.NotNull(ReadSetCookie(httpContext, "tnzi_rt"));
    }

    /// <summary>挑战（未办完、没有令牌）的载荷不该凭空写出一枚 cookie。</summary>
    [Fact]
    public void CookieMode_PendingActionWithoutToken_WritesNothing()
    {
        var httpContext = new DefaultHttpContext();
        var payload = new PendingActionResultDto { Completed = false, RemainingActions = ["ChangePassword"] };
        var context = CreateContext(httpContext, Result<PendingActionResultDto>.Success(payload).ToApiResult());

        CreateFilter(TokenDeliveryMode.Cookie).OnResultExecuting(context);

        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
    }

    /// <summary>
    /// ★ 关掉这一类缺陷：任何嵌着 <c>TokenResult</c>（或别的携带者）的响应 DTO 都必须自己实现
    /// <see cref="IRefreshTokenCarrier"/>，否则过滤器在最外层就停下，嵌套的刷新令牌原样进 JSON。
    /// 判据按形状扫整个程序集，新增一个嵌套签发的 DTO 时这里就会红。
    /// </summary>
    [Fact]
    public void EveryDtoNestingARefreshTokenCarrier_IsItselfACarrier()
    {
        var carrierType = typeof(IRefreshTokenCarrier);
        var offenders = typeof(TokenResult).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => !carrierType.IsAssignableFrom(t))
            .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => carrierType.IsAssignableFrom(p.PropertyType)))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(offenders);
    }
}
