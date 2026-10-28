namespace Tnzi.Identity.Services;

/// <summary>
/// 基于数据库的会话管理服务实现
/// 适用于简单项目，不需要分布式支持的场景
/// </summary>
public class DatabaseSessionService : ApplicationService, ISessionService
{
    private readonly IRepository<UserSession, Guid> _repository;
    private readonly IRepository<User, Guid>? _userRepository;
    private readonly IUserTenantScopeProvider _scope;
    // Optional - validity cache for the per-request OnTokenValidated check, so an
    // authenticated request doesn't hit the DB for every call. Invalidated on revoke.
    private readonly ICache? _cache;
    private readonly IEventBus? _eventBus;
    private readonly IOptionsMonitor<SessionOptions>? _sessionOptionsMonitor;
    private readonly IOptionsMonitor<IdentityOptions>? _identityOptionsMonitor;
    private readonly SessionOptions _fallbackSessionOptions = new();

    private const string ValidityCachePrefix = "identity:session:valid:";

    /// <summary>
    /// 活动时间的最小写入间隔（秒）。闲置超时要有意义，就得有人在普通请求上更新
    /// <see cref="UserSession.LastActivityTime"/>；但每请求一次数据库写是不可接受的开销，
    /// 而闲置超时的量级是分钟，一分钟的粒度对它没有任何影响。
    /// </summary>
    private const int ActivityWriteThrottleSeconds = 60;

    private SessionOptions SessionOptions => _sessionOptionsMonitor?.CurrentValue ?? _fallbackSessionOptions;

    /// <param name="repository">会话仓储。</param>
    /// <param name="serviceProvider">服务提供者。</param>
    /// <param name="scope">
    /// 当前请求能碰到哪些用户。<c>UserSession</c> 没有 <c>TenantId</c>，全局过滤器管不到它，
    /// 管理端按用户读 / 撤销、全局列表与统计都要按用户表的租户裁剪 —— 少了这一道，
    /// 租户管理员能列出并踢掉任何租户的任何人（见 <see cref="UserTenantScope"/>）。
    /// 必填而不是可选：没有它就没有裁剪，而「没裁剪」在返回值上看不出来。
    /// </param>
    public DatabaseSessionService(IRepository<UserSession, Guid> repository, IServiceProvider serviceProvider, IUserTenantScopeProvider scope)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _scope = Check.NotNull(scope);
        // Optional - only needed by GetActiveUsersAsync, resolved lazily so
        // existing call paths and tests that don't register the user repository
        // still construct the service successfully.
        _userRepository = serviceProvider.GetService<IRepository<User, Guid>>();
        _cache = serviceProvider.GetService<ICache>();
        _eventBus = serviceProvider.GetService<IEventBus>();
        // IOptionsMonitor 而不是 IOptions：绑定 / 闲置超时这几项都在配置中心里可热改，
        // 构造时取快照会让「改了但要重启才生效」，而重启前没有任何地方看得出来。
        _sessionOptionsMonitor = serviceProvider.GetService<IOptionsMonitor<SessionOptions>>();
        _identityOptionsMonitor = serviceProvider.GetService<IOptionsMonitor<IdentityOptions>>();
    }

    private static string ValidityCacheKey(Guid sessionId) => ValidityCachePrefix + sessionId.ToString("N");

    /// <summary>
    /// 闲置超时（分钟，0 = 不启用）。取 <c>Identity:AccountSecurity:SessionTimeoutMinutes</c>。
    /// </summary>
    private int IdleTimeoutMinutes => _identityOptionsMonitor?.CurrentValue.AccountSecurity.SessionTimeoutMinutes ?? 0;

    /// <inheritdoc />
    public Task<Guid> CreateSessionAsync(Guid userId, string? deviceInfo, string? ipAddress, string? userAgent, DateTime? expiresAt = null)
        => InsertSessionAsync(userId, deviceInfo, ipAddress, userAgent, expiresAt, delegation: null);

    /// <inheritdoc />
    public Task<Guid> CreateDelegatedSessionAsync(DelegatedSessionCreation creation)
    {
        Check.NotNull(creation);
        return InsertSessionAsync(
            creation.UserId, creation.DeviceInfo, creation.IpAddress, creation.UserAgent, creation.ExpiresAt, creation.Delegation);
    }

    /// <inheritdoc />
    public async Task<DelegatedSessionInfo?> GetDelegationAsync(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        var row = await _repository
            .Where(s => s.Id == sessionId && s.GroupKey != null)
            .Select(s => new { s.GroupKey, s.Label, s.ClaimsJson, s.AbsoluteExpiresAt })
            .FirstOrDefaultAsync();
        if (row == null)
        {
            return null;
        }

        // 委派会话建立时恒有绝对到期时间；读到 null 说明这一行被改过，按已到期处理而不是按不过期。
        return new DelegatedSessionInfo(
            row.GroupKey!, row.Label, DelegatedSessionRules.DeserializeClaims(row.ClaimsJson), row.AbsoluteExpiresAt ?? DateTime.MinValue);
    }

    private async Task<Guid> InsertSessionAsync(
        Guid userId, string? deviceInfo, string? ipAddress, string? userAgent, DateTime? expiresAt, DelegatedSessionInfo? delegation)
    {
        var now = DateTime.UtcNow;
        // 绝对上限在建立那一刻就定死，后续任何续期都不会推动它。
        // 委派会话另有请求方给的到期时间，取两者里更早的那个。
        var absoluteExpiresAt = SessionLifetime.ComputeAbsoluteExpiry(now, SessionOptions.AbsoluteLifetimeHours);
        if (delegation != null)
        {
            absoluteExpiresAt = SessionLifetime.ClampToAbsolute(delegation.AbsoluteExpiresAt, absoluteExpiresAt);
        }

        var session = new UserSession
        {
            UserId = userId,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreationTime = now,
            LastActivityTime = now,
            // 建立时也要夹：绝对上限比刷新令牌周期还短的部署（例如上限 8 小时、刷新 7 天），
            // 不夹的话 ExpiresAt 会比真实寿命长得多，而并发计数读的正是它 ——
            // 于是已经死掉的会话仍占着并发名额。
            ExpiresAt = expiresAt.HasValue
                ? SessionLifetime.ClampToAbsolute(expiresAt.Value, absoluteExpiresAt)
                : absoluteExpiresAt,
            AbsoluteExpiresAt = absoluteExpiresAt,
            GroupKey = delegation?.GroupKey,
            Label = delegation?.Label,
            ClaimsJson = delegation == null ? null : DelegatedSessionRules.SerializeClaims(delegation.Claims),
            IsRevoked = false
        };

        await _repository.InsertAsync(session);
        return session.Id;
    }

    /// <inheritdoc />
    public async Task<bool> IsSessionValidAsync(Guid sessionId)
    {
        var snapshot = await GetSnapshotAsync(sessionId);
        return snapshot != null && IsAlive(snapshot, DateTime.UtcNow);
    }

    /// <inheritdoc />
    public async Task<SessionValidationResult> ValidateAsync(Guid sessionId, SessionValidationContext context)
    {
        Check.NotNull(context);

        var snapshot = await GetSnapshotAsync(sessionId);
        var now = DateTime.UtcNow;

        if (snapshot == null || !IsAlive(snapshot, now))
        {
            return SessionValidationResult.Invalid;
        }

        var options = SessionOptions;

        // ① 设备特征。抹掉版本号之后仍然对不上，说明令牌换了一台机器在用。
        if (options.BindToUserAgent && !SessionBinding.Matches(snapshot.UserAgent, context.UserAgent))
        {
            LogWarning(
                "Session {SessionId} rejected: user-agent fingerprint changed (bound at sign-in, differs now).",
                sessionId);
            return SessionValidationResult.BindingMismatch;
        }

        // ② 来源地址。默认只记录不拦：换 Wi-Fi、切蜂窝、VPN 都会让正常会话换地址。
        var ipChanged = options.IpChangeBehavior != SessionIpChangeBehavior.Ignore
            && !string.IsNullOrEmpty(context.IpAddress)
            && !string.IsNullOrEmpty(snapshot.IpAddress)
            && !string.Equals(snapshot.IpAddress, context.IpAddress, StringComparison.OrdinalIgnoreCase);

        if (ipChanged && options.IpChangeBehavior == SessionIpChangeBehavior.Revoke)
        {
            LogWarning("Session {SessionId} rejected: source address changed and policy is Revoke.", sessionId);
            return SessionValidationResult.BindingMismatch;
        }

        // 写回时发现行已经撤销（快照读在撤销之前）：这一次就判失效，而不是再放行一次。
        if (!await TouchAsync(sessionId, snapshot, context, ipChanged, now))
        {
            return SessionValidationResult.Invalid;
        }

        return SessionValidationResult.Valid;
    }

    /// <inheritdoc />
    public async Task<UserSessionDto?> GetSessionAsync(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        return await _repository
            .Where(s => s.Id == sessionId)
            .ProjectTo<UserSession, UserSessionDto>()
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 会话是不是还活着。判据本身在 <see cref="SessionLifetime.IsAlive"/> —— 两个会话后端共用一份，
    /// 且它是纯函数，闲置超时那条分支因此有测试覆盖得到（默认关闭时集成测试跑不到它）。
    /// </summary>
    private bool IsAlive(SessionSnapshot snapshot, DateTime now)
        => SessionLifetime.IsAlive(
            snapshot.IsRevoked,
            snapshot.ExpiresAt,
            snapshot.AbsoluteExpiresAt,
            snapshot.LastActivityTime,
            IdleTimeoutMinutes,
            now);

    /// <summary>
    /// 续期活动时间（并在地址变化时更新地址、发一条事件）。按 <see cref="ActivityWriteThrottleSeconds"/> 节流。
    /// 返回会话是否仍然有效：写回时发现行已被撤销（或已不存在）返回 <c>false</c>，并清掉缓存里的旧快照。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>先放快照、再做带条件的写，顺序不能反。</strong>此前是「无条件写两列 → 把快照放回缓存」：
    /// 快照若是撤销之前读的，撤销刚清掉的缓存键就被这份 <c>IsRevoked = false</c> 的旧快照重新占上；
    /// 换着两个出口地址发请求，每一次都会写回、每一次都续上一个缓存周期，被撤销的 access token 一直过得了校验。
    /// 现在写只在行未撤销时生效（<c>WHERE !IsRevoked</c>），影响 0 行就说明撤销已经提交，清掉快照、判失效；
    /// 影响 1 行则这次写先于撤销落在这一行上，撤销提交之后的缓存失效必然晚于这里的放快照，旧快照活不下来。
    /// </remarks>
    private async Task<bool> TouchAsync(
        Guid sessionId, SessionSnapshot snapshot, SessionValidationContext context, bool ipChanged, DateTime now)
    {
        var staleActivity = snapshot.LastActivityTime.AddSeconds(ActivityWriteThrottleSeconds) <= now;
        if (!staleActivity && !ipChanged)
        {
            return true;
        }

        var previousIp = snapshot.IpAddress;
        var newIp = ipChanged ? context.IpAddress : snapshot.IpAddress;

        // 缓存里的快照跟着更新，否则本窗口内的后续请求会反复触发同一次写。
        snapshot.LastActivityTime = now;
        snapshot.IpAddress = newIp;
        await StoreSnapshotAsync(sessionId, snapshot);

        var touched = await _repository
            .Where(s => s.Id == sessionId && !s.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.LastActivityTime, now)
                .SetProperty(s => s.IpAddress, newIp));

        if (touched == 0)
        {
            await InvalidateValidityCacheAsync(sessionId);
            LogWarning("Session {SessionId} was revoked while a request was validating it; rejecting the request.", sessionId);
            return false;
        }

        if (ipChanged && _eventBus != null)
        {
            // 地址一变就更新记录，所以同一次变化只会发一条。
            await _eventBus.PublishAsync(new SessionIpChangedEvent
            {
                UserId = snapshot.UserId,
                SessionId = sessionId,
                PreviousIpAddress = previousIp,
                CurrentIpAddress = context.IpAddress,
                ChangedTime = now
            }, cancellationToken: default);
        }

        return true;
    }

    private async Task<SessionSnapshot?> GetSnapshotAsync(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        var cacheSeconds = SessionOptions.ValidationCacheSeconds;
        var useCache = _cache != null && cacheSeconds > 0;
        var cacheKey = ValidityCacheKey(sessionId);

        if (useCache)
        {
            var cached = await _cache!.GetAsync<SessionSnapshot>(cacheKey);
            if (cached != null)
            {
                return cached;
            }
        }

        var snapshot = await _repository
            .Where(s => s.Id == sessionId)
            .Select(s => new SessionSnapshot
            {
                UserId = s.UserId,
                IsRevoked = s.IsRevoked,
                ExpiresAt = s.ExpiresAt,
                AbsoluteExpiresAt = s.AbsoluteExpiresAt,
                LastActivityTime = s.LastActivityTime,
                UserAgent = s.UserAgent,
                IpAddress = s.IpAddress,
            })
            .FirstOrDefaultAsync();

        if (snapshot != null && useCache)
        {
            await StoreSnapshotAsync(sessionId, snapshot);
        }

        return snapshot;
    }

    private async Task StoreSnapshotAsync(Guid sessionId, SessionSnapshot snapshot)
    {
        var cacheSeconds = SessionOptions.ValidationCacheSeconds;
        if (_cache == null || cacheSeconds <= 0)
        {
            return;
        }

        await _cache.SetAsync(ValidityCacheKey(sessionId), snapshot, TimeSpan.FromSeconds(cacheSeconds));
    }

    /// <summary>
    /// 每请求校验所需的会话字段。刻意是可变的普通类而不是 record：
    /// 它要经缓存序列化往返，也要在 <see cref="TouchAsync"/> 里就地更新后写回。
    /// </summary>
    private sealed class SessionSnapshot
    {
        public Guid UserId { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? AbsoluteExpiresAt { get; set; }
        public DateTime LastActivityTime { get; set; }
        public string? UserAgent { get; set; }
        public string? IpAddress { get; set; }
    }

    /// <summary>
    /// 清掉校验缓存里的快照；身处事务时提交之后再清一遍。
    /// </summary>
    /// <remarks>
    /// ★★ 只在写入之后、提交之前清一遍不够：撤销要到提交才对别的连接可见，这之间另一个请求缓存未命中，
    /// 读到未撤销的行并放回缓存，被撤销的会话在一个缓存周期里照样放行（「登出所有设备」本身就跑在事务里）。
    /// 不在事务里时写入已经自动提交，此刻这一遍就是提交后的那一遍。
    /// </remarks>
    private async Task InvalidateValidityCacheAsync(params Guid[] sessionIds)
    {
        if (_cache == null || sessionIds.Length == 0)
        {
            return;
        }

        var keys = sessionIds.Where(id => id != Guid.Empty).Select(ValidityCacheKey).ToArray();
        await RemoveValidityCacheKeysAsync(keys);

        if (AmbientUnitOfWork.Current is { IsTransactionActive: true } transaction)
        {
            transaction.EnqueuePostCommit(_ => RemoveValidityCacheKeysAsync(keys));
        }
    }

    private async Task RemoveValidityCacheKeysAsync(IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            await _cache!.RemoveAsync(key);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<UserSessionDto>>> GetUserSessionsAsync(Guid userId, bool includeRevoked = false)
    {
        if (!await _scope.ContainsAsync(userId))
        {
            return Fail<IEnumerable<UserSessionDto>>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var query = _repository.Where(us => us.UserId == userId);

        if (!includeRevoked)
        {
            query = query.Where(us => !us.IsRevoked);
        }

        var sessions = await query
            .OrderByDescending(us => us.LastActivityTime)
            .ProjectTo<UserSession, UserSessionDto>()
            .ToListAsync();

        return Ok<IEnumerable<UserSessionDto>>(sessions);
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<UserSessionDto>>> GetSessionsAsync(SessionQueryDto query)
    {
        Check.NotNull(query);

        var queryable = WhereInScope(_repository.AsQueryable())
            .WhereIf(us => us.UserId == query.UserId!.Value, query.UserId.HasValue)
            .WhereIf(us => !us.IsRevoked, !query.IncludeRevoked)
            .OrderByDescending(us => us.LastActivityTime)
            .ThenByDescending(us => us.CreationTime);

        var totalCount = await queryable.CountAsync();
        var sessions = await queryable
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .ProjectTo<UserSession, UserSessionDto>()
            .ToListAsync();

        await PopulateUserNamesAsync(sessions);

        var paged = new PagedList<UserSessionDto>(sessions, query.PageIndex, query.PageSize, totalCount);
        return Ok<IPagedList<UserSessionDto>>(paged);
    }

    /// <summary>
    /// 把「当前租户范围」加到会话查询上：全局列表、统计、活跃用户都经它。
    /// 不裁剪时原样返回；裁剪时翻译成 <c>UserId IN (SELECT Id FROM User WHERE ...)</c>。
    /// </summary>
    private IQueryable<UserSession> WhereInScope(IQueryable<UserSession> query)
    {
        var ids = _scope.InScopeUserIds();
        return ids == null ? query : query.Where(us => ids.Contains(us.UserId));
    }

    /// <summary>
    /// 按 id 取会话，且只取其主人在范围内的：不在范围内与不存在同样返回 <c>null</c>，调用方一律答 404。
    /// </summary>
    private async Task<UserSession?> FindScopedSessionAsync(Guid sessionId)
    {
        var session = await _repository.GetAsync(sessionId);
        if (session == null)
        {
            return null;
        }

        return await _scope.ContainsAsync(session.UserId) ? session : null;
    }

    /// <summary>
    /// 批量填充会话 DTO 的 UserName。单独查询（非 JOIN），避免 User 表软删过滤器
    /// 静默丢掉软删用户的会话行（此时 UserName 保持 null）。
    /// </summary>
    private async Task PopulateUserNamesAsync(List<UserSessionDto> sessions)
    {
        if (sessions.Count == 0 || _userRepository == null)
        {
            return;
        }

        var userIds = sessions.Select(s => s.UserId).Distinct().ToList();
        var users = await _userRepository
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync();
        var nameMap = users.ToDictionary(u => u.Id, u => u.UserName);

        foreach (var session in sessions)
        {
            session.UserName = nameMap.GetValueOrDefault(session.UserId);
        }
    }

    /// <inheritdoc />
    public async Task<Result> RevokeSessionAsync(Guid sessionId)
    {
        var session = await FindScopedSessionAsync(sessionId);
        if (session == null)
        {
            return Fail("Session not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (session.IsRevoked)
        {
            return Fail("Session already revoked", 400, ErrorCodes.VALIDATION_ERROR);
        }

        session.IsRevoked = true;
        session.RevokedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(session);
        await InvalidateValidityCacheAsync(sessionId);

        LogInformation("Session revoked: {SessionId}", sessionId);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> RevokeAllSessionsAsync(Guid userId, Guid? excludeSessionId = null)
    {
        if (!await _scope.ContainsAsync(userId))
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var query = _repository.Where(us => us.UserId == userId && !us.IsRevoked);

            if (excludeSessionId.HasValue)
            {
                query = query.Where(us => us.Id != excludeSessionId.Value);
            }

            var sessions = await query.ToListAsync(cancellationToken);

            foreach (var session in sessions)
            {
                session.IsRevoked = true;
                session.RevokedAt = DateTime.UtcNow;
            }

            if (sessions.Any())
            {
                await _repository.UpdateManyAsync(sessions);
                await InvalidateValidityCacheAsync(sessions.Select(s => s.Id).ToArray());
            }
        });

        LogInformation("All sessions revoked for user: {UserId} (excluding: {ExcludeSessionId})", userId, excludeSessionId?.ToString() ?? string.Empty);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> UpdateActivityTimeAsync(Guid sessionId)
    {
        var session = await FindScopedSessionAsync(sessionId);
        if (session == null)
        {
            return Fail("Session not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 带条件的写（理由见 RenewSessionAsync）：读到的「未撤销」可能已经过时，整行写回会把撤销抹掉。
        var touched = session.IsRevoked
            ? 0
            : await _repository
                .Where(s => s.Id == sessionId && !s.IsRevoked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastActivityTime, DateTime.UtcNow));

        if (touched == 0)
        {
            return Fail("Session is revoked", 400, ErrorCodes.VALIDATION_ERROR);
        }

        return Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// ★★★ <strong>只写两列，且只在行未撤销时写。</strong>此前读出整行、改两列、再 <c>UpdateAsync</c> 整行写回
    /// （实体被标成 <c>Modified</c>，所有列都写）：撤销落在这次刷新的读与写之间，就被 <c>IsRevoked = false</c> 盖掉，
    /// 撤销答了成功，这次刷新签出的 access token 却一直过得了校验。绝对上限建立时定死、不会变，先读它来夹紧是安全的。
    /// </remarks>
    public async Task<Result> RenewSessionAsync(Guid sessionId, DateTime expiresAt)
    {
        var current = await _repository
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.IsRevoked, s.AbsoluteExpiresAt })
            .FirstOrDefaultAsync();
        if (current == null || current.IsRevoked)
        {
            return Ok();
        }

        // ★ 续期不得越过绝对上限，否则这条上限就只是一个不参与任何判断的字段：
        // 刷新令牌每次都把 ExpiresAt 推到「今天 + 刷新周期」，一条会话可以被无限续下去。
        DateTime? renewedExpiry = SessionLifetime.ClampToAbsolute(expiresAt, current.AbsoluteExpiresAt);
        var renewedActivity = DateTime.UtcNow;
        await _repository
            .Where(s => s.Id == sessionId && !s.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.LastActivityTime, renewedActivity)
                .SetProperty(s => s.ExpiresAt, renewedExpiry));

        // 续期后有效性可能延长，清缓存让下次校验读到新值。
        await InvalidateValidityCacheAsync(sessionId);

        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<int>> CleanExpiredSessionsAsync(TimeSpan inactiveThreshold)
    {
        var result = await CleanInactiveSessionsAsync(inactiveThreshold);
        return result.Succeeded ? Ok(result.Data!.Count) : Fail<int>(result.Message ?? "Cleanup failed", result.Code ?? 500);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ★ 清扫按「谁不活跃了」整表扫，不经会话 id 也不经用户 id，于是单条 / 该用户全部那两处的范围判断
    /// 一处都罩不到它：租户管理员传一个 0 分钟阈值，所有租户的全部会话一次翻成已撤销、绑定的刷新令牌随之全删。
    /// 与列表 / 统计同源裁剪；没有主体的后台维护任务不裁剪，仍是全量。
    /// </remarks>
    public async Task<Result<IReadOnlyCollection<Guid>>> CleanInactiveSessionsAsync(TimeSpan inactiveThreshold)
    {
        var cutoffTime = DateTime.UtcNow - inactiveThreshold;

        var expiredSessions = await WhereInScope(_repository.Where(us => !us.IsRevoked && us.LastActivityTime < cutoffTime))
            .ToListAsync();

        if (expiredSessions.Count == 0)
        {
            return Ok<IReadOnlyCollection<Guid>>(Array.Empty<Guid>());
        }

        foreach (var session in expiredSessions)
        {
            session.IsRevoked = true;
            session.RevokedAt = DateTime.UtcNow;
        }

        var ids = expiredSessions.Select(s => s.Id).ToArray();
        await _repository.UpdateManyAsync(expiredSessions);
        await InvalidateValidityCacheAsync(ids);

        LogInformation("Cleaned {Count} expired sessions (inactive since {CutoffTime})", ids.Length, cutoffTime);
        return Ok<IReadOnlyCollection<Guid>>(ids);
    }

    /// <inheritdoc />
    public async Task<Result<SessionStatisticsDto>> GetSessionStatisticsAsync()
    {
        var activeSessions = WhereInScope(_repository.Where(us => !us.IsRevoked));

        var activeSessionCount = await activeSessions.CountAsync();
        var onlineUserCount = await activeSessions.Select(us => us.UserId).Distinct().CountAsync();

        var topDevices = await activeSessions
            .Where(us => us.DeviceInfo != null && us.DeviceInfo != string.Empty)
            .GroupBy(us => us.DeviceInfo!)
            .Select(g => new DeviceStatItem
            {
                DeviceInfo = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(d => d.Count)
            .Take(5)
            .ToListAsync();

        var statistics = new SessionStatisticsDto
        {
            ActiveSessionCount = activeSessionCount,
            OnlineUserCount = onlineUserCount,
            TopDevices = topDevices
        };

        return Ok(statistics);
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<ActiveUserSummaryDto>>> GetActiveUsersAsync(int top = 50)
    {
        if (top <= 0) top = 50;
        if (top > 500) top = 500;

        // Step 1: GROUP BY on UserSession to get top-N userIds + per-user aggregates.
        // Single-column join key keeps the GROUP BY index-friendly across providers.
        var aggregates = await WhereInScope(_repository.Where(us => !us.IsRevoked))
            .GroupBy(us => us.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                SessionCount = g.Count(),
                LastActivityTime = g.Max(us => us.LastActivityTime),
            })
            .OrderByDescending(x => x.LastActivityTime)
            .Take(top)
            .ToListAsync();

        if (aggregates.Count == 0)
        {
            return Ok<IEnumerable<ActiveUserSummaryDto>>(Array.Empty<ActiveUserSummaryDto>());
        }

        // Step 2: batch-fetch usernames for those userIds. Done in a separate
        // query (not a JOIN) so that the GROUP BY plan stays clean and the
        // User table's soft-delete filter does not silently drop active-user
        // rows for soft-deleted accounts (we still surface them as null name).
        var userIds = aggregates.Select(a => a.UserId).ToList();
        Dictionary<Guid, string?> nameMap;
        if (_userRepository != null)
        {
            var users = await _userRepository
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName })
                .ToListAsync();
            nameMap = users.ToDictionary(u => u.Id, u => u.UserName);
        }
        else
        {
            nameMap = new Dictionary<Guid, string?>();
        }

        var result = aggregates
            .Select(a => new ActiveUserSummaryDto
            {
                UserId = a.UserId,
                UserName = nameMap.GetValueOrDefault(a.UserId),
                SessionCount = a.SessionCount,
                LastActivityTime = a.LastActivityTime,
            })
            .ToList();

        return Ok<IEnumerable<ActiveUserSummaryDto>>(result);
    }
}

