namespace Tnzi.Identity.Services;

/// <summary>
/// 基于数据库的会话管理服务实现
/// 适用于简单项目，不需要分布式支持的场景
/// </summary>
public class DatabaseSessionService : ApplicationService, ISessionService
{
    private readonly IRepository<UserSession, Guid> _repository;

    public DatabaseSessionService(IRepository<UserSession, Guid> repository, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
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
}

