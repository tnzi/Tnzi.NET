using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace Tnzi.Identity.OpenIdConnect.Services;

/// <summary>
/// <see cref="IOidcSessionBridge"/> 的实现：Data Protection 加密、限时，每次读取都回查平台会话与登录守卫。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>读取时用 <see cref="LoginMethod.RefreshToken"/> 过守卫链</b>：authorize 是在一个已有平台会话上继续签发，
/// 与刷新同一性质 —— 停用、锁定、登录 IP 允许列表照常否决，输错密码触发的临时锁定不否决（与刷新同口径）。
/// 这里的请求来自用户自己的浏览器，IP 与 User-Agent 都是真的，所以按 IP 的守卫在这里判才有意义。
/// </para>
/// <para>
/// ★ 会话校验用 <see cref="ISessionService.ValidateAsync"/> 并带上 User-Agent / IP：与 JwtBearer 每请求校验同一口径，
/// cookie 被搬到另一台机器上时这里认不出它。与那条路径不同，这里<b>不</b>因为绑定不符而撤销平台会话 ——
/// 只拒绝这一次，撤销与否交给平台自己的请求路径决定。
/// </para>
/// </remarks>
public sealed class OidcSessionBridge : IOidcSessionBridge
{
    private readonly ITimeLimitedDataProtector _protector;
    private readonly IOptionsMonitor<OpenIdConnectOptions> _options;
    private readonly ISessionService _sessionService;
    private readonly UserManager<User> _userManager;
    private readonly ILoginGuardEvaluator? _loginGuards;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OidcSessionBridge> _logger;

    /// <summary>创建会话桥。</summary>
    public OidcSessionBridge(
        IDataProtectionProvider dataProtection,
        IOptionsMonitor<OpenIdConnectOptions> options,
        ISessionService sessionService,
        UserManager<User> userManager,
        ILogger<OidcSessionBridge> logger,
        ILoginGuardEvaluator? loginGuards = null,
        TimeProvider? timeProvider = null)
    {
        Check.NotNull(dataProtection);
        _protector = dataProtection.CreateProtector(OidcConstants.SessionBridgeProtectorPurpose).ToTimeLimitedDataProtector();
        _options = Check.NotNull(options);
        _sessionService = Check.NotNull(sessionService);
        _userManager = Check.NotNull(userManager);
        _logger = Check.NotNull(logger);
        _loginGuards = loginGuards;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<Result> EstablishAsync(HttpContext httpContext)
    {
        Check.NotNull(httpContext);

        var principal = httpContext.User;
        var userIdValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (principal.Identity?.IsAuthenticated != true || !Guid.TryParse(userIdValue, out var userId))
        {
            return Result.Failure("Authentication is required.", 401, ErrorCodes.UNAUTHORIZED);
        }

        var sessionValue = principal.FindFirst(IdentityConstants.ClaimTypeNames.SessionId)?.Value;
        if (!Guid.TryParse(sessionValue, out var sessionId) || sessionId == Guid.Empty)
        {
            // 遗留的无会话令牌：桥背后没有可回查的会话，登出与踢下线都管不到它，拒绝签发。
            return Result.Failure("The access token is not bound to a session; sign in again.", 400, ErrorCodes.IDENTITY_TOKEN_INVALID);
        }

        var session = await VerifyAsync(httpContext, userId, sessionId);
        if (session == null)
        {
            return Result.Failure("The session is no longer valid.", 401, ErrorCodes.IDENTITY_SESSION_REVOKED);
        }

        var cookie = _options.CurrentValue.SessionCookie;
        var payload = string.Create(CultureInfo.InvariantCulture, $"{userId:N}.{sessionId:N}");
        var expires = _timeProvider.GetUtcNow().Add(cookie.Lifetime);
        var protectedValue = _protector.Protect(payload, expires);

        httpContext.Response.Cookies.Append(cookie.Name, protectedValue, BuildCookieOptions(cookie, expires));
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<OidcBrowserSession?> ResolveAsync(HttpContext httpContext)
    {
        Check.NotNull(httpContext);

        var cookie = _options.CurrentValue.SessionCookie;
        if (!httpContext.Request.Cookies.TryGetValue(cookie.Name, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(value, out _);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // 过期、被篡改、或用一把已不在 key ring 里的密钥加密：都按「没有会话」处理。
            Clear(httpContext);
            return null;
        }

        var parts = payload.Split('.');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var userId) || !Guid.TryParseExact(parts[1], "N", out var sessionId))
        {
            Clear(httpContext);
            return null;
        }

        var session = await VerifyAsync(httpContext, userId, sessionId);
        if (session == null)
        {
            Clear(httpContext);
        }

        return session;
    }

    /// <inheritdoc />
    public void Clear(HttpContext httpContext)
    {
        Check.NotNull(httpContext);

        var cookie = _options.CurrentValue.SessionCookie;
        httpContext.Response.Cookies.Delete(cookie.Name, BuildCookieOptions(cookie, expires: null));
    }

    private async Task<OidcBrowserSession?> VerifyAsync(HttpContext httpContext, Guid userId, Guid sessionId)
    {
        var request = httpContext.Request;
        var userAgent = request.Headers.UserAgent.ToString();
        var ipAddress = request.GetClientIp();

        var validation = await _sessionService.ValidateAsync(sessionId, new SessionValidationContext(userAgent, ipAddress));
        if (validation != SessionValidationResult.Valid)
        {
            _logger.LogInformation("OIDC session bridge rejected: platform session {SessionId} is {Validation}", sessionId, validation);
            return null;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return null;
        }

        if (_loginGuards is { HasGuards: true })
        {
            var verdict = await _loginGuards.EvaluateAsync(new LoginGuardContext(user, LoginMethod.RefreshToken, ipAddress, userAgent));
            if (!verdict.Allowed)
            {
                _logger.LogInformation("OIDC session bridge rejected by a login guard for user {UserId}", userId);
                return null;
            }
        }

        return new OidcBrowserSession(user, sessionId);
    }

    private static CookieOptions BuildCookieOptions(OidcSessionCookieOptions cookie, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = cookie.Path,
        Domain = string.IsNullOrWhiteSpace(cookie.Domain) ? null : cookie.Domain,
        Expires = expires,
        IsEssential = true,
    };
}
