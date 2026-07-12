namespace Tnzi.Authorization.Events.Handlers;

/// <summary>
/// 处理 Identity 模块发布的 <see cref="UserRolesChangedEvent"/>，
/// 使该用户的权限缓存立即失效。
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b>: <see cref="FunctionAuthCache"/> caches a user's
/// effective permission set (union of <c>RoleFunction</c> /
/// <c>UserFunction</c>) keyed by user-id, default 30-minute TTL. When
/// an admin revokes a role from a user, the TTL would otherwise let the
/// user retain the revoked role's permissions for up to a full window —
/// a real permission-retention security gap. This handler closes that
/// window by invalidating the user's cache entry the moment the role
/// change commits.
/// </para>
/// <para>
/// <b>Why let exceptions propagate</b>: the event bus provides error isolation,
/// retry, and dead-letter handling, so a transient cache-invalidation failure is
/// retried instead of being silently swallowed — closing the permission-retention
/// window faster and more reliably. The bus keeps the failure off the main
/// role-change request; the 30-minute TTL remains the ultimate backstop if all
/// retries are exhausted.
/// </para>
/// </remarks>
public class UserRolesChangedEventHandler : IEventHandler<UserRolesChangedEvent>
{
    private readonly FunctionAuthCache? _functionAuthCache;
    private readonly ILogger<UserRolesChangedEventHandler> _logger;

    public UserRolesChangedEventHandler(
        ILogger<UserRolesChangedEventHandler> logger,
        FunctionAuthCache? functionAuthCache = null)
    {
        _logger = Check.NotNull(logger);
        _functionAuthCache = functionAuthCache;
    }

    /// <inheritdoc />
    public async Task HandleAsync(UserRolesChangedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_functionAuthCache == null)
        {
            // No cache configured — nothing to do. This branch exists for
            // hosts that disabled the FunctionAuthCache registration.
            return;
        }

        // 不再吞异常：失效失败应冒泡给事件总线，由其错误隔离 + 重试 + DLQ 兜底
        await _functionAuthCache.RemoveUserPermissionNamesAsync(@event.UserId);
        _logger.LogDebug(
            "Invalidated permission cache for user {UserId} after role change ({ChangeType}; added={AddedCount}, removed={RemovedCount}).",
            @event.UserId,
            @event.ChangeType,
            @event.AddedRoleIds.Count,
            @event.RemovedRoleIds.Count);
    }
}
