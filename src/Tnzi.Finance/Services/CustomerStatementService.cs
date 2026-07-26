namespace Tnzi.Finance.Services;

/// <summary>
/// 客户/供应商对账单
/// </summary>
/// <remarks>
/// 余额、账龄、流水三者都**复用既有服务**（账龄报表 + 往来方流水），不另写一遍：
/// 寄出去的那张纸与自己账上的数对不上，比不寄更糟。
/// </remarks>
public class CustomerStatementService : ApplicationService, ICustomerStatementService
{
    private readonly IPartyLedgerService _ledger;
    private readonly IFinancialReportService _reports;
    private readonly IDunningPolicy _dunning;
    private readonly FinanceDocumentHelper _helper;
    private readonly FinanceOptions _options;

    public CustomerStatementService(
        IServiceProvider serviceProvider,
        IPartyLedgerService ledger,
        IFinancialReportService reports,
        IDunningPolicy dunning,
        FinanceDocumentHelper helper,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _ledger = Check.NotNull(ledger);
        _reports = Check.NotNull(reports);
        _dunning = Check.NotNull(dunning);
        _helper = Check.NotNull(helper);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<CustomerStatementDto>> GetAsync(
        FinancePartyType partyType, Guid partyId, CustomerStatementQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var to = (query.To ?? DateTime.UtcNow.Date).ToUtcDate();
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
        var date = (asOf ?? DateTime.UtcNow.Date).ToUtcDate();

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
    /// Open Item：只列还没付清的单据。
    /// </summary>
    private async Task<Result<StatementBody>> BuildOpenItemLinesAsync(
        FinancePartyType partyType, Guid partyId, DateTime asOf, CancellationToken cancellationToken)
    {
        var page = await _ledger.GetTransactionsAsync(partyType, partyId,
            new PartyLedgerQueryDto { PageIndex = 1, PageSize = MaxLines, OpenOnly = true, To = asOf }, cancellationToken);
        if (!page.Succeeded)
            return Fail<StatementBody>(page.Message!, page.Code ?? 400);
        if (page.Data!.TotalCount > MaxLines)
            return TooManyLines<StatementBody>(page.Data.TotalCount);

        var lines = page.Data.Items
            .OrderBy(e => e.DocDate)
            .Select(e => new StatementLineDto
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
                // Open Item 上的"余额"是未清额本身：这张单还欠多少。
                Balance = e.Outstanding,
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
        var period = await _ledger.GetTransactionsAsync(partyType, partyId,
            new PartyLedgerQueryDto { PageIndex = 1, PageSize = MaxLines, From = from, To = to }, cancellationToken);
        if (!period.Succeeded)
            return Fail<StatementBody>(period.Message!, period.Code ?? 400);
        if (period.Data!.TotalCount > MaxLines)
            return TooManyLines<StatementBody>(period.Data.TotalCount);

        // 期初 = 期末余额 − 本期净发生额。用减法而不是再查一次历史：往来方流水
        // 的符号已经统一（发票为正、收款为负），两者必然自洽。
        var priorSummary = await _ledger.GetSummaryAsync(partyType, partyId, from.AddDays(-1), cancellationToken: cancellationToken);
        var opening = priorSummary.Succeeded ? priorSummary.Data!.OpenBalance : 0m;

        var running = opening;
        var lines = new List<StatementLineDto>();
        foreach (var e in period.Data.Items.OrderBy(x => x.DocDate).ThenBy(x => x.Number))
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

    /// <summary>超限拒绝：两种形态各自的补救办法都写出来，别只说"太多了"。</summary>
    private Result<T> TooManyLines<T>(int total) => Fail<T>(
        $"The statement would contain {total} lines, exceeding the limit of {MaxLines}. "
        + "Issue it for a shorter period, or settle open items first.", 400);
}
