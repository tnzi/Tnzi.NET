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
}
