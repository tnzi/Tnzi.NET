namespace Tnzi.Identity.Services;

/// <summary>
/// Identity 租户检查适配器
/// </summary>
public class TenantChecker : ITenantChecker
{
    private readonly ITenantService _tenantService;

    public TenantChecker(ITenantService tenantService)
    {
        _tenantService = Check.NotNull(tenantService);
    }

    public async Task<bool> IsActiveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _tenantService.IsActiveAsync(tenantId);
    }
}
