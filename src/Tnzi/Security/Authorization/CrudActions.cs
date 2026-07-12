namespace Tnzi.Security.Authorization;

/// <summary>
/// Action set for <see cref="PermissionDefinitionContextExtensions.AddCrudPermissions"/> —
/// which per-operation permission codes to declare for an entity surface.
/// </summary>
/// <remarks>
/// Codes follow the <c>{prefix}.{action}</c> convention (<c>user.view</c>,
/// <c>user.create</c>, <c>user.update</c>, <c>user.delete</c>). Declare only
/// the actions the surface actually exposes: a read-only surface (logs,
/// diagnostics) is <see cref="View"/> alone; most managed entities are
/// <see cref="All"/>. Trigger-style dangerous operations (execute SQL, run
/// cleanup, broadcast, …) are NOT crud actions — declare them individually
/// as <c>{prefix}.execute</c>-style codes via <c>AddPermission</c>.
/// </remarks>
[Flags]
public enum CrudActions
{
    /// <summary>No actions — invalid as an argument; present for flags math.</summary>
    None = 0,

    /// <summary>Read access to the surface (list/detail/statistics/export).</summary>
    View = 1,

    /// <summary>Create new records.</summary>
    Create = 2,

    /// <summary>Modify records, including enable/disable and status transitions.</summary>
    Update = 4,

    /// <summary>Delete records (single/batch/revoke).</summary>
    Delete = 8,

    /// <summary>All four crud actions.</summary>
    All = View | Create | Update | Delete,

    /// <summary>The three mutating actions, without view.</summary>
    Write = Create | Update | Delete,
}
