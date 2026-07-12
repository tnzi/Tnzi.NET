namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 供应商管理控制器
/// </summary>
[Route("admin/finance/vendors")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.vendor.view")]
public class DefaultFinanceVendorAdminController : ApiAdminControllerBase
{
    private readonly IVendorService _vendorService;

    public DefaultFinanceVendorAdminController(IVendorService vendorService)
    {
        _vendorService = Check.NotNull(vendorService);
    }

    protected IVendorService VendorService => _vendorService;

    /// <summary>
    /// 分页查询供应商
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<VendorDto>>> GetPaged([FromQuery] VendorQueryDto query)
    {
        var result = await _vendorService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取供应商
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<VendorDto>> Get(Guid id)
    {
        var result = await _vendorService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建供应商
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.vendor.create")]
    public virtual async Task<ApiResult<VendorDto>> Create([FromBody] CreateVendorDto request)
    {
        var result = await _vendorService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新供应商
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.vendor.update")]
    public virtual async Task<ApiResult<VendorDto>> Update(Guid id, [FromBody] UpdateVendorDto request)
    {
        var result = await _vendorService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除供应商
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.vendor.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _vendorService.DeleteAsync(id);
        return result.ToApiResult();
    }
}
