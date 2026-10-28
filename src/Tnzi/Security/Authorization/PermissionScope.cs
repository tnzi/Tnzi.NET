namespace Tnzi.Security.Authorization;

/// <summary>
/// Who a permission group belongs to: the operator who runs the platform, or
/// the organisation that uses the application. Drives the narrowed super-admin
/// bypass (<c>Authorization:SuperAdminBypassScope = Platform</c>): super admins
/// pass <see cref="Platform"/> codes implicitly and need explicit grants for
/// <see cref="Application"/> codes, like every other user. With the default
/// bypass scope (<c>All</c>) it has no effect on access.
/// </summary>
/// <remarks>
/// Declared per group (<see cref="PermissionGroupDefinition.Scope"/>). A group
/// that does not declare one is <see cref="Platform"/> when the provider that
/// created it lives in a framework (<c>Tnzi.*</c>) assembly and
/// <see cref="Application"/> otherwise; deployments can override per group
/// through <c>Authorization:PermissionScopeOverrides</c>.
/// </remarks>
public enum PermissionScope
{
    /// <summary>Platform administration: users, roles, settings, audit, operations, billing back office.</summary>
    Platform = 0,

    /// <summary>The customer organisation's own business data and workflows.</summary>
    Application = 1,
}
