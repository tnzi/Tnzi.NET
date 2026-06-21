
namespace Tnzi.AspNetCore.Security;

public class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = Check.NotNull(httpContextAccessor);
    }

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public Guid? Id => FindClaim(ClaimTypes.NameIdentifier) is string id && Guid.TryParse(id, out var guid) ? guid : null;

    public string? UserName => FindClaim(ClaimTypes.Name);

    public Guid? TenantId => FindClaim("tenant_id") is string id && Guid.TryParse(id, out var guid) ? guid : null;

    public string[] Roles => FindClaims(ClaimTypes.Role);

    public bool IsInRole(string roleName)
    {
        return FindClaims(ClaimTypes.Role).Contains(roleName, StringComparer.OrdinalIgnoreCase);
    }

    public string? FindClaim(string claimType)
    {
        return _httpContextAccessor.HttpContext?.User?.FindFirst(claimType)?.Value;
    }

    public string[] FindClaims(string claimType)
    {
        return _httpContextAccessor.HttpContext?.User?.FindAll(claimType).Select(c => c.Value).ToArray() ?? Array.Empty<string>();
    }
}