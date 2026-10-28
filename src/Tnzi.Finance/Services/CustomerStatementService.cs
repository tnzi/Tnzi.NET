namespace Tnzi.Finance.Services;

/// <summary>
/// 客户/供应商对账单
/// </summary>
/// <remarks>
/// 余额、账龄、流水三者都**复用既有服务**（账龄报表 + 往来方流水），不另写一遍：
/// 寄出去的那张纸与自己账上的数对不上，比不寄更糟。
/// <para>
/// ★ 流水的口径必须与账龄（期初、期末余额的来源）逐项一致，否则同一张纸上末行余额与「应付金额」
/// 对不上：作废的单据账龄视为从未存在，流水也不列；费用直接付清、从不挂应付，供应商对账单也不列；
/// 账龄是本位币，所以对账单只出本位币单据，含外币单据时拒绝出单，而不是把外币金额加进本位币余额。
/// </para>
/// </remarks>
public class CustomerStatementService : ApplicationService, ICustomerStatementService
{
    private readonly IPartyLedgerService _ledger;
    private readonly IFinancialReportService _reports;
    private readonly IDunningPolicy _dunning;
    private readonly FinanceDocumentHelper _helper;
    private readonly IReadOnlyRepository<PaymentApplication, Guid> _applicationRepository;
    private readonly FinanceOptions _options;

    public CustomerStatementService(
        IServiceProvider serviceProvider,
        IPartyLedgerService ledger,
        IFinancialReportService reports,
        IDunningPolicy dunning,
        FinanceDocumentHelper helper,
        IReadOnlyRepository<PaymentApplication, Guid> applicationRepository,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _ledger = Check.NotNull(ledger);
        _reports = Check.NotNull(reports);
        _dunning = Check.NotNull(dunning);
        _helper = Check.NotNull(helper);
        _applicationRepository = Check.NotNull(applicationRepository);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<CustomerStatementDto>> GetAsync(
        FinancePartyType partyType, Guid partyId, CustomerStatementQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var to = (query.To ?? TimeProvider.GetUtcNow().UtcDateTime.Date).ToUtcDate();
        // Activity 形态没给起点时按一个月：月结是这种对账单最常见的用途。
        var from = (query.From ?? to.AddMonths(-1)).ToUtcDate();
        if (from > to)
            return Fail<CustomerStatementDto>("The statement period starts after it ends.", 400);

        var summary = await _ledger.GetSummaryAsync(partyType, partyId, to, cancellationToken: cancellationToken);
        if (!summary.Succeeded)
            return Fail<CustomerStatementDto>(summary.Message!, summary.Code ?? 400);
        var head = summary.Data!;

        var statement = new CustomerStatementDto
        {
            PartyId = partyId,
            PartyName = head.PartyName,
            PartyType = partyType,
            Style = query.Style,
            Currency = head.BaseCurrency,
            PeriodFrom = query.Style == StatementStyle.Activity ? from : to,
            PeriodTo = to,
            ClosingBalance = head.OpenBalance,
            Overdue = head.Overdue,
            Buckets = head.Buckets,
        };

        var linesResult = query.Style == StatementStyle.OpenItem
            ? await BuildOpenItemLinesAsync(partyType, partyId, to, cancellationToken)
            : await BuildActivityLinesAsync(partyType, partyId, from, to, cancellationToken);
        if (!linesResult.Succeeded)
            return Fail<CustomerStatementDto>(linesResult.Message!, linesResult.Code ?? 400);

        statement.Lines = linesResult.Data!.Lines;
        statement.OpeningBalance = linesResult.Data.OpeningBalance;

        var oldest = statement.Lines.Count == 0 ? 0 : statement.Lines.Max(l => l.OverdueDays);
        statement.DunningLevel = _dunning.Evaluate(oldest, head.Overdue);

        return Ok(statement);
    }

    public async Task<Result<List<DunningCandidateDto>>> GetDunningCandidatesAsync(
        FinancePartyType partyType, DateTime? asOf = null, CancellationToken cancellationToken = default)
    {
        var date = (asOf ?? TimeProvider.GetUtcNow().UtcDateTime.Date).ToUtcDate();

        // 账龄报表已经按往来方汇总过一遍，直接用——催收工作台与账龄报表给出
        // 两套"谁欠多少"是最伤信任的失败模式。
        var aging = partyType == FinancePartyType.Customer
            ? await _reports.GetArAgingAsync(date, cancellationToken)
            : await _reports.GetApAgingAsync(date, cancellationToken);
        if (!aging.Succeeded)
            return Fail<List<DunningCandidateDto>>(aging.Message!, aging.Code ?? 400);

        var candidates = new List<DunningCandidateDto>();
        foreach (var row in aging.Data!.Rows)
        {
            var overdue = row.Total - row.Current;
            if (overdue <= 0)
                continue;

            // 最久的桶决定天数下界：末桶里至少 cuts[2] 天，第三桶里至少 cuts[1]+1 天。
            // 刻意取**桶的下界**而不是精确天数——精确天数要再扫一遍单据，而催收
            // 分级只需要知道"落在哪一档"。
            // 下界必须由生效的切分点算出：桶已参数化（AgingBucketDays），写死 90/61/31
            // 会让配了 7/14/21 的部署把"逾期 15 天"报成"逾期 61 天"，随即被催收阈值
            // 判成最后通知。
            var cuts = _options.ResolveAgingBucketDays();
            var oldestDays = row.Over90 > 0 ? cuts[2]
                : row.Days61To90 > 0 ? cuts[1] + 1
                : row.Days31To60 > 0 ? cuts[0] + 1
                : 1;

            candidates.Add(new DunningCandidateDto
            {
                PartyId = row.PartyId,
                PartyName = row.PartyName,
                OpenBalance = row.Total,
                Overdue = overdue,
                OldestOverdueDays = oldestDays,
                Level = _dunning.Evaluate(oldestDays, overdue),
                Buckets = new AgingBucketsDto
                {
                    Current = row.Current,
                    Days1To30 = row.Days1To30,
                    Days31To60 = row.Days31To60,
                    Days61To90 = row.Days61To90,
                    Over90 = row.Over90,
                    Total = row.Total,
                    AgingBucketDays = row.AgingBucketDays,
                },
            });
        }

        // 最该催的排最前：先按强度，再按逾期金额。
        return Ok(candidates
            .OrderByDescending(c => c.Level)
            .ThenByDescending(c => c.Overdue)
            .ToList());
    }

    private sealed record StatementBody(List<StatementLineDto> Lines, decimal OpeningBalance);

    /// <summary>
    /// Open Item：只列截至 <paramref name="asOf"/> 还没付清的单据（发票 / 账单）。
    /// </summary>
    /// <remarks>
    /// ★ 未清额按 <paramref name="asOf"/> 时点重建（核销记账时刻早于次日零点的才算），与账龄同一判据：
    /// 用单据上当前的已核销额，一张过去日期的对账单会漏掉当时还欠着、之后才付清的单据，
    /// 而同一张纸上的期末余额（账龄）照样把它算在里面。逾期天数同理按 <paramref name="asOf"/> 算。
    /// </remarks>
    private async Task<Result<StatementBody>> BuildOpenItemLinesAsync(
        FinancePartyType partyType, Guid partyId, DateTime asOf, CancellationToken cancellationToken)
    {
        var all = await _ledger.GetAllTransactionsAsync(partyType, partyId, to: asOf, cancellationToken: cancellationToken);
        if (!all.Succeeded)
            return Fail<StatementBody>(all.Message!, all.Code ?? 400);

        var (targetDocType, settlementType) = partyType == FinancePartyType.Customer
            ? (FinanceSourceTypes.Invoice, SettlementDocType.Invoice)
            : (FinanceSourceTypes.Bill, SettlementDocType.Bill);
        var targets = all.Data!
            .Where(e => e.DocType == targetDocType && e.Status != FinanceDocumentStatus.Voided)
            .ToList();

        var targetIds = targets.Select(e => e.DocId).ToList();
        var appliedCutoff = asOf.AddDays(1);
        var appliedAsOf = targetIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _applicationRepository.AsNoTracking()
                .Where(a => a.TargetType == settlementType && targetIds.Contains(a.TargetId) && a.CreationTime < appliedCutoff)
                .GroupBy(a => a.TargetId)
                .Select(g => new { TargetId = g.Key, Applied = g.Sum(x => x.AppliedAmount) })
                .ToDictionaryAsync(x => x.TargetId, x => x.Applied, cancellationToken);

        var open = targets
            .Select(e => (Entry: e, Outstanding: e.Amount - appliedAsOf.GetValueOrDefault(e.DocId)))
            .Where(x => x.Outstanding > 0)
            .ToList();
        if (open.Count > MaxLines)
            return TooManyLines<StatementBody>(open.Count);

        var foreign = ForeignCurrencyRejection<StatementBody>(open.Select(x => x.Entry));
        if (foreign != null)
            return foreign;

        var lines = open
            .OrderBy(x => x.Entry.DocDate)
            .Select(x => new StatementLineDto
            {
                DocDate = x.Entry.DocDate,
                DueDate = x.Entry.DueDate,
                DocType = x.Entry.DocType,
                DocId = x.Entry.DocId,
                Number = x.Entry.Number,
                Charge = x.Entry.Amount > 0 ? x.Entry.Amount : 0,
                Payment = x.Entry.Amount < 0 ? -x.Entry.Amount : 0,
                Outstanding = x.Outstanding,
                OverdueDays = x.Entry.DueDate != null && x.Entry.DueDate.Value.ToUtcDate() < asOf
                    ? (int)(asOf - x.Entry.DueDate.Value.ToUtcDate()).TotalDays
                    : 0,
                // Open Item 上的"余额"是未清额本身：这张单还欠多少。
                Balance = x.Outstanding,
            })
            .ToList();

        return Ok(new StatementBody(lines, 0m));
    }

    /// <summary>
    /// Activity：期初余额 + 本期往来 + 逐行累计余额。
    /// </summary>
    private async Task<Result<StatementBody>> BuildActivityLinesAsync(
        FinancePartyType partyType, Guid partyId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var period = await _ledger.GetAllTransactionsAsync(partyType, partyId, from, to, cancellationToken);
        if (!period.Succeeded)
            return Fail<StatementBody>(period.Message!, period.Code ?? 400);

        // 与账龄同一口径（期初与期末余额都取自账龄）：作废单据在账龄里视为从未存在，
        // 费用直接付清、从不挂应付，列进来，末行余额就与同一张纸上的应付金额对不上。
        var entries = period.Data!
            .Where(e => e.Status != FinanceDocumentStatus.Voided && e.DocType != FinanceSourceTypes.Expense)
            .ToList();
        if (entries.Count > MaxLines)
            return TooManyLines<StatementBody>(entries.Count);

        var foreign = ForeignCurrencyRejection<StatementBody>(entries);
        if (foreign != null)
            return foreign;

        // 期初取起始日前一天的账龄余额，与期末余额同一口径；上面的筛选让本期流水与账龄逐项一致，
        // 于是「期初 + 本期净发生额」落在期末余额上（往来方流水的符号已统一：发票为正、收款为负）。
        var priorSummary = await _ledger.GetSummaryAsync(partyType, partyId, from.AddDays(-1), cancellationToken: cancellationToken);
        var opening = priorSummary.Succeeded ? priorSummary.Data!.OpenBalance : 0m;

        var running = opening;
        var lines = new List<StatementLineDto>();
        foreach (var e in entries.OrderBy(x => x.DocDate).ThenBy(x => x.Number))
        {
            running = _helper.Round(running + e.Amount);
            lines.Add(new StatementLineDto
            {
                DocDate = e.DocDate,
                DueDate = e.DueDate,
                DocType = e.DocType,
                DocId = e.DocId,
                Number = e.Number,
                Charge = e.Amount > 0 ? e.Amount : 0,
                Payment = e.Amount < 0 ? -e.Amount : 0,
                Outstanding = e.Outstanding,
                OverdueDays = e.OverdueDays,
                Balance = running,
            });
        }

        return Ok(new StatementBody(lines, opening));
    }

    /// <summary>
    /// 一张对账单的行数上限。
    /// </summary>
    /// <remarks>
    /// 对账单是要寄给对方看的一张纸，不是分页列表；超过这个量级说明该按期间拆开寄，
    /// 而不是让系统悄悄截断——截断的余额是错的（Activity 形态的逐行累计余额会停在
    /// 第 500 行，与同一张纸上的期末余额对不上）。故超限即拒绝，与总账 CSV 导出
    /// 的 <c>ReportExportMaxRows</c> 同一取舍。
    /// </remarks>
    private const int MaxLines = 500;

    /// <summary>
    /// 含外币单据时拒绝出单（返回 null = 全是本位币）。
    /// </summary>
    /// <remarks>
    /// 期初、期末与账龄分桶都是本位币（账龄按捕获汇率折算），单据行却是交易币金额：
    /// 把 EUR 加进 USD 的累计余额，纸上就同时印着两个互相矛盾的数。换算成本位币也不够，
    /// 同一张发票与收款汇率不同时，差额作为已实现汇兑损益记在控制科目上，不在任何一张单据里，
    /// 累计余额仍落不到期末余额上。外币对账单（按交易币出单，或带汇兑差额行）需要另行设计，
    /// 在那之前拒绝，比寄出一张自相矛盾的纸好。
    /// </remarks>
    private Result<T>? ForeignCurrencyRejection<T>(IEnumerable<PartyLedgerEntryDto> entries)
    {
        var baseCurrency = _options.BaseCurrency;
        var foreign = entries
            .Select(e => e.Currency)
            .Where(c => !string.IsNullOrWhiteSpace(c) && !string.Equals(c, baseCurrency, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ToUpperInvariant())
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        if (foreign.Count == 0)
            return null;

        return Fail<T>(
            $"Statements are issued in the base currency ({baseCurrency}), but this one would include documents in {string.Join(", ", foreign)}. "
            + "Foreign-currency statements are not supported in this version.", 400);
    }

    /// <summary>超限拒绝：两种形态各自的补救办法都写出来，别只说"太多了"。</summary>
    private Result<T> TooManyLines<T>(int total) => Fail<T>(
        $"The statement would contain {total} lines, exceeding the limit of {MaxLines}. "
        + "Issue it for a shorter period, or settle open items first.", 400);
}
