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
    /// 总账明细（单科目，分页；可选按关键字与来源类型筛选）
    /// </summary>
    /// <remarks>
    /// 筛选条件下推数据库，分页与总数都是筛选后的口径。筛选生效时响应的
    /// <c>isFiltered</c> 为 true 且余额三件套（期初/期末/运行余额）不适用并置 0，
    /// 呈现端应据此隐藏余额列
    /// </remarks>
    [HttpGet("general-ledger/{accountId:guid}")]
    public virtual async Task<ApiResult<GeneralLedgerReportDto>> GetGeneralLedger(
        Guid accountId, [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] PagedQueryDto paging,
        [FromQuery] GeneralLedgerFilterDto? filter = null)
    {
        var result = await _reportService.GetGeneralLedgerAsync(accountId, from, to, paging, filter);
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

    /// <summary>
    /// 税务申报汇总（期间 × 税务机构/税率的销项税、进项税与净额）
    /// </summary>
    [HttpGet("tax-summary")]
    public virtual async Task<ApiResult<TaxSummaryReportDto>> GetTaxSummary([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var result = await _reportService.GetTaxSummaryAsync(from, to);
        return result.ToApiResult();
    }

    /// <summary>
    /// 现金流量表（间接法，含恒等式校验行）
    /// </summary>
    [HttpGet("cash-flow")]
    public virtual async Task<ApiResult<CashFlowReportDto>> GetCashFlow([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var result = await _reportService.GetCashFlowAsync(from, to);
        return result.ToApiResult();
    }

    /// <summary>
    /// 现金流量表 CSV 导出
    /// </summary>
    [HttpGet("cash-flow/export")]
    public virtual async Task<IActionResult> ExportCashFlow([FromQuery] DateTime from, [FromQuery] DateTime to)
        => CsvFile(await _reportService.ExportCashFlowCsvAsync(from, to), "cash_flow");

    /// <summary>
    /// 试算平衡表 CSV 导出
    /// </summary>
    [HttpGet("trial-balance/export")]
    public virtual async Task<IActionResult> ExportTrialBalance([FromQuery] DateTime from, [FromQuery] DateTime to)
        => CsvFile(await _reportService.ExportTrialBalanceCsvAsync(from, to), "trial_balance");

    /// <summary>
    /// 资产负债表 CSV 导出
    /// </summary>
    [HttpGet("balance-sheet/export")]
    public virtual async Task<IActionResult> ExportBalanceSheet([FromQuery] DateTime asOf)
        => CsvFile(await _reportService.ExportBalanceSheetCsvAsync(asOf), "balance_sheet");

    /// <summary>
    /// 利润表 CSV 导出
    /// </summary>
    [HttpGet("profit-and-loss/export")]
    public virtual async Task<IActionResult> ExportProfitAndLoss([FromQuery] DateTime from, [FromQuery] DateTime to)
        => CsvFile(await _reportService.ExportProfitAndLossCsvAsync(from, to), "profit_and_loss");

    /// <summary>
    /// 总账明细 CSV 导出（期间全量、含运行余额；超过配置上限时返回 400 提示缩小期间）
    /// </summary>
    [HttpGet("general-ledger/{accountId:guid}/export")]
    public virtual async Task<IActionResult> ExportGeneralLedger(Guid accountId, [FromQuery] DateTime from, [FromQuery] DateTime to)
        => CsvFile(await _reportService.ExportGeneralLedgerCsvAsync(accountId, from, to), "general_ledger");

    /// <summary>
    /// 应收账龄 CSV 导出
    /// </summary>
    [HttpGet("ar-aging/export")]
    public virtual async Task<IActionResult> ExportArAging([FromQuery] DateTime asOf)
        => CsvFile(await _reportService.ExportArAgingCsvAsync(asOf), "ar_aging");

    /// <summary>
    /// 应付账龄 CSV 导出
    /// </summary>
    [HttpGet("ap-aging/export")]
    public virtual async Task<IActionResult> ExportApAging([FromQuery] DateTime asOf)
        => CsvFile(await _reportService.ExportApAgingCsvAsync(asOf), "ap_aging");

    /// <summary>
    /// 税务申报汇总 CSV 导出
    /// </summary>
    [HttpGet("tax-summary/export")]
    public virtual async Task<IActionResult> ExportTaxSummary([FromQuery] DateTime from, [FromQuery] DateTime to)
        => CsvFile(await _reportService.ExportTaxSummaryCsvAsync(from, to), "tax_summary");

}
