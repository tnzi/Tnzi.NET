namespace Tnzi.Identity.Services;

/// <summary>
/// <see cref="ISessionRevocationService"/> 的默认实现。
/// </summary>
public class SessionRevocationService : ApplicationService, ISessionRevocationService
{
    private readonly ISessionService _sessionService;
    private readonly IAuthTokenService _authTokenService;
    private readonly IUserTenantScopeProvider _scope;
    private readonly IEventBus? _eventBus;

    /// <summary>初始化一个 <see cref="SessionRevocationService"/> 类型的新实例。</summary>
    /// <remarks>
    /// <paramref name="scope"/>（当前请求的用户范围）★ 必需而不是可选：会话服务按范围答 404，而这里的令牌删除
    /// <b>不以撤销成功为前提</b>（见 <see cref="RevokeSessionAsync"/>），少了范围判断，
    /// 租户 A 的管理员拿一个会话 id 就能删掉租户 B 用户的刷新令牌 —— 而「没判」在返回值里看不出来。
    /// </remarks>
    public SessionRevocationService(
        IServiceProvider serviceProvider,
        ISessionService sessionService,
        IAuthTokenService authTokenService,
        IUserTenantScopeProvider scope,
        IEventBus? eventBus = null)
        : base(serviceProvider)
    {
        _sessionService = Check.NotNull(sessionService);
        _authTokenService = Check.NotNull(authTokenService);
        _scope = Check.NotNull(scope);
        _eventBus = eventBus;
    }

    /// <inheritdoc />
    public async Task<int> RevokeSessionAsync(Guid sessionId, SessionRevocationReason reason)
    {
        if (sessionId == Guid.Empty)
        {
            return 0;
        }

        // 先取一次：事件里要填 UserId（撤销之后就取不到「这是谁的会话」了），范围判断也要用它。
        var session = await _sessionService.GetSessionAsync(sessionId);

        // ★ 范围被收窄（多租户下的管理端）时，主人不在范围内的会话一个字节都不碰 ——
        //   查不到的会话同样按不在范围内处理：拿一个猜来的 id 去删令牌就是跨租户删除。
        //   不收窄（登录链路、后台任务、单租户）时保持原样：查不到也照删，
        //   分布式会话过期后残留的令牌正靠这条路收走。
        if (!_scope.Current.IsUnrestricted
            && (session == null || !await _scope.ContainsAsync(session.UserId)))
        {
            LogWarning("Refused to revoke session {SessionId}: it is not within the caller's tenant scope.", sessionId);
            return 0;
        }

        var revokeResult = await _sessionService.RevokeSessionAsync(sessionId);

        // ★ 令牌删除**不以会话撤销成功为前提**。RevokeSessionAsync 在会话已撤销时返回失败，
        // 而那正是最需要把令牌一起清掉的情形之一：此前的撤销只改了会话行，令牌还留着。
        var tokenCount = await _authTokenService.RemoveSessionTokensAsync([sessionId]);

        var sessionCount = revokeResult.Succeeded ? 1 : 0;

        await PublishRevokedAsync(session?.UserId ?? Guid.Empty, sessionCount, tokenCount, reason, excludedSessionId: null);

        LogInformation(
            "Session revoked: {SessionId}, reason: {Reason}, tokens removed: {TokenCount}",
            sessionId, reason, tokenCount);

        return sessionCount;
    }

    /// <inheritdoc />
    public async Task<int> RevokeUserSessionsAsync(Guid userId, SessionRevocationReason reason, Guid? excludeSessionId = null)
    {
        if (userId == Guid.Empty)
        {
            return 0;
        }

        // 理由同 RevokeSessionAsync：会话服务会按范围拒绝，但令牌是按用户直接删的。
        // 不收窄时 ContainsAsync 恒为 true 且不查库。
        if (!await _scope.ContainsAsync(userId))
        {
            LogWarning("Refused to revoke the sessions of user {UserId}: not within the caller's tenant scope.", userId);
            return 0;
        }

        // 撤销前先数一次还活着的会话数：撤销之后它们全都是 IsRevoked，数不出「这次踢掉了几个」。
        var sessionCount = await CountActiveSessionsAsync(userId, excludeSessionId);

        await _sessionService.RevokeAllSessionsAsync(userId, excludeSessionId);

        // 按用户删而不是按刚才数到的那批会话删 —— 两步之间新建的会话，
        // 它的刷新令牌不在那批里，按 id 删会漏掉它。
        var tokenCount = await _authTokenService.RemoveUserSessionTokensAsync(userId, excludeSessionId);

        await PublishRevokedAsync(userId, sessionCount, tokenCount, reason, excludeSessionId);

        LogInformation(
            "All sessions revoked for user {UserId} (reason: {Reason}, excluding: {ExcludeSessionId}): {SessionCount} sessions, {TokenCount} tokens",
            userId, reason, excludeSessionId?.ToString() ?? "none", sessionCount, tokenCount);

        return sessionCount;
    }

    /// <inheritdoc />
    public async Task<int> RevokeInactiveSessionsAsync(TimeSpan inactiveThreshold)
    {
        var cleaned = await _sessionService.CleanInactiveSessionsAsync(inactiveThreshold);
        if (!cleaned.Succeeded || cleaned.Data is not { Count: > 0 } sessionIds)
        {
            return 0;
        }

        var tokenCount = await _authTokenService.RemoveSessionTokensAsync(sessionIds);

        // 事件按用户聚合不了（这一批跨多个用户），故只记日志；
        // 逐条发 SessionsRevokedEvent 会在一次清扫里制造成百上千条噪音事件。
        LogInformation(
            "Session maintenance revoked {SessionCount} inactive sessions and removed {TokenCount} bound refresh tokens.",
            sessionIds.Count, tokenCount);

        return sessionIds.Count;
    }

    private async Task<int> CountActiveSessionsAsync(Guid userId, Guid? excludeSessionId)
    {
        var sessions = await _sessionService.GetUserSessionsAsync(userId);
        if (!sessions.Succeeded || sessions.Data == null)
        {
            return 0;
        }

        return sessions.Data.Count(s => !s.IsRevoked && s.Id != excludeSessionId);
    }

    private async Task PublishRevokedAsync(
        Guid userId, int sessionCount, int tokenCount, SessionRevocationReason reason, Guid? excludedSessionId)
    {
        // 一次都没踢到、也没删到令牌，就没有发生任何事，不必制造一条噪声事件。
        if (_eventBus == null || (sessionCount == 0 && tokenCount == 0))
        {
            return;
        }

        await _eventBus.PublishAsync(new SessionsRevokedEvent
        {
            UserId = userId,
            SessionCount = sessionCount,
            TokenCount = tokenCount,
            Reason = reason,
            ExcludedSessionId = excludedSessionId,
            RevokedTime = DateTime.UtcNow
        }, cancellationToken: default);
    }
}
