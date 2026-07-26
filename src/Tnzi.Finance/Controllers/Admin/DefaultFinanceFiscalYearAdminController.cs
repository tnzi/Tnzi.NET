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
    private readonly ILedgerLockService _ledgerLockService;

    public DefaultFinanceFiscalYearAdminController(IFiscalYearService fiscalYearService, ILedgerLockService ledgerLockService)
    {
        _fiscalYearService = Check.NotNull(fiscalYearService);
        _ledgerLockService = Check.NotNull(ledgerLockService);
    }

    protected IFiscalYearService FiscalYearService => _fiscalYearService;
    protected ILedgerLockService LedgerLockService => _ledgerLockService;

    /// <summary>
    /// 读取封账状态（账已封到哪一天、是否设了口令）
    /// </summary>
    /// <remarks>
    /// 与会计年度同处一个控制器：两者都是"期间控制"，操作员在同一个屏幕上决定
    /// "这一期能不能再动"。但权限码各自独立（见 FinancePermissions）。
    /// </remarks>
    [HttpGet("closing-date")]
    [ApiAuthorize(PermissionName = "finance.ledgerLock.view")]
    public virtual async Task<ApiResult<LedgerLockDto>> GetClosingDate()
    {
        var result = await _ledgerLockService.GetAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 设定 / 推进 / 解除封账日（已设口令时须提供）
    /// </summary>
    [HttpPut("closing-date")]
    [ApiAuthorize(PermissionName = "finance.ledgerLock.update")]
    public virtual async Task<ApiResult<LedgerLockDto>> SetClosingDate([FromBody] SetLedgerLockDto request)
    {
        var result = await _ledgerLockService.SetAsync(request);
        return result.ToApiResult();
    }

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
