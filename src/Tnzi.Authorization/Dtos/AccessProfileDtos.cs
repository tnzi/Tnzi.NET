namespace Tnzi.Authorization.Dtos;

/// <summary>
/// The current user's resolved access profile - the single self-service
/// payload the admin front-end needs after login: the effective permission
/// codes plus the backend-authoritative super-admin flag. Replaces the
/// convention-based front-end mirror of <c>Authorization:SuperAdminRoles</c>
/// (a client-side role-name list could silently drift from the backend
/// configuration; this flag cannot).
/// </summary>
public class AccessProfileDto
{
    /// <summary>
    /// Whether the user is a super administrator (bypasses every permission
    /// check; assignment UIs may skip grantable filtering).
    /// </summary>
    public bool IsSuperAdmin { get; set; }

    /// <summary>
    /// The user's effective permission codes. Full enabled catalogue for a
    /// super admin; explicit grants only for everyone else.
    /// </summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// Present only when the super-admin bypass is narrowed
    /// (<c>Authorization:SuperAdminBypassScope = Platform</c>). Then a super admin's
    /// <see cref="Permissions"/> is the platform catalogue plus their explicit grants,
    /// and clients must gate on that list instead of on <see cref="IsSuperAdmin"/>.
    /// Absent (not serialized) in the default mode, where the bypass covers everything.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Options.SuperAdminBypassScope? SuperAdminBypassScope { get; set; }
}
