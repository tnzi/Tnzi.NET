namespace Tnzi.Finance.Payroll.Controllers.Admin;

/// <summary>
/// 税级表管理控制器
/// </summary>
[Route("admin/payroll/brackets")]
[DefaultController]
[ApiAuthorize(PermissionName = "payroll.config.view")]
public class DefaultPayrollBracketAdminController : ApiAdminControllerBase
{
    private readonly IBracketTableService _bracketTableService;

    public DefaultPayrollBracketAdminController(IBracketTableService bracketTableService)
    {
        _bracketTableService = Check.NotNull(bracketTableService);
    }

    protected IBracketTableService BracketTableService => _bracketTableService;

    /// <summary>
    /// 分页查询税级表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<BracketTableListDto>>> GetPaged([FromQuery] BracketTableQueryDto query)
    {
        var result = await _bracketTableService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 解析某日期生效的表版本（含行）
    /// </summary>
    [HttpGet("resolve")]
    public virtual async Task<ApiResult<BracketTableDto>> Resolve([FromQuery] string code, [FromQuery] DateTime asOf)
    {
        var result = await _bracketTableService.ResolveAsync(code, asOf);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取税级表（含行）
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<BracketTableDto>> Get(Guid id)
    {
        var result = await _bracketTableService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建税级表
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "payroll.config.create")]
    public virtual async Task<ApiResult<BracketTableDto>> Create([FromBody] CreateBracketTableDto request)
    {
        var result = await _bracketTableService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新税级表（行全量重建）
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.config.update")]
    public virtual async Task<ApiResult<BracketTableDto>> Update(Guid id, [FromBody] UpdateBracketTableDto request)
    {
        var result = await _bracketTableService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除税级表
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.config.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _bracketTableService.DeleteAsync(id);
        return result.ToApiResult();
    }
}
