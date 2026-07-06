namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 财务报表服务（全部从总账行数据库级聚合，本位币口径）
/// </summary>
public interface IFinancialReportService
{
    /// <summary>试算平衡表（期初 + 期间借贷 + 期末）</summary>
    Task<Result<TrialBalanceReportDto>> GetTrialBalanceAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>资产负债表（本年利润为计算行，无需年末结转）</summary>
    Task<Result<BalanceSheetReportDto>> GetBalanceSheetAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>利润表（日期区间）</summary>
    Task<Result<ProfitAndLossReportDto>> GetProfitAndLossAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>总账明细（单科目，分页）</summary>
    Task<Result<GeneralLedgerReportDto>> GetGeneralLedgerAsync(Guid accountId, DateTime from, DateTime to, PagedQueryDto paging, CancellationToken cancellationToken = default);

    /// <summary>应收账龄（按客户分组，本位币估算口径 = 未清交易币 × 捕获汇率；未清额取当前核销状态而非 asOf 时点快照，历史 asOf 为近似值）</summary>
    Task<Result<AgingReportDto>> GetArAgingAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>应付账龄（按供应商分组）</summary>
    Task<Result<AgingReportDto>> GetApAgingAsync(DateTime asOf, CancellationToken cancellationToken = default);
}
