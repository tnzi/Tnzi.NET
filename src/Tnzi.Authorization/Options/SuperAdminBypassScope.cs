namespace Tnzi.Authorization.Options;

/// <summary>
/// Which permission codes the super-admin bypass covers
/// (<c>Authorization:SuperAdminBypassScope</c>).
/// </summary>
public enum SuperAdminBypassScope
{
    /// <summary>
    /// Every code, declared or not (default). Super admins pass every check and
    /// see the whole enabled catalogue.
    /// </summary>
    All = 0,

    /// <summary>
    /// Only codes in <see cref="Tnzi.Security.Authorization.PermissionScope.Platform"/>
    /// groups. Application codes (and codes no provider declares) resolve through
    /// explicit role / user grants for super admins too, user-level deny rows
    /// included. For products where the super admin is the vendor's platform
    /// operator and must not see the customer's business data by default.
    /// </summary>
    Platform = 1,
}
