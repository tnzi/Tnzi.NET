namespace Tnzi.Authorization.Options;

/// <summary>
/// Authorization module options.
/// </summary>
public class AuthorizationOptions
{
    /// <summary>
    /// The conventional super-admin role name applied when no
    /// <see cref="SuperAdminRoles"/> configuration is provided.
    /// </summary>
    public const string DefaultSuperAdminRoleName = "SuperAdmin";

    /// <summary>
    /// Role names whose members are treated as super administrators -
    /// every <see cref="Tnzi.Security.Authorization.IFunctionAuthorizationService"/>
    /// check short-circuits to "granted" and
    /// <c>GetUserPermissionNamesAsync</c> returns the full enabled-function
    /// catalogue. Comparison is case-insensitive.
    /// Out-of-the-box convention: when nothing is configured here (a JSON
    /// empty array produces zero configuration keys and is indistinguishable
    /// from an absent key), <see cref="ApplyConventionDefaults"/> fills in
    /// <see cref="DefaultSuperAdminRoleName"/> at post-configure time. Set
    /// <see cref="DisableSuperAdminBypass"/> to opt into the legacy
    /// "all permissions require an explicit assignment" mode instead.
    /// NOTE: the convention default is intentionally NOT a property
    /// initializer - the configuration binder APPENDS to a pre-populated
    /// list, so a class-level default would duplicate configured entries.
    /// </summary>
    public List<string> SuperAdminRoles { get; set; } = [];

    /// <summary>
    /// Disables the super-admin bypass entirely: the convention default is
    /// not applied and every permission check consults the explicit
    /// RoleFunction/UserFunction assignments (legacy pre-convention mode).
    /// Contradictory combinations (this flag plus a configured
    /// <see cref="SuperAdminRoles"/> or <see cref="BootstrapSuperAdminUsers"/>)
    /// fail startup validation instead of silently picking a winner.
    /// </summary>
    public bool DisableSuperAdminBypass { get; set; }

    /// <summary>
    /// Per-code category overrides applied at permission seed time - lets a
    /// deployment reclassify a framework default without code changes, e.g.
    /// <c>{ "audit.log.view": "Technical" }</c>. The category is informational
    /// metadata (assignment UIs render a "Technical" badge on ops/dangerous
    /// surfaces); it does not drive any implicit grant. Keys are permission
    /// codes (case-insensitive), values win over
    /// <c>IPermissionDefinitionProvider</c> declarations.
    /// </summary>
    public Dictionary<string, PermissionCategory> PermissionCategoryOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When <c>true</c> (default), startup creates an Identity role (marked
    /// <c>IsSystem</c>) for every name listed in <see cref="SuperAdminRoles"/>
    /// that does not exist yet, so the super-admin convention works out of
    /// the box without an application-side role seeder. Existing roles are
    /// never modified. Set to <c>false</c> when role provisioning should stay
    /// the consuming application's responsibility.
    /// </summary>
    public bool SeedBuiltInAdminRoles { get; set; } = true;

    /// <summary>
    /// User names to assign to the first existing super-admin role at startup
    /// - the "first super admin" bootstrap. Assignment only happens while ALL
    /// configured super-admin roles have ZERO members: a zero-member
    /// super-admin role means the system has no recovery account at all
    /// (usually a fresh install, or an accidental lockout this bootstrap is
    /// the designated escape from), so re-applying on restart is a feature
    /// here, not an operational-clear resurrection. Once any member exists
    /// the list is ignored. Listed users must already exist (created via
    /// registration or an application seeder) - this never creates users and
    /// never touches passwords; missing names are logged as warnings.
    /// </summary>
    public List<string> BootstrapSuperAdminUsers { get; set; } = [];

    /// <summary>
    /// Applies the out-of-the-box convention defaults after configuration
    /// binding: an unconfigured <see cref="SuperAdminRoles"/> falls back to
    /// <see cref="DefaultSuperAdminRoleName"/> unless
    /// <see cref="DisableSuperAdminBypass"/> is set. Public and static so
    /// tests can exercise the exact rule the module registers via
    /// <c>PostConfigure</c>.
    /// </summary>
    public static void ApplyConventionDefaults(AuthorizationOptions options)
    {
        Check.NotNull(options);
        if (!options.DisableSuperAdminBypass && options.SuperAdminRoles.Count == 0)
        {
            options.SuperAdminRoles.Add(DefaultSuperAdminRoleName);
        }
    }
}
