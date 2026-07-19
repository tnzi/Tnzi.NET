namespace Tnzi.Finance.Payroll.Controllers.Admin;

/// <summary>
/// 薪资组件管理控制器
/// </summary>
[Route("admin/payroll/components")]
[DefaultController]
[ApiAuthorize(PermissionName = "payroll.config.view")]
public class DefaultPayrollComponentAdminController : ApiAdminControllerBase
{
    private readonly ISalaryComponentService _componentService;

    public DefaultPayrollComponentAdminController(ISalaryComponentService componentService)
    {
        _componentService = Check.NotNull(componentService);
    }

    protected ISalaryComponentService ComponentService => _componentService;

    /// <summary>
    /// 分页查询薪资组件
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<SalaryComponentDto>>> GetPaged([FromQuery] SalaryComponentQueryDto query)
    {
        var result = await _componentService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取薪资组件
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<SalaryComponentDto>> Get(Guid id)
    {
        var result = await _componentService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建薪资组件
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "payroll.config.create")]
    public virtual async Task<ApiResult<SalaryComponentDto>> Create([FromBody] CreateSalaryComponentDto request)
    {
        var result = await _componentService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新薪资组件
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.config.update")]
    public virtual async Task<ApiResult<SalaryComponentDto>> Update(Guid id, [FromBody] UpdateSalaryComponentDto request)
    {
        var result = await _componentService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除薪资组件
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.config.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _componentService.DeleteAsync(id);
        return result.ToApiResult();
    }
}
