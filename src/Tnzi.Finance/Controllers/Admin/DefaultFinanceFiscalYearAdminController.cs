namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 会计年度管理控制器
/// </summary>
[Route("admin/finance/fiscal-years")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.fiscalYear.view")]
public class DefaultFinanceFiscalYearAdminController : ApiAdminControllerBase
{
    private readonly IFiscalYearService _fiscalYearService;

    public DefaultFinanceFiscalYearAdminController(IFiscalYearService fiscalYearService)
    {
        _fiscalYearService = Check.NotNull(fiscalYearService);
    }

    protected IFiscalYearService FiscalYearService => _fiscalYearService;

    /// <summary>
    /// 获取全部会计年度
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<FiscalYearDto>>> GetList()
    {
        var result = await _fiscalYearService.GetListAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建会计年度
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.fiscalYear.create")]
    public virtual async Task<ApiResult<FiscalYearDto>> Create([FromBody] CreateFiscalYearDto request)
    {
        var result = await _fiscalYearService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 关闭会计年度（区间内禁止过账）
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [ApiAuthorize(PermissionName = "finance.fiscalYear.update")]
    public virtual async Task<ApiResult> Close(Guid id)
    {
        var result = await _fiscalYearService.CloseAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重新打开会计年度
    /// </summary>
    [HttpPost("{id:guid}/reopen")]
    [ApiAuthorize(PermissionName = "finance.fiscalYear.update")]
    public virtual async Task<ApiResult> Reopen(Guid id)
    {
        var result = await _fiscalYearService.ReopenAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除会计年度
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.fiscalYear.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _fiscalYearService.DeleteAsync(id);
        return result.ToApiResult();
    }
}
