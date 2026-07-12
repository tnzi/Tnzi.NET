namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 租户管理控制器基类
/// </summary>
[Route("admin/tenants")]
[DefaultController]
[ApiAuthorize(PermissionName = "tenant.view")]
public class DefaultTenantAdminController : ApiAdminControllerBase
{
    protected readonly ITenantService TenantService;

    public DefaultTenantAdminController(ITenantService tenantService)
    {
        TenantService = Check.NotNull(tenantService);
    }

    [HttpGet("paged")]
    public virtual async Task<ApiResult<IPagedList<TenantDto>>> GetPagedList([FromQuery] TenantQueryDto query)
    {
        var result = await TenantService.GetListAsync(query);
        return result.ToApiResult();
    }

    [HttpGet("{id}")]
    public virtual async Task<ApiResult<TenantDto>> GetById(Guid id)
    {
        var result = await TenantService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    [HttpGet("by-code/{code}")]
    public virtual async Task<ApiResult<TenantDto>> GetByCode(string code)
    {
        var result = await TenantService.GetByCodeAsync(code);
        return result.ToApiResult();
    }

    [HttpPost]
    [ApiAuthorize(PermissionName = "tenant.create")]
    public virtual async Task<ApiResult<TenantDto>> Create([FromBody] CreateTenantDto input)
    {
        var result = await TenantService.CreateAsync(input);
        return result.ToApiResult();
    }

    [HttpPut("{id}")]
    [ApiAuthorize(PermissionName = "tenant.update")]
    public virtual async Task<ApiResult<TenantDto>> Update(Guid id, [FromBody] UpdateTenantDto input)
    {
        var result = await TenantService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    [HttpPut("{id}/enabled")]
    [ApiAuthorize(PermissionName = "tenant.update")]
    public virtual async Task<ApiResult> SetEnabled(Guid id, [FromQuery] bool enabled)
    {
        var result = await TenantService.SetEnabledAsync(id, enabled);
        return result.ToApiResult();
    }

    [HttpDelete("{id}")]
    [ApiAuthorize(PermissionName = "tenant.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await TenantService.DeleteAsync(id);
        return result.ToApiResult();
    }
}
