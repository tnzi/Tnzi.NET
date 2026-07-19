namespace Tnzi.Finance.Payroll.Controllers.Admin;

/// <summary>
/// 薪资结构管理控制器
/// </summary>
[Route("admin/payroll/structures")]
[DefaultController]
[ApiAuthorize(PermissionName = "payroll.config.view")]
public class DefaultPayrollStructureAdminController : ApiAdminControllerBase
{
    private readonly ISalaryStructureService _structureService;

    public DefaultPayrollStructureAdminController(ISalaryStructureService structureService)
    {
        _structureService = Check.NotNull(structureService);
    }

    protected ISalaryStructureService StructureService => _structureService;

    /// <summary>
    /// 分页查询薪资结构
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<SalaryStructureListDto>>> GetPaged([FromQuery] SalaryStructureQueryDto query)
    {
        var result = await _structureService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取薪资结构（含行）
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<SalaryStructureDto>> Get(Guid id)
    {
        var result = await _structureService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建薪资结构
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "payroll.config.create")]
    public virtual async Task<ApiResult<SalaryStructureDto>> Create([FromBody] CreateSalaryStructureDto request)
    {
        var result = await _structureService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新薪资结构（行全量重建）
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.config.update")]
    public virtual async Task<ApiResult<SalaryStructureDto>> Update(Guid id, [FromBody] UpdateSalaryStructureDto request)
    {
        var result = await _structureService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除薪资结构
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.config.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _structureService.DeleteAsync(id);
        return result.ToApiResult();
    }
}
