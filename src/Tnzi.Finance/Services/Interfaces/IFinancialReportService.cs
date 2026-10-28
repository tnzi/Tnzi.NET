namespace Tnzi.Finance.Services;

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

    /// <summary>
    /// 总账明细（单科目，分页 + 可选筛选）。筛选条件全部下推数据库，分页与总数都是筛选后的口径
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="filter"/> 为 null 或全空时行为与不带筛选的重载<b>完全一致</b>（含余额三件套）。
    /// 任一条件生效时 <see cref="GeneralLedgerReportDto.IsFiltered"/> 为 true，余额字段一律置 0 且不适用
    /// ——契约见该属性说明。
    /// </para>
    /// <para>
    /// 本重载晚于接口首版加入，故为默认接口方法：既有实现者不重写也不会编译失败。但默认实现<b>不会</b>
    /// 静默丢弃筛选条件（那会把用户已经筛掉的行照样返回），带条件调用时直接返回 501。
    /// </para>
    /// <para>
    /// 重载而非可选参数：<c>GetGeneralLedgerAsync(id, from, to, paging, cancellationToken)</c> 这样把
    /// <see cref="CancellationToken"/> 放在第 5 位的既有调用点必须继续解析到上面那个重载，故本方法的
    /// <paramref name="filter"/> 刻意不给默认值。
    /// </para>
    /// </remarks>
    /// <param name="accountId">科目 ID</param>
    /// <param name="from">起始日期（含）</param>
    /// <param name="to">截止日期（含）</param>
    /// <param name="paging">分页参数</param>
    /// <param name="filter">筛选条件（null = 不筛选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result<GeneralLedgerReportDto>> GetGeneralLedgerAsync(
        Guid accountId, DateTime from, DateTime to, PagedQueryDto paging, GeneralLedgerFilterDto? filter,
        CancellationToken cancellationToken = default)
        => filter == null || (string.IsNullOrWhiteSpace(filter.Keyword) && string.IsNullOrWhiteSpace(filter.SourceType))
            ? GetGeneralLedgerAsync(accountId, from, to, paging, cancellationToken)
            : Task.FromResult(Result<GeneralLedgerReportDto>.Failure(
                "This IFinancialReportService implementation does not support general ledger filtering.", 501));

    /// <summary>应收账龄（按客户分组，本位币估算口径 = 未清交易币 × 捕获汇率；已核销额按 asOf 时点重建，未核销收款与贷项作负行，合计与 AR 控制科目 tie-out）</summary>
    Task<Result<AgingReportDto>> GetArAgingAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>应付账龄（按供应商分组）</summary>
    Task<Result<AgingReportDto>> GetApAgingAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>
    /// 单个往来方的账龄：<c>Rows</c> 至多一行（就是全量账龄报表里他那一行，逐字段相同），<c>Totals</c> 等于这一行。
    /// </summary>
    /// <remarks>
    /// 往来方工作面与对账单只要一行，不该为它算整本账的账龄。本方法晚于接口首版加入，故为默认接口方法：
    /// 默认实现取全量账龄再挑出这一行（结果相同、开销不变），既有实现者不重写也不会编译失败；
    /// 框架实现把往来方谓词下推到每一条查询。
    /// </remarks>
    /// <param name="partyType">客户（应收）或供应商（应付）</param>
    /// <param name="partyId">往来方 ID</param>
    /// <param name="asOf">时点</param>
    /// <param name="cancellationToken">取消令牌</param>
    async Task<Result<AgingReportDto>> GetPartyAgingAsync(
        FinancePartyType partyType, Guid partyId, DateTime asOf, CancellationToken cancellationToken = default)
    {
        var full = partyType == FinancePartyType.Customer
            ? await GetArAgingAsync(asOf, cancellationToken)
            : await GetApAgingAsync(asOf, cancellationToken);
        if (!full.Succeeded)
            return full;

        var report = full.Data!;
        var row = report.Rows.FirstOrDefault(r => r.PartyId == partyId);
        return Result<AgingReportDto>.Success(new AgingReportDto
        {
            AsOf = report.AsOf,
            BaseCurrency = report.BaseCurrency,
            Rows = row == null ? [] : [row],
            Totals = row == null
                ? new AgingBucketsDto { AgingBucketDays = report.Totals.AgingBucketDays }
                : new AgingBucketsDto
                {
                    Current = row.Current,
                    Days1To30 = row.Days1To30,
                    Days31To60 = row.Days31To60,
                    Days61To90 = row.Days61To90,
                    Over90 = row.Over90,
                    Total = row.Total,
                    AgingBucketDays = row.AgingBucketDays
                }
        });
    }

    /// <summary>
    /// 税务申报汇总（期间 × 税务机构/税率的销项税、进项税与净额；纯 GL 聚合，
    /// 只统计携带 TaxRateId 的已过账行——税维度自引入迁移起写入，历史行不计入）
    /// </summary>
    Task<Result<TaxSummaryReportDto>> GetTaxSummaryAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>
    /// 现金流量表（间接法：净利润 + 按科目 CashFlowActivity 分类的资产负债类科目期间变动；
    /// 未分类科目落显式 Unclassified 桶；恒等式校验行 CheckDifference 应为 0）
    /// </summary>
    Task<Result<CashFlowReportDto>> GetCashFlowAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>试算平衡表 CSV 导出（invariant culture；字符串单元格做 CSV 公式注入转义）</summary>
    Task<Result<string>> ExportTrialBalanceCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>资产负债表 CSV 导出</summary>
    Task<Result<string>> ExportBalanceSheetCsvAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>利润表 CSV 导出</summary>
    Task<Result<string>> ExportProfitAndLossCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>
    /// 总账明细 CSV 导出（期间全量、含运行余额；超过 FinanceOptions.ReportExportMaxRows 时拒绝并提示缩小期间）
    /// </summary>
    Task<Result<string>> ExportGeneralLedgerCsvAsync(Guid accountId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>应收账龄 CSV 导出</summary>
    Task<Result<string>> ExportArAgingCsvAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>应付账龄 CSV 导出</summary>
    Task<Result<string>> ExportApAgingCsvAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>税务申报汇总 CSV 导出</summary>
    Task<Result<string>> ExportTaxSummaryCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>现金流量表 CSV 导出</summary>
    Task<Result<string>> ExportCashFlowCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
