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

    /// <inheritdoc />
    public async Task<Result<Guid>> EstablishDelegatedAsync(Guid userId, DelegatedSessionRequest request)
    {
        Check.NotNull(request);

        if (_sessionService == null)
        {
            // 没有会话服务就没有会话记录：声明无处可存、分组无从撤销，签出来的令牌也不受每请求校验约束。
            return Fail<Guid>("Delegated sessions require a session service.", 501, ErrorCodes.CONFIGURATION_ERROR);
        }

        // ★ 刻意不碰多登录策略（见接口注释）：既不数名额、也不挤掉任何会话。
        var jwt = IdentityOptions.Jwt;
        var absoluteExpiresAt = DelegatedSessionRules.ToUtc(request.ExpiresAt);
        var lifetime = jwt.EnableRefreshToken
            ? TimeSpan.FromDays(jwt.RefreshTokenExpirationDays)
            : TimeSpan.FromMinutes(jwt.AccessTokenExpirationMinutes);
        var expiresAt = SessionLifetime.ClampToAbsolute(DateTime.UtcNow.Add(lifetime), absoluteExpiresAt);

        var userAgent = ScopedContext?.UserAgent;
        var sessionId = await _sessionService.CreateDelegatedSessionAsync(new DelegatedSessionCreation(
            userId,
            BuildDeviceInfo(userAgent),
            ScopedContext?.ClientIpAddress,
            userAgent,
            expiresAt,
            new DelegatedSessionInfo(request.GroupKey, request.Label, request.Claims, absoluteExpiresAt)));

        if (sessionId == Guid.Empty)
        {
            return Fail<Guid>(
                "The configured session service does not support delegated sessions.", 501, ErrorCodes.CONFIGURATION_ERROR);
        }

        return Ok(sessionId);
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

        // 1) Reject 策略：在建立会话之前判定，达到上限直接拒绝本次登录（不建会话；并发下的复核见第 3 步）。
        if (multi.OnConflict == LoginConflictPolicy.Reject && LimitsSessions(multi))
        {
            var existing = await GetValidSessionsAsync(userId);
            var rejection = RejectionFor(multi, existing.Count);
            if (rejection != null)
            {
                return rejection;
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

        // 3) Reject 策略的**建后复核**。上面那次判定读的是建会话之前的快照，读与建之间没有任何串行化：
        //    同一账号两台设备同时登录，两边都读到 0 条、各建一条，单设备策略收场时有两条活会话（限并发同理）。
        //    建完再数一次「除我之外」的会话，超了就撤掉刚建的这条并按同一句话拒绝。两边交错时至多一边留下：
        //    后数的那一边必然看得见先建的那一条；最坏是两边都撤，用户重试一次即可，不会两边都活着。
        //    ⚠ 前提是新会话一建就提交（框架默认）。开着全局工作单元（AspNetCore:EnableGlobalUnitOfWork）时，
        //    会话行要到请求事务提交才对别的连接可见，两边复核时都还看不见对方，这道复核在并发下不起作用。
        if (multi.OnConflict == LoginConflictPolicy.Reject && LimitsSessions(multi))
        {
            var others = (await GetValidSessionsAsync(userId)).Count(s => s.Id != newSessionId);
            var rejection = RejectionFor(multi, others);
            if (rejection != null)
            {
                // 这条会话还没有签出任何令牌（没有刷新令牌可删），直接撤销会话行即可。
                await _sessionService.RevokeSessionAsync(newSessionId);
                LogInformation(
                    "Session {SessionId} of user {UserId} was withdrawn: a concurrent sign-in took the slot the multi-login policy allows.",
                    newSessionId, userId);
                return rejection;
            }
        }

        // 4) Replace 策略：**先建后撤**（排除本会话），使并发登录竞态下也收敛到正确状态
        //    —— 两个同时登录各自撤销对方，最终最后建立者胜出、其余被踢，而非旧实现的"都幸存"。
        if (multi.OnConflict == LoginConflictPolicy.Replace)
        {
            if (!multi.AllowMultiLogin)
            {
                // 单设备：撤销该用户其余全部登录会话（连同它们的刷新令牌）。
                // ★ 逐条撤销而不是「撤销该用户全部、只留新的这一条」：委派会话不受多登录策略管辖，
                //   本人在别处登录不能把它挤掉，而按用户整批撤销分不出它来。
                //   并发登录照样收敛：后读的一方看得见先建的会话并撤掉它，与整批撤销同一个结果。
                var others = (await GetValidSessionsAsync(userId)).Where(s => s.Id != newSessionId).ToList();
                foreach (var other in others)
                {
                    await RevokeSessionAsync(other.Id);
                }
            }
            else if (multi.MaxConcurrentSessions > 0)
            {
                // 限并发：连同新会话若超上限，按最后活动时间撤销最旧的若干个（排除新会话）。
                // ★ 按建完之后重读的集合算超出几条，不用建之前的快照：并发登录各自只看见旧快照时，
                //   两边都只挤掉同一条最旧的，结果比上限多一条。
                var others = (await GetValidSessionsAsync(userId))
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

    /// <summary>这套多登录策略是否限制会话条数：单设备，或设了并发上限。</summary>
    private static bool LimitsSessions(MultiLoginOptions multi)
        => !multi.AllowMultiLogin || multi.MaxConcurrentSessions > 0;

    /// <summary>
    /// Reject 策略下，账号已有 <paramref name="otherSessions"/> 条有效会话时本次登录该得到的拒绝；放行时为 <c>null</c>。
    /// 建会话之前的判定与建会话之后的复核用同一份规则、同一句话。
    /// </summary>
    private Result<Guid>? RejectionFor(MultiLoginOptions multi, int otherSessions)
    {
        if (!multi.AllowMultiLogin && otherSessions > 0)
        {
            return Fail<Guid>("Already logged in on another device", 403, ErrorCodes.IDENTITY_SESSION_ALREADY_ACTIVE);
        }

        if (multi.AllowMultiLogin && multi.MaxConcurrentSessions > 0 && otherSessions >= multi.MaxConcurrentSessions)
        {
            return Fail<Guid>("Maximum concurrent sessions reached", 403, ErrorCodes.IDENTITY_SESSION_LIMIT_REACHED);
        }

        return null;
    }

    /// <summary>
    /// 取当前有效、且受多登录策略管辖的会话（未撤销、未过期、不是委派会话）。<c>ExpiresAt == null</c> 视为不过期（遗留会话）。
    /// </summary>
    /// <remarks>
    /// 委派会话（<see cref="UserSessionDto.GroupKey"/> 非空）在这里就被排除：计数、Reject 判定、Replace 挤掉谁，
    /// 读的都是这一份，于是它既不占名额也不会被挤掉。
    /// </remarks>
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
                && s.GroupKey == null
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
