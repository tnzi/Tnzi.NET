
namespace Tnzi.Identity.Services;

/// <summary>
/// 基于 Redis 分布式缓存的会话管理服务实现
/// 适用于高并发、分布式部署场景
/// </summary>
public class DistributedSessionService : ApplicationService, ISessionService
{
    private readonly IDistributedCache _cache;
    private readonly SessionOptions _sessionOptions;
    private readonly IOptions<IdentityOptions>? _identityOptions;
    private readonly IRepository<UserSession, Guid>? _repository;
    private readonly IDistributedLock? _distributedLock;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // 锁超时时间
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    // 用户会话索引过期时间
    private static readonly TimeSpan UserSessionsIndexExpiration = TimeSpan.FromDays(30);

    public DistributedSessionService(
        IDistributedCache cache,
        IOptions<SessionOptions> sessionOptions,
        IServiceProvider serviceProvider,
        IRepository<UserSession, Guid>? repository = null,
        IDistributedLock? distributedLock = null,
        IOptions<IdentityOptions>? identityOptions = null)
        : base(serviceProvider)
    {
        _cache = Check.NotNull(cache);
        _sessionOptions = Check.NotNull(sessionOptions).Value;
        _identityOptions = identityOptions;
        _repository = repository;
        _distributedLock = distributedLock;

        // 验证：如果启用了数据库审计日志，必须有 repository
        if (_sessionOptions.KeepDatabaseAuditLog && _repository == null)
        {
            throw new InvalidOperationException(
                "Session.KeepDatabaseAuditLog is enabled but IRepository<UserSession, Guid> is not available. " +
                "Please ensure the Identity module's DbContext is properly configured, or disable KeepDatabaseAuditLog.");
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateSessionAsync(Guid userId, string? deviceInfo, string? ipAddress, string? userAgent)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var sessionData = new SessionData
        {
            Id = sessionId,
            UserId = userId,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreationTime = now,
            LastActivityTime = now,
            IsRevoked = false
        };

        // 存储到 Redis
        await SetSessionAsync(sessionData);

        // 添加到用户会话索引
        await AddToUserSessionsIndexAsync(userId, sessionId);

        // 如果启用了数据库审计日志，同时写入数据库
        if (_sessionOptions.KeepDatabaseAuditLog && _repository != null)
        {
            var session = new UserSession
            {
                Id = sessionId,
                UserId = userId,
                DeviceInfo = deviceInfo,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                CreationTime = now,
                LastActivityTime = now,
                IsRevoked = false
            };
            await _repository.InsertAsync(session);
        }

        LogInformation("Session created: {SessionId} for user: {UserId}", sessionId, userId);
        return sessionId;
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<UserSessionDto>>> GetUserSessionsAsync(Guid userId, bool includeRevoked = false)
    {
        var sessionIds = await GetUserSessionsIndexAsync(userId);
        if (!sessionIds.Any())
        {
            return Ok<IEnumerable<UserSessionDto>>(Enumerable.Empty<UserSessionDto>());
        }

        // 并行获取所有会话数据，减少总耗时
        var sessionDataTasks = sessionIds.Select(sessionId => GetSessionAsync(sessionId));
        var sessionDataResults = await Task.WhenAll(sessionDataTasks);

        var sessions = new List<UserSessionDto>();
        var expiredSessionIds = new List<Guid>();

        for (int i = 0; i < sessionIds.Count; i++)
        {
            var sessionData = sessionDataResults[i];
            if (sessionData == null)
            {
                // 会话已过期，记录待移除
                expiredSessionIds.Add(sessionIds[i]);
                continue;
            }

            if (!includeRevoked && sessionData.IsRevoked)
            {
                continue;
            }

            sessions.Add(new UserSessionDto
            {
                Id = sessionData.Id,
                UserId = sessionData.UserId,
                DeviceInfo = sessionData.DeviceInfo,
                IpAddress = sessionData.IpAddress,
                UserAgent = sessionData.UserAgent,
                CreationTime = sessionData.CreationTime,
                LastActivityTime = sessionData.LastActivityTime,
                IsRevoked = sessionData.IsRevoked,
                RevokedAt = sessionData.RevokedAt
            });
        }

        // 批量移除过期的会话索引（后台执行，不阻塞返回）
        // 使用 Task 执行后台清理，避免阻塞主流程
        // 注意：即使清理失败，过期会话也会在下次查询时自动清理，影响有限
        if (expiredSessionIds.Any())
        {
            _ = CleanupExpiredSessionsAsync(userId, expiredSessionIds);
        }

        return Ok<IEnumerable<UserSessionDto>>(sessions.OrderByDescending(s => s.LastActivityTime));
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<UserSessionDto>>> GetSessionsAsync(SessionQueryDto query)
    {
        Check.NotNull(query);

        // Redis 无法高效遍历全部会话 — 与 GetSessionStatisticsAsync 同理，
        // 启用数据库审计日志时降级到数据库查询。
        if (_sessionOptions.KeepDatabaseAuditLog && _repository != null)
        {
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

            if (sessions.Count > 0)
            {
                var userIds = sessions.Select(s => s.UserId).Distinct().ToList();
                var userRepository = ServiceProvider?.GetService<IRepository<User, Guid>>();
                if (userRepository != null)
                {
                    var users = await userRepository
                        .Where(u => userIds.Contains(u.Id))
                        .Select(u => new { u.Id, u.UserName })
                        .ToListAsync();
                    var nameMap = users.ToDictionary(u => u.Id, u => u.UserName);
                    foreach (var session in sessions)
                    {
                        session.UserName = nameMap.GetValueOrDefault(session.UserId);
                    }
                }
            }

            var paged = new PagedList<UserSessionDto>(sessions, query.PageIndex, query.PageSize, totalCount);
            return Ok<IPagedList<UserSessionDto>>(paged);
        }

        LogWarning("Global session listing is not available in Redis mode without KeepDatabaseAuditLog enabled.");
        return Ok<IPagedList<UserSessionDto>>(new PagedList<UserSessionDto>(new List<UserSessionDto>(), query.PageIndex, query.PageSize, 0));
    }

    /// <inheritdoc />
    public async Task<Result> RevokeSessionAsync(Guid sessionId)
    {
        var sessionData = await GetSessionAsync(sessionId);
        if (sessionData == null)
        {
            return Fail("Session not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (sessionData.IsRevoked)
        {
            return Fail("Session already revoked", 400, ErrorCodes.VALIDATION_ERROR);
        }

        sessionData.IsRevoked = true;
        sessionData.RevokedAt = DateTime.UtcNow;
        await SetSessionAsync(sessionData);

        // 从用户会话索引中移除
        await RemoveFromUserSessionsIndexAsync(sessionData.UserId, sessionId);

        // 如果启用了数据库审计日志，同时更新数据库
        if (_sessionOptions.KeepDatabaseAuditLog && _repository != null)
        {
            var dbSession = await _repository.GetAsync(sessionId);
            if (dbSession != null)
            {
                dbSession.IsRevoked = true;
                dbSession.RevokedAt = sessionData.RevokedAt;
                await _repository.UpdateAsync(dbSession);
            }
        }

        LogInformation("Session revoked: {SessionId}", sessionId);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> RevokeAllSessionsAsync(Guid userId, Guid? excludeSessionId = null)
    {
        var sessionIds = await GetUserSessionsIndexAsync(userId);

        // 过滤掉要排除的会话ID
        var sessionsToRevoke = excludeSessionId.HasValue
            ? sessionIds.Where(id => id != excludeSessionId.Value).ToList()
            : sessionIds.ToList();

        if (!sessionsToRevoke.Any())
        {
            return Ok();
        }

        // 并行获取所有会话数据，提升性能
        var sessionDataTasks = sessionsToRevoke.Select(sessionId => GetSessionAsync(sessionId));
        var sessionDataResults = await Task.WhenAll(sessionDataTasks);

        // 批量更新Redis中的会话状态（并行执行）
        var updateTasks = new List<Task>();
        for (int i = 0; i < sessionsToRevoke.Count; i++)
        {
            var sessionData = sessionDataResults[i];
            if (sessionData != null && !sessionData.IsRevoked)
            {
                sessionData.IsRevoked = true;
                sessionData.RevokedAt = DateTime.UtcNow;
                updateTasks.Add(SetSessionAsync(sessionData));
            }
        }
        await Task.WhenAll(updateTasks);

        // 从用户会话索引中移除（由于分布式锁，需要逐个处理，但可以并行尝试）
        // 注意：RemoveFromUserSessionsIndexAsync内部有锁保护，可以安全地并行调用
        var removeTasks = sessionsToRevoke.Select(sessionId => RemoveFromUserSessionsIndexAsync(userId, sessionId));
        await Task.WhenAll(removeTasks);

        // 如果启用了数据库审计日志，同时更新数据库
        if (_sessionOptions.KeepDatabaseAuditLog && _repository != null)
        {
            var dbSessions = await _repository.Where(us => us.UserId == userId && !us.IsRevoked)
                .ToListAsync();

            var sessionsToUpdate = excludeSessionId.HasValue
                ? dbSessions.Where(s => s.Id != excludeSessionId.Value).ToList()
                : dbSessions.ToList();

            foreach (var dbSession in sessionsToUpdate)
            {
                dbSession.IsRevoked = true;
                dbSession.RevokedAt = DateTime.UtcNow;
            }

            if (sessionsToUpdate.Any())
            {
                await _repository.UpdateManyAsync(sessionsToUpdate);
            }
        }

        LogInformation("All sessions revoked for user: {UserId} (excluding: {ExcludeSessionId})", userId, excludeSessionId?.ToString() ?? string.Empty);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> UpdateActivityTimeAsync(Guid sessionId)
    {
        var sessionData = await GetSessionAsync(sessionId);
        if (sessionData == null)
        {
            return Fail("Session not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (sessionData.IsRevoked)
        {
            return Fail("Session is revoked", 400, ErrorCodes.VALIDATION_ERROR);
        }

        sessionData.LastActivityTime = DateTime.UtcNow;
        await SetSessionAsync(sessionData);

        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<int>> CleanExpiredSessionsAsync(TimeSpan inactiveThreshold)
    {
        // Redis 会话使用 TTL 自动过期，无法高效遍历所有会话
        // 如果启用了数据库审计日志，清理数据库中的过期记录
        if (_sessionOptions.KeepDatabaseAuditLog && _repository != null)
        {
            var cutoffTime = DateTime.UtcNow - inactiveThreshold;

            var expiredSessions = await _repository
                .Where(us => !us.IsRevoked && us.LastActivityTime < cutoffTime)
                .ToListAsync();

            if (!expiredSessions.Any())
            {
                return Ok(0);
            }

            foreach (var session in expiredSessions)
            {
                session.IsRevoked = true;
                session.RevokedAt = DateTime.UtcNow;
            }

            await _repository.UpdateManyAsync(expiredSessions);

            LogInformation("Cleaned {Count} expired sessions from database (inactive since {CutoffTime})", expiredSessions.Count, cutoffTime);
            return Ok(expiredSessions.Count);
        }

        // Redis 模式下没有数据库审计日志，会话由 Redis TTL 自动清理
        LogInformation("Redis sessions are automatically cleaned by TTL expiration. No manual cleanup needed.");
        return Ok(0);
    }

    /// <inheritdoc />
    public async Task<Result<SessionStatisticsDto>> GetSessionStatisticsAsync()
    {
        // Redis 无法高效遍历所有会话，降级到数据库查询
        if (_sessionOptions.KeepDatabaseAuditLog && _repository != null)
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

            return Ok(new SessionStatisticsDto
            {
                ActiveSessionCount = activeSessionCount,
                OnlineUserCount = onlineUserCount,
                TopDevices = topDevices
            });
        }

        // 没有数据库审计日志时，无法提供统计
        LogWarning("Session statistics are not available in Redis mode without KeepDatabaseAuditLog enabled.");
        return Ok(new SessionStatisticsDto());
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<ActiveUserSummaryDto>>> GetActiveUsersAsync(int top = 50)
    {
        if (top <= 0) top = 50;
        if (top > 500) top = 500;

        // Same rationale as GetSessionStatisticsAsync — Redis can't aggregate
        // efficiently, so we fall back to the DB audit log when present.
        if (_sessionOptions.KeepDatabaseAuditLog && _repository != null)
        {
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

            var userIds = aggregates.Select(a => a.UserId).ToList();
            var userRepository = ServiceProvider?.GetService<IRepository<User, Guid>>();
            Dictionary<Guid, string?> nameMap;
            if (userRepository != null)
            {
                var users = await userRepository
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

        LogWarning("Active user list is not available in Redis mode without KeepDatabaseAuditLog enabled.");
        return Ok<IEnumerable<ActiveUserSummaryDto>>(Array.Empty<ActiveUserSummaryDto>());
    }

    #region Redis 操作辅助方法

    private string GetSessionKey(Guid sessionId) => $"{_sessionOptions.RedisKeyPrefix}:{sessionId}";
    private string GetUserSessionsIndexKey(Guid userId) => $"{_sessionOptions.RedisKeyPrefix}:User:{userId}";

    private async Task<SessionData?> GetSessionAsync(Guid sessionId)
    {
        var key = GetSessionKey(sessionId);
        var json = await _cache.GetStringAsync(key);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<SessionData>(json, JsonOptions);
    }

    private async Task SetSessionAsync(SessionData sessionData)
    {
        var key = GetSessionKey(sessionData.Id);
        var json = JsonSerializer.Serialize(sessionData, JsonOptions);

        var options = new DistributedCacheEntryOptions();

        // 获取过期时间：如果 ExpirationMinutes 为 0，尝试使用 AccountSecurity.SessionTimeoutMinutes
        var expirationMinutes = _sessionOptions.ExpirationMinutes;
        if (expirationMinutes == 0)
        {
            // 尝试从 IdentityOptions 获取 AccountSecurity.SessionTimeoutMinutes
            var accountSecurityTimeout = _identityOptions?.Value?.AccountSecurity?.SessionTimeoutMinutes ?? 0;
            if (accountSecurityTimeout > 0)
            {
                expirationMinutes = accountSecurityTimeout;
            }
            // 如果仍然为 0，则不设置过期时间（永不过期）
        }

        if (expirationMinutes > 0)
        {
            if (_sessionOptions.SlidingExpiration)
            {
                options.SlidingExpiration = TimeSpan.FromMinutes(expirationMinutes);
            }
            else
            {
                options.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(expirationMinutes);
            }
        }

        await _cache.SetStringAsync(key, json, options);
    }

    private async Task<List<Guid>> GetUserSessionsIndexAsync(Guid userId)
    {
        var key = GetUserSessionsIndexKey(userId);
        var json = await _cache.GetStringAsync(key);
        if (string.IsNullOrEmpty(json))
        {
            return new List<Guid>();
        }

        return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? new List<Guid>();
    }

    private async Task SetUserSessionsIndexAsync(Guid userId, List<Guid> sessionIds)
    {
        var key = GetUserSessionsIndexKey(userId);
        var json = JsonSerializer.Serialize(sessionIds, JsonOptions);

        // 用户会话索引使用较长的过期时间
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = UserSessionsIndexExpiration
        };

        await _cache.SetStringAsync(key, json, options);
    }

    private async Task AddToUserSessionsIndexAsync(Guid userId, Guid sessionId)
    {
        var lockKey = $"{_sessionOptions.RedisKeyPrefix}:Lock:User:{userId}";

        // 如果有分布式锁，使用锁保护并发操作
        if (_distributedLock != null)
        {
            await using var handle = await _distributedLock.AcquireAsync(lockKey, LockTimeout);
            if (handle != null)
            {
                await AddToIndexWithoutLockAsync(userId, sessionId);
            }
            else
            {
                // 获取锁失败，不执行无锁操作以避免数据不一致
                // 记录错误，会话索引可能不完整，但会话数据本身已保存，影响有限
                LogWarning("Failed to acquire lock for user session index: {UserId}. Session index update skipped to avoid race condition.", userId);
            }
        }
        else
        {
            // 无分布式锁时直接操作（可能存在竞态条件，但影响有限）
            // 注意：在高并发场景下，建议配置分布式锁服务
            await AddToIndexWithoutLockAsync(userId, sessionId);
        }
    }

    private async Task AddToIndexWithoutLockAsync(Guid userId, Guid sessionId)
    {
        var sessionIds = await GetUserSessionsIndexAsync(userId);
        if (!sessionIds.Contains(sessionId))
        {
            sessionIds.Add(sessionId);
            await SetUserSessionsIndexAsync(userId, sessionIds);
        }
    }

    private async Task RemoveFromUserSessionsIndexAsync(Guid userId, Guid sessionId)
    {
        var lockKey = $"{_sessionOptions.RedisKeyPrefix}:Lock:User:{userId}";

        // 如果有分布式锁，使用锁保护并发操作
        if (_distributedLock != null)
        {
            await using var handle = await _distributedLock.AcquireAsync(lockKey, LockTimeout);
            if (handle != null)
            {
                await RemoveFromIndexWithoutLockAsync(userId, sessionId);
            }
            else
            {
                // 获取锁失败，不执行无锁操作以避免数据不一致
                // 记录警告，索引可能包含已撤销的会话，但会在后续查询时自动清理
                LogWarning("Failed to acquire lock for user session index removal: {UserId}. Index update skipped to avoid race condition.", userId);
            }
        }
        else
        {
            // 无分布式锁时直接操作
            // 注意：在高并发场景下，建议配置分布式锁服务
            await RemoveFromIndexWithoutLockAsync(userId, sessionId);
        }
    }

    private async Task RemoveFromIndexWithoutLockAsync(Guid userId, Guid sessionId)
    {
        var sessionIds = await GetUserSessionsIndexAsync(userId);
        if (sessionIds.Remove(sessionId))
        {
            await SetUserSessionsIndexAsync(userId, sessionIds);
        }
    }

    /// <summary>
    /// 后台清理过期的会话索引
    /// </summary>
    private async Task CleanupExpiredSessionsAsync(Guid userId, List<Guid> expiredSessionIds)
    {
        var failedCount = 0;
        foreach (var expiredSessionId in expiredSessionIds)
        {
            try
            {
                await RemoveFromUserSessionsIndexAsync(userId, expiredSessionId);
            }
            catch (Exception ex)
            {
                failedCount++;
                Logger.LogWarning(ex, "Failed to remove expired session {SessionId} from index for user {UserId}", expiredSessionId, userId);
                // 如果失败次数过多，停止处理以避免日志洪水
                if (failedCount > expiredSessionIds.Count / 2)
                {
                    LogWarning("Too many failures when removing expired sessions, stopping cleanup for user {UserId}", userId);
                    break;
                }
            }
        }

        if (failedCount > 0 && failedCount < expiredSessionIds.Count)
        {
            LogInformation("Removed {SuccessCount} expired sessions from index for user {UserId}, {FailedCount} failed",
                expiredSessionIds.Count - failedCount, userId, failedCount);
        }
    }

    #endregion

    /// <summary>
    /// 会话数据（Redis存储用）
    /// </summary>
    private class SessionData
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string? DeviceInfo { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastActivityTime { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}