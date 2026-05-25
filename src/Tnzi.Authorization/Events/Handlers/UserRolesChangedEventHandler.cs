namespace Tnzi.Authorization.Events.Handlers;

/// <summary>
/// 处理 Identity 模块发布的 <see cref="UserRolesChangedEvent"/>，
/// 使该用户的权限缓存立即失效。
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b>: <see cref="FunctionAuthCache"/> caches a user's
/// effective permission set (union of <c>ModuleUser</c> / <c>ModuleRole</c>
/// / <c>RoleFunction</c>) keyed by user-id, default 30-minute TTL. When
/// an admin revokes a role from a user, the TTL would otherwise let the
/// user retain the revoked role's permissions for up to a full window —
/// a real permission-retention security gap. This handler closes that
/// window by invalidating the user's cache entry the moment the role
/// change commits.
/// </para>
/// <para>
/// <b>Why silent-catch</b>: framework rule for event handlers — auxiliary
/// flows must not break the main role-change request. If cache invalidation
/// fails (transient cache outage etc.), the TTL backstop still kicks in and
/// the worst case is a 30-minute permission lag, which is what we'd have
/// without this handler anyway.
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

        try
        {
            await _functionAuthCache.RemoveUserPermissionNamesAsync(@event.UserId);
            _logger.LogDebug(
                "Invalidated permission cache for user {UserId} after role change ({ChangeType}; added={AddedCount}, removed={RemovedCount}).",
                @event.UserId,
                @event.ChangeType,
                @event.AddedRoleIds.Count,
                @event.RemovedRoleIds.Count);
        }
        catch (Exception ex)
        {
            // Per framework convention, log + swallow so the role-change
            // transaction's outer request isn't impacted.
            _logger.LogWarning(ex,
                "Failed to invalidate permission cache for user {UserId} after role change.",
                @event.UserId);
        }
    }
}
