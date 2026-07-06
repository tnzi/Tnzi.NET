namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 财务报表控制器
/// </summary>
[Route("admin/finance/reports")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.report.view")]
public class DefaultFinanceReportAdminController : ApiAdminControllerBase
{
    private readonly IFinancialReportService _reportService;

    public DefaultFinanceReportAdminController(IFinancialReportService reportService)
    {
        _reportService = Check.NotNull(reportService);
    }

    protected IFinancialReportService ReportService => _reportService;

    /// <summary>
    /// 试算平衡表
    /// </summary>
    [HttpGet("trial-balance")]
    public virtual async Task<ApiResult<TrialBalanceReportDto>> GetTrialBalance([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var result = await _reportService.GetTrialBalanceAsync(from, to);
        return result.ToApiResult();
    }

    /// <summary>
    /// 资产负债表
    /// </summary>
    [HttpGet("balance-sheet")]
    public virtual async Task<ApiResult<BalanceSheetReportDto>> GetBalanceSheet([FromQuery] DateTime asOf)
    {
        var result = await _reportService.GetBalanceSheetAsync(asOf);
        return result.ToApiResult();
    }

    /// <summary>
    /// 利润表
    /// </summary>
    [HttpGet("profit-and-loss")]
    public virtual async Task<ApiResult<ProfitAndLossReportDto>> GetProfitAndLoss([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var result = await _reportService.GetProfitAndLossAsync(from, to);
        return result.ToApiResult();
    }

    /// <summary>
    /// 总账明细（单科目，分页）
    /// </summary>
    [HttpGet("general-ledger/{accountId:guid}")]
    public virtual async Task<ApiResult<GeneralLedgerReportDto>> GetGeneralLedger(
        Guid accountId, [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] PagedQueryDto paging)
    {
        var result = await _reportService.GetGeneralLedgerAsync(accountId, from, to, paging);
        return result.ToApiResult();
    }

    /// <summary>
    /// 应收账龄
    /// </summary>
    [HttpGet("ar-aging")]
    public virtual async Task<ApiResult<AgingReportDto>> GetArAging([FromQuery] DateTime asOf)
    {
        var result = await ReportService.GetArAgingAsync(asOf);
        return result.ToApiResult();
    }

    /// <summary>
    /// 应付账龄
    /// </summary>
    [HttpGet("ap-aging")]
    public virtual async Task<ApiResult<AgingReportDto>> GetApAging([FromQuery] DateTime asOf)
    {
        var result = await ReportService.GetApAgingAsync(asOf);
        return result.ToApiResult();
    }
}
