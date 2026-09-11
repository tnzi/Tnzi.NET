namespace Tnzi.Identity.Services;

/// <summary>
/// 基于数据库的会话管理服务实现
/// 适用于简单项目，不需要分布式支持的场景
/// </summary>
public class DatabaseSessionService : ApplicationService, ISessionService
{
    private readonly IRepository<UserSession, Guid> _repository;
    private readonly IRepository<User, Guid>? _userRepository;
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

    public DatabaseSessionService(IRepository<UserSession, Guid> repository, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
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
    public async Task<Guid> CreateSessionAsync(Guid userId, string? deviceInfo, string? ipAddress, string? userAgent, DateTime? expiresAt = null)
    {
        var now = DateTime.UtcNow;
        // 绝对上限在建立那一刻就定死，后续任何续期都不会推动它。
        var absoluteExpiresAt = SessionLifetime.ComputeAbsoluteExpiry(now, SessionOptions.AbsoluteLifetimeHours);

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

        await TouchAsync(sessionId, snapshot, context, ipChanged, now);

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
    /// </summary>
    private async Task TouchAsync(
        Guid sessionId, SessionSnapshot snapshot, SessionValidationContext context, bool ipChanged, DateTime now)
    {
        var staleActivity = snapshot.LastActivityTime.AddSeconds(ActivityWriteThrottleSeconds) <= now;
        if (!staleActivity && !ipChanged)
        {
            return;
        }

        var previousIp = snapshot.IpAddress;
        var newIp = ipChanged ? context.IpAddress : snapshot.IpAddress;

        await _repository
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.LastActivityTime, now)
                .SetProperty(s => s.IpAddress, newIp));

        // 缓存里的快照跟着更新，否则本窗口内的后续请求会反复触发同一次写。
        snapshot.LastActivityTime = now;
        snapshot.IpAddress = newIp;
        await StoreSnapshotAsync(sessionId, snapshot);

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

    private async Task InvalidateValidityCacheAsync(params Guid[] sessionIds)
    {
        if (_cache == null || sessionIds.Length == 0)
        {
            return;
        }

        foreach (var sessionId in sessionIds)
        {
            if (sessionId != Guid.Empty)
            {
                await _cache.RemoveAsync(ValidityCacheKey(sessionId));
            }
        }
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<UserSessionDto>>> GetUserSessionsAsync(Guid userId, bool includeRevoked = false)
    {
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

        var queryable = _repository.AsQueryable()
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
        var session = await _repository.GetAsync(sessionId);
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
        var session = await _repository.GetAsync(sessionId);
        if (session == null)
        {
            return Fail("Session not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (session.IsRevoked)
        {
            return Fail("Session is revoked", 400, ErrorCodes.VALIDATION_ERROR);
        }

        session.LastActivityTime = DateTime.UtcNow;
        await _repository.UpdateAsync(session);

        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> RenewSessionAsync(Guid sessionId, DateTime expiresAt)
    {
        var session = await _repository.GetAsync(sessionId);
        if (session == null || session.IsRevoked)
        {
            return Ok();
        }

        session.LastActivityTime = DateTime.UtcNow;
        // ★ 续期不得越过绝对上限，否则这条上限就只是一个不参与任何判断的字段：
        // 刷新令牌每次都把 ExpiresAt 推到「今天 + 刷新周期」，一条会话可以被无限续下去。
        session.ExpiresAt = SessionLifetime.ClampToAbsolute(expiresAt, session.AbsoluteExpiresAt);
        await _repository.UpdateAsync(session);
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
    public async Task<Result<IReadOnlyCollection<Guid>>> CleanInactiveSessionsAsync(TimeSpan inactiveThreshold)
    {
        var cutoffTime = DateTime.UtcNow - inactiveThreshold;

        var expiredSessions = await _repository
            .Where(us => !us.IsRevoked && us.LastActivityTime < cutoffTime)
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
        var activeSessions = _repository.Where(us => !us.IsRevoked);

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
        var aggregates = await _repository
            .Where(us => !us.IsRevoked)
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

