namespace Tnzi.Identity.Services;

/// <summary>
/// 租户管理服务接口
/// </summary>
public interface ITenantService
{
    Task<Result<TenantDto>> GetByIdAsync(Guid id);
    Task<Result<TenantDto>> GetByCodeAsync(string code);
    Task<Result<IPagedList<TenantDto>>> GetListAsync(TenantQueryDto query);
    Task<Result<TenantDto>> CreateAsync(CreateTenantDto input);
    Task<Result<TenantDto>> UpdateAsync(Guid id, UpdateTenantDto input);
    Task<Result> SetEnabledAsync(Guid id, bool enabled);
    Task<Result> DeleteAsync(Guid id);
    Task<bool> IsActiveAsync(Guid tenantId);
}
