
namespace Tnzi.AspNetCore.Security;

public class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = Check.NotNull(httpContextAccessor);
    }

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public Guid? Id => FindClaimValue(ClaimTypes.NameIdentifier) is string id && Guid.TryParse(id, out var guid) ? guid : null;

    public string? UserName => FindClaimValue(ClaimTypes.Name);

    public Guid? TenantId => FindClaimValue("tenant_id") is string id && Guid.TryParse(id, out var guid) ? guid : null;

    public string[] Roles => FindClaimValues(ClaimTypes.Role);

    public bool IsInRole(string roleName)
    {
        return FindClaimValues(ClaimTypes.Role).Contains(roleName, StringComparer.OrdinalIgnoreCase);
    }

    private string? FindClaimValue(string claimType)
    {
        return _httpContextAccessor.HttpContext?.User?.FindFirst(claimType)?.Value;
    }

    private string[] FindClaimValues(string claimType)
    {
        return _httpContextAccessor.HttpContext?.User?.FindAll(claimType).Select(c => c.Value).ToArray() ?? Array.Empty<string>();
    }
}