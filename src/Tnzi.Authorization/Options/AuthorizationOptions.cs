namespace Tnzi.Authorization.Options;

/// <summary>
/// Authorization module options.
/// </summary>
public class AuthorizationOptions
{
    /// <summary>
    /// Role names whose members are treated as super administrators —
    /// every <see cref="Tnzi.Security.Authorization.IFunctionAuthorizationService"/>
    /// check short-circuits to "granted" and
    /// <c>GetUserPermissionNamesAsync</c> returns the full enabled-function catalogue.
    /// Comparison is case-insensitive. Empty list (default) keeps the legacy
    /// "all permissions require an explicit assignment" behaviour.
    /// Convention: seed a role named <c>SuperAdmin</c> and list it here.
    /// </summary>
    public List<string> SuperAdminRoles { get; set; } = [];

    /// <summary>
    /// Per-code category overrides applied at permission seed time — lets a
    /// deployment reclassify a framework default without code changes, e.g.
    /// <c>{ "audit.log.view": "Technical" }</c>. The category is informational
    /// metadata (assignment UIs render a "Technical" badge on ops/dangerous
    /// surfaces); it does not drive any implicit grant. Keys are permission
    /// codes (case-insensitive), values win over
    /// <c>IPermissionDefinitionProvider</c> declarations.
    /// </summary>
    public Dictionary<string, PermissionCategory> PermissionCategoryOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When <c>true</c>, startup creates an Identity role (marked
    /// <c>IsSystem</c>) for every name listed in <see cref="SuperAdminRoles"/>
    /// that does not exist yet, so the super-admin convention works out of
    /// the box without an application-side role seeder. Default <c>false</c>:
    /// role provisioning stays the consuming application's responsibility.
    /// Existing roles are never modified. Assigning users to the seeded
    /// roles remains an application / operator task.
    /// </summary>
    public bool SeedBuiltInAdminRoles { get; set; }
}
