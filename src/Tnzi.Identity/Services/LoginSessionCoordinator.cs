namespace Tnzi.Identity.Services;

/// <summary>
/// <see cref="ILoginSessionCoordinator"/> 默认实现。
/// </summary>
public class LoginSessionCoordinator : ApplicationService, ILoginSessionCoordinator
{
    private readonly ISessionService? _sessionService;
    private readonly IOptionsMonitor<IdentityOptions> _identityOptionsMonitor;
    private readonly IUserAgentParserService? _userAgentParser;
    private readonly ISessionRevocationService? _sessionRevocation;

    private IdentityOptions IdentityOptions => _identityOptionsMonitor.CurrentValue;

    public LoginSessionCoordinator(
        IServiceProvider serviceProvider,
        IOptionsMonitor<IdentityOptions> identityOptions,
        ISessionService? sessionService = null,
        IUserAgentParserService? userAgentParser = null,
        ISessionRevocationService? sessionRevocation = null)
        : base(serviceProvider)
    {
        _identityOptionsMonitor = Check.NotNull(identityOptions);
        _sessionService = sessionService;
        _userAgentParser = userAgentParser;
        _sessionRevocation = sessionRevocation;
    }

    /// <summary>
    /// 踢掉一条会话（多登录策略的 Replace 分支）。
    /// </summary>
    /// <remarks>
    /// ★★ <b>必须走撤销出口，不能只调 <c>ISessionService.RevokeSessionAsync</c>。</b>
    /// 后者只把会话行标成已撤销，绑定其上的<b>刷新令牌原样留在库里</b>；
    /// 「被踢的设备进不来」于是完全押在 <c>EnforceSessionValidation</c> 这个逃生开关上 ——
    /// 一旦有人把它关掉，被踢的设备可以拿旧刷新令牌换一枚全新的 access token，
    /// <b>单设备登录与限并发一起变成装饰</b>，而管理端的会话列表看上去一切正常。
    /// 这是本模块所有撤销点里最常被触发的一条，反而最容易被漏掉。
    /// </remarks>
    private async Task RevokeSessionAsync(Guid sessionId)
    {
        if (_sessionRevocation != null)
        {
            await _sessionRevocation.RevokeSessionAsync(sessionId, SessionRevocationReason.MultiLoginReplaced);
            return;
        }

        LogWarning(
            "ISessionRevocationService is not available; session {SessionId} is revoked but its refresh token is left behind.",
            sessionId);
        await _sessionService!.RevokeSessionAsync(sessionId);
    }

    /// <summary>
    /// 踢掉该用户的其余全部会话（单设备策略）。理由同 <see cref="RevokeSessionAsync"/>。
    /// </summary>
    private async Task RevokeOtherSessionsAsync(Guid userId, Guid keepSessionId)
    {
        if (_sessionRevocation != null)
        {
            await _sessionRevocation.RevokeUserSessionsAsync(
                userId, SessionRevocationReason.MultiLoginReplaced, excludeSessionId: keepSessionId);
            return;
        }

        LogWarning(
            "ISessionRevocationService is not available; other sessions of user {UserId} are revoked but their refresh tokens are left behind.",
            userId);
        await _sessionService!.RevokeAllSessionsAsync(userId, excludeSessionId: keepSessionId);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> EstablishAsync(Guid userId)
    {
        // 无会话服务：不做会话绑定，令牌退回无 session_id（不受强制校验）。
        if (_sessionService == null)
        {
            return Ok(Guid.Empty);
        }

        var options = IdentityOptions;
        var multi = options.MultiLogin;
        var jwt = options.Jwt;

        // 当前有效（未撤销、未过期）会话集合 —— 供策略判定与并发计数。
        var existing = await GetValidSessionsAsync(userId);

        // 1) Reject 策略：在建立会话之前判定，达到上限直接拒绝本次登录。
        if (multi.OnConflict == LoginConflictPolicy.Reject)
        {
            if (!multi.AllowMultiLogin && existing.Count > 0)
            {
                return Fail<Guid>("Already logged in on another device", 403, ErrorCodes.IDENTITY_SESSION_ALREADY_ACTIVE);
            }

            if (multi.AllowMultiLogin && multi.MaxConcurrentSessions > 0 && existing.Count >= multi.MaxConcurrentSessions)
            {
                return Fail<Guid>("Maximum concurrent sessions reached", 403, ErrorCodes.IDENTITY_SESSION_LIMIT_REACHED);
            }
        }

        // 2) 建立新会话（同步、在令牌签发之前）。会话生命周期绑定刷新令牌：
        //    启用刷新令牌 → 会话活到刷新令牌到期；否则 → 活到 access token 到期。
        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;
        var deviceInfo = BuildDeviceInfo(userAgent);
        var lifetime = jwt.EnableRefreshToken
            ? TimeSpan.FromDays(jwt.RefreshTokenExpirationDays)
            : TimeSpan.FromMinutes(jwt.AccessTokenExpirationMinutes);
        var expiresAt = DateTime.UtcNow.Add(lifetime);

        var newSessionId = await _sessionService.CreateSessionAsync(userId, deviceInfo, ipAddress, userAgent, expiresAt);

        // 3) Replace 策略：**先建后撤**（排除本会话），使并发登录竞态下也收敛到正确状态
        //    —— 两个同时登录各自撤销对方，最终最后建立者胜出、其余被踢，而非旧实现的"都幸存"。
        if (multi.OnConflict == LoginConflictPolicy.Replace)
        {
            if (!multi.AllowMultiLogin)
            {
                // 单设备：撤销该用户其余全部会话（连同它们的刷新令牌）。
                await RevokeOtherSessionsAsync(userId, newSessionId);
            }
            else if (multi.MaxConcurrentSessions > 0)
            {
                // 限并发：连同新会话若超上限，按最后活动时间撤销最旧的若干个（排除新会话）。
                var others = existing
                    .Where(s => s.Id != newSessionId)
                    .OrderBy(s => s.LastActivityTime)
                    .ToList();
                var surplus = others.Count + 1 - multi.MaxConcurrentSessions;
                for (var i = 0; i < surplus && i < others.Count; i++)
                {
                    await RevokeSessionAsync(others[i].Id);
                }
            }
        }

        return Ok(newSessionId);
    }

    /// <summary>
    /// 取当前有效会话（未撤销、未过期）。<c>ExpiresAt == null</c> 视为不过期（遗留会话）。
    /// </summary>
    private async Task<List<UserSessionDto>> GetValidSessionsAsync(Guid userId)
    {
        var result = await _sessionService!.GetUserSessionsAsync(userId);
        if (!result.Succeeded || result.Data == null)
        {
            return new List<UserSessionDto>();
        }

        var now = DateTime.UtcNow;
        // 闲置超时也要算进来：一条超过闲置窗口的会话在每请求校验里已经判死，
        // 却还占着并发名额 —— 症状是「明明只登了一台设备，却说已达上限」。
        var idleMinutes = IdentityOptions.AccountSecurity.SessionTimeoutMinutes;

        return result.Data
            .Where(s => !s.IsRevoked
                && (s.ExpiresAt == null || s.ExpiresAt > now)
                && (s.AbsoluteExpiresAt == null || s.AbsoluteExpiresAt > now)
                && (idleMinutes <= 0 || s.LastActivityTime.AddMinutes(idleMinutes) > now))
            .ToList();
    }

    /// <summary>
    /// 从 UserAgent 提取简短设备描述（如 "Chrome on Windows"），供会话统计 Top device 聚合；
    /// 解析器缺失或 UA 不可识别时返回 null。
    /// </summary>
    private string? BuildDeviceInfo(string? userAgent)
    {
        if (_userAgentParser == null || string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var info = _userAgentParser.Parse(userAgent);
        if (!string.IsNullOrEmpty(info.Browser) && !string.IsNullOrEmpty(info.OperatingSystem))
        {
            return $"{info.Browser} on {info.OperatingSystem}";
        }

        return info.Browser ?? info.OperatingSystem ?? info.DeviceType;
    }
}
