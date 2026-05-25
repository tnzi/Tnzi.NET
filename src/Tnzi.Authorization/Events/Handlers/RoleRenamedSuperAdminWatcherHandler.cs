namespace Tnzi.Authorization.Events.Handlers;

/// <summary>
/// 监听 <see cref="RoleUpdatedEvent"/>，当被重命名的角色出现在
/// <c>Authorization:SuperAdminRoles</c> 配置里时打 Warning 日志。
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b>: <see cref="Tnzi.Authorization.Options.AuthorizationOptions.SuperAdminRoles"/>
/// is configured by *role name* (string), not role-id, because role-ids
/// are runtime-generated Guids that can't be hardcoded into appsettings.
/// The trade-off: if an admin renames a role through the role-management
/// UI, the appsettings entry now points at a non-existent role, and the
/// affected admins silently lose super-admin bypass at next cache miss.
/// </para>
/// <para>
/// This handler doesn't *fix* the problem (we can't safely auto-rewrite
/// appsettings.json from a server process), but it surfaces the issue
/// in the application log at the moment the rename happens — far easier
/// than diagnosing "admin can't access X anymore" three days later.
/// </para>
/// <para>
/// Cache invalidation is *not* this handler's job — the broader
/// <c>RoleFunctionsChangedEvent</c> + <c>UserRolesChangedEvent</c> wiring
/// already covers cache lifetime. This handler is diagnostic only.
/// </para>
/// </remarks>
public class RoleRenamedSuperAdminWatcherHandler : IEventHandler<RoleUpdatedEvent>
{
    private readonly IOptions<Tnzi.Authorization.Options.AuthorizationOptions> _options;
    private readonly ILogger<RoleRenamedSuperAdminWatcherHandler> _logger;

    public RoleRenamedSuperAdminWatcherHandler(
        IOptions<Tnzi.Authorization.Options.AuthorizationOptions> options,
        ILogger<RoleRenamedSuperAdminWatcherHandler> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public Task HandleAsync(RoleUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        // No-op when the update wasn't a rename or no super-admin roles are configured.
        if (string.IsNullOrEmpty(@event.PreviousName)) return Task.CompletedTask;
        var superAdminRoles = _options.Value.SuperAdminRoles;
        if (superAdminRoles == null || superAdminRoles.Count == 0) return Task.CompletedTask;

        var configured = new HashSet<string>(superAdminRoles, StringComparer.OrdinalIgnoreCase);
        if (configured.Contains(@event.PreviousName))
        {
            // Match by previous name → admin renamed a role currently listed
            // in Authorization.SuperAdminRoles. They lose super-admin once
            // the cache TTL expires. Log loud so runbook can catch it.
            _logger.LogWarning(
                "Role '{PreviousName}' was renamed to '{NewName}' but Authorization.SuperAdminRoles still references the old name. " +
                "Members of this role will lose super-admin bypass after the cache TTL expires. " +
                "Either rename the config entry to '{NewName}' or rename the role back to '{PreviousName}'.",
                @event.PreviousName, @event.RoleName, @event.RoleName, @event.PreviousName);
        }
        else if (configured.Contains(@event.RoleName))
        {
            // Match by new name → admin renamed *into* a super-admin slot.
            // Probably intentional (adopting an existing role into the
            // bypass set), but log Info so the audit trail captures it.
            _logger.LogInformation(
                "Role '{PreviousName}' was renamed to '{NewName}'; the new name is listed in Authorization.SuperAdminRoles. " +
                "Members of this role will now be treated as super-admin after the cache TTL refreshes.",
                @event.PreviousName, @event.RoleName);
        }

        return Task.CompletedTask;
    }
}
