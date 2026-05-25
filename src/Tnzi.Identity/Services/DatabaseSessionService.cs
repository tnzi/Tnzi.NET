namespace Tnzi.Identity.Services;

/// <summary>
/// 基于数据库的会话管理服务实现
/// 适用于简单项目，不需要分布式支持的场景
/// </summary>
public class DatabaseSessionService : ApplicationService, ISessionService
{
    private readonly IRepository<UserSession, Guid> _repository;
    private readonly IRepository<User, Guid>? _userRepository;

    public DatabaseSessionService(IRepository<UserSession, Guid> repository, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        // Optional — only needed by GetActiveUsersAsync, resolved lazily so
        // existing call paths and tests that don't register the user repository
        // still construct the service successfully.
        _userRepository = serviceProvider.GetService<IRepository<User, Guid>>();
    }

    /// <inheritdoc />
    public async Task<Guid> CreateSessionAsync(Guid userId, string? deviceInfo, string? ipAddress, string? userAgent)
    {
        var session = new UserSession
        {
            UserId = userId,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreationTime = DateTime.UtcNow,
            LastActivityTime = DateTime.UtcNow,
            IsRevoked = false
        };

        await _repository.InsertAsync(session);
        return session.Id;
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
    public async Task<Result<int>> CleanExpiredSessionsAsync(TimeSpan inactiveThreshold)
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

        LogInformation("Cleaned {Count} expired sessions (inactive since {CutoffTime})", expiredSessions.Count, cutoffTime);
        return Ok(expiredSessions.Count);
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

