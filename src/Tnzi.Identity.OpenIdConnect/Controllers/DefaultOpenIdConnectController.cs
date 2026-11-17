using Microsoft.AspNetCore;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Core;
using OpenIddict.Server.AspNetCore;
using Tnzi.AspNetCore.Routing;

namespace Tnzi.Identity.OpenIdConnect.Controllers;

/// <summary>
/// OpenID Connect 的协议端点（<c>/connect/*</c>，不带 <c>api/</c> 前缀）与会话桥端点。
/// </summary>
/// <remarks>
/// <para>
/// 发现文档（<c>/.well-known/openid-configuration</c>）、JWKS（<c>/.well-known/jwks</c>）与撤销端点由 OpenIddict 直接应答；
/// 这里接的是 OpenIddict 处理完协议校验之后、需要应用决定「是谁、给不给」的那几个端点。
/// </para>
/// <para>
/// 方法全部 <c>virtual</c>：消费方可以继承本类覆写其中一步（例如 authorize 前多问一个问题），
/// 或在同一路由上注册自己的控制器整组接管。
/// </para>
/// </remarks>
[DefaultController]
[NonRoutePrefix]
[Route(OidcConstants.RoutePrefix)]
[ApiExplorerSettings(IgnoreApi = true)]
public class DefaultOpenIdConnectController : ControllerBase
{
    /// <summary>OpenIddict 服务端的认证方案名。</summary>
    protected const string ServerScheme = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;

    /// <summary>会话桥。</summary>
    protected readonly IOidcSessionBridge SessionBridge;

    /// <summary>OpenIddict 应用（客户端）管理器。</summary>
    protected readonly OpenIddictApplicationManager<OidcClient> Applications;

    /// <summary>OpenIddict 授权管理器。</summary>
    protected readonly OpenIddictAuthorizationManager<OidcAuthorization> Authorizations;

    /// <summary>声明工厂。</summary>
    protected readonly OidcPrincipalFactory PrincipalFactory;

    /// <summary>平台用户。</summary>
    protected readonly UserManager<User> UserManager;

    /// <summary>提供方配置。</summary>
    protected readonly IOptionsMonitor<OpenIdConnectOptions> Options;

    /// <summary>创建控制器。</summary>
    public DefaultOpenIdConnectController(
        IOidcSessionBridge sessionBridge,
        OpenIddictApplicationManager<OidcClient> applications,
        OpenIddictAuthorizationManager<OidcAuthorization> authorizations,
        OidcPrincipalFactory principalFactory,
        UserManager<User> userManager,
        IOptionsMonitor<OpenIdConnectOptions> options)
    {
        SessionBridge = Check.NotNull(sessionBridge);
        Applications = Check.NotNull(applications);
        Authorizations = Check.NotNull(authorizations);
        PrincipalFactory = Check.NotNull(principalFactory);
        UserManager = Check.NotNull(userManager);
        Options = Check.NotNull(options);
    }

    /// <summary>
    /// 授权端点：确认浏览器背后是哪个平台用户、要不要征得同意，然后签发授权码。
    /// </summary>
    [HttpGet("authorize")]
    [HttpPost("authorize")]
    [IgnoreAntiforgeryToken]
    public virtual async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var client = await Applications.FindByClientIdAsync(request.ClientId!, HttpContext.RequestAborted);
        if (client is not { IsEnabled: true })
        {
            return ForbidWith(Errors.UnauthorizedClient, "The client application is disabled.");
        }

        if (string.Equals(Request.Query[OidcConstants.ConsentDecisionParameter], OidcConstants.ConsentDeniedValue, StringComparison.Ordinal))
        {
            return ForbidWith(Errors.AccessDenied, "The user denied the authorization request.");
        }

        var session = await SessionBridge.ResolveAsync(HttpContext);
        if (session == null)
        {
            return request.HasPromptValue(PromptValues.None) || string.IsNullOrWhiteSpace(Options.CurrentValue.LoginUrl)
                ? ForbidWith(Errors.LoginRequired, "The user is not signed in.")
                : Redirect(BuildInteractionUrl(Options.CurrentValue.LoginUrl!, extra: null));
        }

        var user = session.User;
        var subject = user.Id.ToString();
        var clientRecordId = client.Id.ToString();
        var scopes = request.GetScopes();

        var authorization = await FindPermanentAuthorizationAsync(subject, clientRecordId, scopes);
        var consentType = await Applications.GetConsentTypeAsync(client, HttpContext.RequestAborted);

        if (authorization == null && consentType == ConsentTypes.Explicit)
        {
            var consentUrl = Options.CurrentValue.ConsentUrl;
            if (request.HasPromptValue(PromptValues.None) || string.IsNullOrWhiteSpace(consentUrl))
            {
                return ForbidWith(Errors.ConsentRequired, "The user has not granted this application access.");
            }

            return Redirect(BuildInteractionUrl(consentUrl, new Dictionary<string, string?>
            {
                [Parameters.ClientId] = request.ClientId,
                [Parameters.Scope] = string.Join(' ', scopes),
            }));
        }

        var principal = await PrincipalFactory.CreatePrincipalAsync(user, scopes);
        var identity = (ClaimsIdentity)principal.Identity!;

        // 隐式同意（第一方应用）也记一条永久授权：「我授权过的应用」列表与撤销都靠它。
        authorization ??= await Authorizations.CreateAsync(
            identity, subject, clientRecordId, AuthorizationTypes.Permanent, scopes, HttpContext.RequestAborted);

        identity.SetAuthorizationId(await Authorizations.GetIdAsync(authorization, HttpContext.RequestAborted));
        return SignIn(principal, ServerScheme);
    }

    /// <summary>
    /// 令牌端点（授权码与刷新令牌两种授权方式）：OpenIddict 已经核过客户端凭据、PKCE 与令牌本身，
    /// 这里再确认客户端仍启用、用户仍可登录，并按库里的最新资料重出声明。
    /// </summary>
    [HttpPost("token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public virtual async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return ForbidWith(Errors.UnsupportedGrantType, "The specified grant type is not supported.");
        }

        var result = await HttpContext.AuthenticateAsync(ServerScheme);
        var principal = result.Principal;
        if (principal == null)
        {
            return ForbidWith(Errors.InvalidGrant, "The token is no longer valid.");
        }

        var client = await Applications.FindByClientIdAsync(request.ClientId!, HttpContext.RequestAborted);
        if (client is not { IsEnabled: true })
        {
            return ForbidWith(Errors.InvalidGrant, "The client application is disabled.");
        }

        var user = await UserManager.FindByIdAsync(principal.GetClaim(Claims.Subject) ?? string.Empty);
        if (user == null || !await IsUserAllowedAsync(user))
        {
            return ForbidWith(Errors.InvalidGrant, "The user is no longer allowed to sign in.");
        }

        var refreshed = await PrincipalFactory.CreatePrincipalAsync(user, principal.GetScopes());
        var identity = (ClaimsIdentity)refreshed.Identity!;
        identity.SetAuthorizationId(principal.GetAuthorizationId());
        return SignIn(refreshed, ServerScheme);
    }

    /// <summary>用户信息端点：按访问令牌里的作用域返回 <c>sub</c> / <c>name</c> / <c>email</c> 等。</summary>
    [HttpGet("userinfo")]
    [HttpPost("userinfo")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public virtual async Task<IActionResult> UserInfo()
    {
        var result = await HttpContext.AuthenticateAsync(ServerScheme);
        var subject = result.Principal?.GetClaim(Claims.Subject);
        var user = subject == null ? null : await UserManager.FindByIdAsync(subject);
        if (user == null)
        {
            return Challenge(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user associated with the access token no longer exists.",
                }),
                ServerScheme);
        }

        return Ok(await PrincipalFactory.CreateUserInfoAsync(user, result.Principal!.GetScopes()));
    }

    /// <summary>
    /// 依赖方发起登出：清掉会话桥 cookie；配置开启时同时撤销平台会话。然后回跳依赖方登记过的登出地址。
    /// </summary>
    /// <remarks>
    /// ★ 撤销平台会话还要求请求带着 <c>id_token_hint</c>、且它的主体就是会话桥背后的那个用户。
    /// 这个端点接受跨站的顶层 GET，而会话桥 cookie 是 <c>SameSite=Lax</c>（正是为了随这种导航到达）——
    /// 不加这一条，任何网站放一个指向 <c>/connect/endsession</c> 的链接就能把访客从平台登出。
    /// 只有真正签过这个用户的依赖方手里才有那枚 ID token。清 cookie 本身不需要它：最坏结果是用户下次要再建一次会话桥。
    /// </remarks>
    [HttpGet("endsession")]
    [HttpPost("endsession")]
    [IgnoreAntiforgeryToken]
    public virtual async Task<IActionResult> EndSession()
    {
        if (Options.CurrentValue.EndSessionRevokesPlatformSession)
        {
            var session = await SessionBridge.ResolveAsync(HttpContext);
            var hinted = (await HttpContext.AuthenticateAsync(ServerScheme)).Principal?.GetClaim(Claims.Subject);
            if (session != null && string.Equals(hinted, session.User.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var revocation = HttpContext.RequestServices.GetService<ISessionRevocationService>();
                if (revocation != null)
                {
                    await revocation.RevokeSessionAsync(session.SessionId, SessionRevocationReason.Logout);
                }
            }
        }

        SessionBridge.Clear(HttpContext);

        // 依赖方带了已登记的 post_logout_redirect_uri 时 OpenIddict 回跳那里；没带时回到登录页（或站点根）。
        return SignOut(new AuthenticationProperties { RedirectUri = Options.CurrentValue.LoginUrl ?? "/" }, ServerScheme);
    }

    /// <summary>
    /// 建立会话桥：已登录的平台前端带 bearer 令牌调用，换一枚 authorize 端点认得的 cookie。
    /// </summary>
    /// <remarks>
    /// 要求 bearer 认证，所以跨站请求伪造无从发起。前端与本服务不同源时要以 <c>credentials: 'include'</c> 调用，
    /// 否则浏览器丢弃响应里的 <c>Set-Cookie</c>。
    /// </remarks>
    [HttpPost("session")]
    [ApiAuthorize]
    [IgnoreAntiforgeryToken]
    public virtual async Task<IActionResult> EstablishSession()
    {
        var result = await SessionBridge.EstablishAsync(HttpContext);
        return result.Succeeded ? NoContent() : StatusCode(result.Code ?? 400, result.ToApiResult());
    }

    /// <summary>清掉会话桥 cookie（平台前端登出时调用）。</summary>
    [HttpDelete("session")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public virtual IActionResult ClearSession()
    {
        SessionBridge.Clear(HttpContext);
        return NoContent();
    }

    /// <summary>
    /// 只读的会话检查，给反向代理的 <c>forward_auth</c> 用：有效时 200 并回带用户请求头，否则 401。
    /// </summary>
    /// <remarks>
    /// 代理要拿到这枚 cookie，<c>SessionCookie:Domain</c> 须设为父域、<c>SessionCookie:Path</c> 须为 <c>/</c>。
    /// 回带的请求头：<see cref="OidcConstants.ForwardAuthUserIdHeader"/>、<see cref="OidcConstants.ForwardAuthUserNameHeader"/>，
    /// 邮箱已验证时还有 <see cref="OidcConstants.ForwardAuthUserEmailHeader"/>。
    /// </remarks>
    [HttpGet("session/check")]
    [AllowAnonymous]
    public virtual async Task<IActionResult> CheckSession()
    {
        Response.Headers.CacheControl = "no-store";

        var session = await SessionBridge.ResolveAsync(HttpContext);
        if (session == null)
        {
            return Unauthorized();
        }

        Response.Headers[OidcConstants.ForwardAuthUserIdHeader] = session.User.Id.ToString();
        if (!string.IsNullOrEmpty(session.User.UserName))
        {
            Response.Headers[OidcConstants.ForwardAuthUserNameHeader] = session.User.UserName;
        }

        if (session.User.EmailConfirmed && !string.IsNullOrEmpty(session.User.Email))
        {
            Response.Headers[OidcConstants.ForwardAuthUserEmailHeader] = session.User.Email;
        }

        return Ok();
    }

    /// <summary>
    /// 令牌端点上的账号检查：停用与锁定否决，输错密码触发的临时锁定不否决（与平台刷新令牌同口径）。
    /// </summary>
    /// <remarks>
    /// 这里不跑完整的守卫链：令牌端点的请求来自依赖方的服务器，IP 与 User-Agent 是它的，
    /// 按 IP 的守卫在这里只会把每个开了 IP 允许列表的用户误拒。按 IP 的判定已在 authorize（用户自己的浏览器）上做过。
    /// </remarks>
    protected virtual async Task<bool> IsUserAllowedAsync(User user)
    {
        var verdict = await new LockedAccountLoginGuard(UserManager)
            .EvaluateAsync(new LoginGuardContext(user, LoginMethod.RefreshToken, IpAddress: null, UserAgent: null), HttpContext.RequestAborted);
        return verdict.Allowed;
    }

    /// <summary>找一条覆盖本次作用域的有效永久授权。</summary>
    protected virtual async Task<OidcAuthorization?> FindPermanentAuthorizationAsync(string subject, string clientRecordId, ImmutableArray<string> scopes)
    {
        await foreach (var authorization in Authorizations.FindAsync(
            subject, clientRecordId, Statuses.Valid, AuthorizationTypes.Permanent, scopes, HttpContext.RequestAborted))
        {
            return authorization;
        }

        return null;
    }

    /// <summary>
    /// 构造登录页 / 同意页地址：附上本次 authorize 请求的绝对 URL 作为回跳地址（去掉同意结果参数，免得回跳后被当成拒绝）。
    /// </summary>
    protected virtual string BuildInteractionUrl(string target, IDictionary<string, string?>? extra)
    {
        var query = Request.Query
            .Where(p => !string.Equals(p.Key, OidcConstants.ConsentDecisionParameter, StringComparison.Ordinal))
            .SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string?>(p.Key, v)));
        var returnUrl = QueryHelpers.AddQueryString(UriHelper.BuildAbsolute(Request.Scheme, Request.Host, Request.PathBase, Request.Path), query);

        var parameters = new Dictionary<string, string?> { [Options.CurrentValue.ReturnUrlParameter] = returnUrl };
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
        {
            parameters[key] = value;
        }

        return QueryHelpers.AddQueryString(target, parameters);
    }

    /// <summary>以 OIDC 错误结束本次请求（OpenIddict 负责按协议把错误送回依赖方）。</summary>
    protected IActionResult ForbidWith(string error, string description)
        => Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            ServerScheme);
}
