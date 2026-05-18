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
    /// </summary>
    public List<string> SuperAdminRoles { get; set; } = [];
}
