namespace Tnzi.Identity.Services;

/// <summary>
/// <see cref="ILoginSessionCoordinator"/> 默认实现。
/// </summary>
public class LoginSessionCoordinator : ApplicationService, ILoginSessionCoordinator
{
    private readonly ISessionService? _sessionService;
    private readonly IOptionsMonitor<IdentityOptions> _identityOptionsMonitor;
    private readonly IUserAgentParserService? _userAgentParser;

    private IdentityOptions IdentityOptions => _identityOptionsMonitor.CurrentValue;

    public LoginSessionCoordinator(
        IServiceProvider serviceProvider,
        IOptionsMonitor<IdentityOptions> identityOptions,
        ISessionService? sessionService = null,
        IUserAgentParserService? userAgentParser = null)
        : base(serviceProvider)
    {
        _identityOptionsMonitor = Check.NotNull(identityOptions);
        _sessionService = sessionService;
        _userAgentParser = userAgentParser;
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
                // 单设备：撤销该用户其余全部会话。
                await _sessionService.RevokeAllSessionsAsync(userId, excludeSessionId: newSessionId);
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
                    await _sessionService.RevokeSessionAsync(others[i].Id);
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
        return result.Data
            .Where(s => !s.IsRevoked && (s.ExpiresAt == null || s.ExpiresAt > now))
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
