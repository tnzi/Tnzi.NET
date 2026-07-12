namespace Tnzi.Finance.Services;

/// <summary>
/// 财务报表服务（全部从总账行数据库级聚合，本位币口径）
/// </summary>
public class FinancialReportService : ApplicationService, IFinancialReportService
{
    private readonly IReadOnlyRepository<JournalLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<Invoice, Guid> _invoiceRepository;
    private readonly IReadOnlyRepository<Bill, Guid> _billRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IReadOnlyRepository<TaxRate, Guid> _taxRateRepository;
    private readonly FinanceOptions _options;

    public FinancialReportService(
        IServiceProvider serviceProvider,
        IReadOnlyRepository<JournalLine, Guid> lineRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<Invoice, Guid> invoiceRepository,
        IReadOnlyRepository<Bill, Guid> billRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IReadOnlyRepository<TaxRate, Guid> taxRateRepository,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _lineRepository = Check.NotNull(lineRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _billRepository = Check.NotNull(billRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _taxRateRepository = Check.NotNull(taxRateRepository);
        _options = Check.NotNull(options).Value;
    }

    private IQueryable<JournalLine> PostedLines => _lineRepository.AsNoTracking().Where(l => l.IsPosted);

    public async Task<Result<TrialBalanceReportDto>> GetTrialBalanceAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Fail<TrialBalanceReportDto>("The 'to' date must not be earlier than the 'from' date.");

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        var opening = await SumByAccountAsync(l => l.PostingDate < fromDate, cancellationToken);
        var period = await SumByAccountAsync(l => l.PostingDate >= fromDate && l.PostingDate < toExclusive, cancellationToken);

        var accounts = await GetPostableAccountsAsync(cancellationToken);

        var report = new TrialBalanceReportDto
        {
            From = fromDate,
            To = to.Date,
            BaseCurrency = _options.BaseCurrency
        };

        foreach (var account in accounts)
        {
            var hasOpening = opening.TryGetValue(account.Id, out var o);
            var hasPeriod = period.TryGetValue(account.Id, out var p);
            if (!hasOpening && !hasPeriod)
                continue;

            var openingBalance = o.Debit - o.Credit;
            var row = new TrialBalanceRowDto
            {
                AccountId = account.Id,
                Code = account.Code,
                Name = account.Name,
                RootType = account.RootType,
                OpeningBalance = openingBalance,
                PeriodDebit = p.Debit,
                PeriodCredit = p.Credit,
                ClosingBalance = openingBalance + p.Debit - p.Credit
            };

            if (row.OpeningBalance == 0 && row.PeriodDebit == 0 && row.PeriodCredit == 0 && row.ClosingBalance == 0)
                continue;

            report.Rows.Add(row);
            report.TotalOpeningBalance += row.OpeningBalance;
            report.TotalPeriodDebit += row.PeriodDebit;
            report.TotalPeriodCredit += row.PeriodCredit;
            report.TotalClosingBalance += row.ClosingBalance;
        }

        return Ok(report);
    }

    public async Task<Result<BalanceSheetReportDto>> GetBalanceSheetAsync(DateTime asOf, CancellationToken cancellationToken = default)
    {
        var toExclusive = asOf.ToUtcDate().AddDays(1);

        var sums = await SumByAccountAsync(l => l.PostingDate < toExclusive, cancellationToken);
        var accounts = await GetPostableAccountsAsync(cancellationToken);

        var report = new BalanceSheetReportDto
        {
            AsOf = asOf.Date,
            BaseCurrency = _options.BaseCurrency
        };

        foreach (var account in accounts)
        {
            if (!sums.TryGetValue(account.Id, out var s))
                continue;

            switch (account.RootType)
            {
                case AccountRootType.Asset:
                    AddRow(report.Assets, account, s.Debit - s.Credit);
                    break;
                case AccountRootType.Liability:
                    AddRow(report.Liabilities, account, s.Credit - s.Debit);
                    break;
                case AccountRootType.Equity:
                    AddRow(report.Equity, account, s.Credit - s.Debit);
                    break;
                case AccountRootType.Income:
                case AccountRootType.Expense:
                    // 收入与费用累计净额构成本年（累计）利润计算行
                    report.CurrentEarnings += s.Credit - s.Debit;
                    break;
            }
        }

        report.TotalAssets = report.Assets.Sum(r => r.Balance);
        report.TotalLiabilities = report.Liabilities.Sum(r => r.Balance);
        report.TotalEquity = report.Equity.Sum(r => r.Balance) + report.CurrentEarnings;
        report.BalanceCheck = report.TotalAssets - report.TotalLiabilities - report.TotalEquity;

        return Ok(report);
    }

    public async Task<Result<ProfitAndLossReportDto>> GetProfitAndLossAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Fail<ProfitAndLossReportDto>("The 'to' date must not be earlier than the 'from' date.");

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        var sums = await SumByAccountAsync(l => l.PostingDate >= fromDate && l.PostingDate < toExclusive, cancellationToken);
        var accounts = await GetPostableAccountsAsync(cancellationToken);

        var report = new ProfitAndLossReportDto
        {
            From = fromDate,
            To = to.Date,
            BaseCurrency = _options.BaseCurrency
        };

        foreach (var account in accounts)
        {
            if (!sums.TryGetValue(account.Id, out var s))
                continue;

            switch (account.RootType)
            {
                case AccountRootType.Income:
                    AddRow(report.Income, account, s.Credit - s.Debit);
                    break;
                case AccountRootType.Expense:
                    AddRow(report.Expenses, account, s.Debit - s.Credit);
                    break;
            }
        }

        report.TotalIncome = report.Income.Sum(r => r.Balance);
        report.TotalExpenses = report.Expenses.Sum(r => r.Balance);
        report.NetProfit = report.TotalIncome - report.TotalExpenses;

        return Ok(report);
    }

    public async Task<Result<GeneralLedgerReportDto>> GetGeneralLedgerAsync(Guid accountId, DateTime from, DateTime to, PagedQueryDto paging, CancellationToken cancellationToken = default)
    {
        Check.NotNull(paging);

        if (to.Date < from.Date)
            return Fail<GeneralLedgerReportDto>("The 'to' date must not be earlier than the 'from' date.");

        var account = await _accountRepository.GetAsync(accountId, cancellationToken);
        if (account == null)
            return Fail<GeneralLedgerReportDto>("Account not found.", 404);

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        // 期初/期间借贷四项聚合合并为单次往返（条件求和）
        var sums = await PostedLines
            .Where(l => l.AccountId == accountId && l.PostingDate < toExclusive)
            .GroupBy(l => 1)
            .Select(g => new
            {
                OpeningDebit = g.Sum(l => l.PostingDate < fromDate ? l.Debit : 0m),
                OpeningCredit = g.Sum(l => l.PostingDate < fromDate ? l.Credit : 0m),
                PeriodDebit = g.Sum(l => l.PostingDate >= fromDate ? l.Debit : 0m),
                PeriodCredit = g.Sum(l => l.PostingDate >= fromDate ? l.Credit : 0m)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var openingDebit = sums?.OpeningDebit ?? 0m;
        var openingCredit = sums?.OpeningCredit ?? 0m;
        var periodDebit = sums?.PeriodDebit ?? 0m;
        var periodCredit = sums?.PeriodCredit ?? 0m;

        var lines = await ProjectLedgerLines(OrderedPeriodLines(accountId, fromDate, toExclusive))
            .CreateAsync(paging.PageIndex, paging.PageSize, cancellationToken);

        var openingBalance = openingDebit - openingCredit;

        // 第 N 页起点余额 = 期初余额 + 页首之前行的净额（单次聚合，首页零额外查询）。
        // 页行与前缀和是两次独立查询：并发过账落在两查询之间时本页余额可能整体偏移
        // （报表为近似快照，非可串行化读；刷新即自愈）
        var running = openingBalance;
        if (paging.Skip > 0)
        {
            running += await OrderedPeriodLines(accountId, fromDate, toExclusive)
                .Take(paging.Skip)
                .SumAsync(l => l.Debit - l.Credit, cancellationToken);
        }

        foreach (var line in lines.Items)
        {
            running += line.Debit - line.Credit;
            line.RunningBalance = running;
        }

        var report = new GeneralLedgerReportDto
        {
            AccountId = account.Id,
            Code = account.Code,
            Name = account.Name,
            From = fromDate,
            To = to.Date,
            BaseCurrency = _options.BaseCurrency,
            OpeningBalance = openingBalance,
            ClosingBalance = openingBalance + periodDebit - periodCredit,
            Lines = lines
        };

        return Ok(report);
    }

    /// <summary>
    /// 期间内已过账行的稳定全序：同日跨凭证按凭证号（过账时顺序分配、补零后字符串可排序，
    /// 凭证号唯一故构成全序；顺序 GUID 的字符串序不保证时序）、凭证内按行号。
    /// 运行余额与分页导出都依赖此确定性顺序
    /// </summary>
    private IQueryable<JournalLine> OrderedPeriodLines(Guid accountId, DateTime fromDate, DateTime toExclusive)
        => PostedLines
            .Where(l => l.AccountId == accountId && l.PostingDate >= fromDate && l.PostingDate < toExclusive)
            .OrderBy(l => l.PostingDate)
            .ThenBy(l => l.JournalEntry!.Number)
            .ThenBy(l => l.LineNumber);

    private static IQueryable<GeneralLedgerLineDto> ProjectLedgerLines(IQueryable<JournalLine> query)
        => query.Select(l => new GeneralLedgerLineDto
        {
            JournalEntryId = l.JournalEntryId,
            EntryNumber = l.JournalEntry!.Number,
            PostingDate = l.PostingDate,
            Memo = l.Memo ?? l.JournalEntry.Memo,
            Debit = l.Debit,
            Credit = l.Credit,
            PartyType = l.PartyType,
            PartyId = l.PartyId,
            SourceType = l.JournalEntry.SourceType,
            SourceId = l.JournalEntry.SourceId
        });

    private async Task<Dictionary<Guid, (decimal Debit, decimal Credit)>> SumByAccountAsync(
        Expression<Func<JournalLine, bool>> predicate, CancellationToken cancellationToken)
    {
        var sums = await PostedLines
            .Where(predicate)
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .ToListAsync(cancellationToken);

        return sums.ToDictionary(s => s.AccountId, s => (s.Debit, s.Credit));
    }

    private Task<List<Account>> GetPostableAccountsAsync(CancellationToken cancellationToken)
        => _accountRepository.AsNoTracking()
            .Where(a => !a.IsGroup)
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

    private static void AddRow(List<ReportAccountRowDto> rows, Account account, decimal balance)
    {
        if (balance == 0)
            return;

        rows.Add(new ReportAccountRowDto
        {
            AccountId = account.Id,
            Code = account.Code,
            Name = account.Name,
            RootType = account.RootType,
            SubType = account.SubType,
            Balance = balance
        });
    }

    public async Task<Result<AgingReportDto>> GetArAgingAsync(DateTime asOf, CancellationToken cancellationToken = default)
    {
        var asOfDate = asOf.ToUtcDate();
        var open = await _invoiceRepository.AsNoTracking()
            .Where(i => (i.Status == FinanceDocumentStatus.Posted || i.Status == FinanceDocumentStatus.PartiallyPaid) &&
                        i.AppliedTotal < i.Total && i.DocDate <= asOfDate)
            .Select(i => new OpenAgingItem(i.CustomerId, i.DueDate ?? i.DocDate, (i.Total - i.AppliedTotal) * i.ExchangeRate))
            .ToListAsync(cancellationToken);

        var names = await _customerRepository.AsNoTracking()
            .Where(c => open.Select(o => o.PartyId).Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return Ok(BuildAging(asOfDate, open, names));
    }

    public async Task<Result<AgingReportDto>> GetApAgingAsync(DateTime asOf, CancellationToken cancellationToken = default)
    {
        var asOfDate = asOf.ToUtcDate();
        var open = await _billRepository.AsNoTracking()
            .Where(b => (b.Status == FinanceDocumentStatus.Posted || b.Status == FinanceDocumentStatus.PartiallyPaid) &&
                        b.AppliedTotal < b.Total && b.DocDate <= asOfDate)
            .Select(b => new OpenAgingItem(b.VendorId, b.DueDate ?? b.DocDate, (b.Total - b.AppliedTotal) * b.ExchangeRate))
            .ToListAsync(cancellationToken);

        var names = await _vendorRepository.AsNoTracking()
            .Where(v => open.Select(o => o.PartyId).Contains(v.Id))
            .Select(v => new { v.Id, v.Name })
            .ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken);

        return Ok(BuildAging(asOfDate, open, names));
    }

    private sealed record OpenAgingItem(Guid PartyId, DateTime DueDate, decimal OutstandingBase);

    private AgingReportDto BuildAging(DateTime asOf, List<OpenAgingItem> items, Dictionary<Guid, string> names)
    {
        var report = new AgingReportDto { AsOf = asOf, BaseCurrency = _options.BaseCurrency };

        static void AddToBucket(AgingBucketsDto buckets, int overdueDays, decimal amount)
        {
            if (overdueDays <= 0) buckets.Current += amount;
            else if (overdueDays <= 30) buckets.Days1To30 += amount;
            else if (overdueDays <= 60) buckets.Days31To60 += amount;
            else if (overdueDays <= 90) buckets.Days61To90 += amount;
            else buckets.Over90 += amount;
            buckets.Total += amount;
        }

        foreach (var group in items.GroupBy(i => i.PartyId).OrderBy(g => names.GetValueOrDefault(g.Key)))
        {
            var row = new AgingRowDto { PartyId = group.Key, PartyName = names.GetValueOrDefault(group.Key) ?? group.Key.ToString() };
            foreach (var item in group)
            {
                var amount = Math.Round(item.OutstandingBase, _options.BaseCurrencyDecimals, MidpointRounding.AwayFromZero);
                var overdue = (int)(asOf - item.DueDate).TotalDays;
                AddToBucket(row, overdue, amount);
                AddToBucket(report.Totals, overdue, amount);
            }

            report.Rows.Add(row);
        }

        return report;
    }

    public async Task<Result<TaxSummaryReportDto>> GetTaxSummaryAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Fail<TaxSummaryReportDto>("The 'to' date must not be earlier than the 'from' date.");

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        // 税维度行按（税率 × 科目角色）单次聚合：销项 = TaxPayable 角色科目贷方净额，
        // 进项 = TaxReceivable 角色科目借方净额；其他科目上的税维度行不计入申报口径
        var sums = await PostedLines
            .Where(l => l.TaxRateId != null && l.PostingDate >= fromDate && l.PostingDate < toExclusive)
            .GroupBy(l => new { l.TaxRateId, l.Account!.SystemRole })
            .Select(g => new
            {
                g.Key.TaxRateId,
                g.Key.SystemRole,
                Debit = g.Sum(l => l.Debit),
                Credit = g.Sum(l => l.Credit)
            })
            .ToListAsync(cancellationToken);

        var report = new TaxSummaryReportDto
        {
            From = fromDate,
            To = to.Date,
            BaseCurrency = _options.BaseCurrency
        };

        if (sums.Count == 0)
            return Ok(report);

        var rateIds = sums.Select(s => s.TaxRateId!.Value).Distinct().ToList();

        // 历史行可能引用已停用/软删的税率：按 ID 精确解析名称时忽略全局过滤器
        //（rateIds 来自当前租户已过滤的总账行，不构成跨租户泄漏面）
        var rates = await _taxRateRepository.AsNoTracking()
            .IgnoreQueryFilters()
            .Include(r => r.Agency)
            .Where(r => rateIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var rateId in rateIds)
        {
            var output = sums.Where(s => s.TaxRateId == rateId && s.SystemRole == AccountSystemRole.TaxPayable)
                .Sum(s => s.Credit - s.Debit);
            var input = sums.Where(s => s.TaxRateId == rateId && s.SystemRole == AccountSystemRole.TaxReceivable)
                .Sum(s => s.Debit - s.Credit);
            if (output == 0 && input == 0)
                continue;

            rates.TryGetValue(rateId, out var rate);
            report.Rows.Add(new TaxSummaryRowDto
            {
                TaxRateId = rateId,
                RateName = rate?.Name,
                Rate = rate?.Rate,
                AgencyId = rate?.AgencyId,
                AgencyName = rate?.Agency?.Name,
                OutputTax = output,
                InputTax = input,
                NetTax = output - input
            });
        }

        report.Rows = report.Rows.OrderBy(r => r.AgencyName).ThenBy(r => r.RateName).ToList();
        report.TotalOutputTax = report.Rows.Sum(r => r.OutputTax);
        report.TotalInputTax = report.Rows.Sum(r => r.InputTax);
        report.TotalNetTax = report.TotalOutputTax - report.TotalInputTax;

        return Ok(report);
    }

    public async Task<Result<CashFlowReportDto>> GetCashFlowAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Fail<CashFlowReportDto>("The 'to' date must not be earlier than the 'from' date.");

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        // 期初 + 期间条件求和合并为单次全账本扫描（与 GetGeneralLedgerAsync 的合并聚合同款）
        var sums = await PostedLines
            .Where(l => l.PostingDate < toExclusive)
            .GroupBy(l => l.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                OpeningDebit = g.Sum(l => l.PostingDate < fromDate ? l.Debit : 0m),
                OpeningCredit = g.Sum(l => l.PostingDate < fromDate ? l.Credit : 0m),
                PeriodDebit = g.Sum(l => l.PostingDate >= fromDate ? l.Debit : 0m),
                PeriodCredit = g.Sum(l => l.PostingDate >= fromDate ? l.Credit : 0m)
            })
            .ToDictionaryAsync(x => x.AccountId, cancellationToken);
        var accounts = await GetPostableAccountsAsync(cancellationToken);

        var report = new CashFlowReportDto
        {
            From = fromDate,
            To = to.Date,
            BaseCurrency = _options.BaseCurrency
        };

        foreach (var account in accounts)
        {
            if (!sums.TryGetValue(account.Id, out var p))
                continue;

            // 损益科目先行：净额整体经净利润进入经营活动，其自身分类被忽略——
            // 误标为 CashEquivalent 的收入/费用科目不得流入现金桶（否则净利润被悄悄低估
            // 而恒等式两侧同变仍显示 0，校验行给出虚假的安心）
            if (account.RootType is AccountRootType.Income or AccountRootType.Expense)
            {
                report.NetProfit += p.PeriodCredit - p.PeriodDebit;
                continue;
            }

            if (account.CashFlowActivity == CashFlowActivity.CashEquivalent)
            {
                // 现金科目是报表的解释对象：计入期初/期末现金与现金净变动，不进活动分桶
                report.OpeningCash += p.OpeningDebit - p.OpeningCredit;
                report.CashMovement += p.PeriodDebit - p.PeriodCredit;
                continue;
            }

            if (p.PeriodDebit == 0 && p.PeriodCredit == 0)
                continue;

            // 资产负债类科目按现金流视角取贡献（流入为正：资产减少/负债权益增加 = 贷方净额）
            var bucket = account.CashFlowActivity switch
            {
                CashFlowActivity.Investing => report.Investing,
                CashFlowActivity.Financing => report.Financing,
                CashFlowActivity.Operating => report.Operating,
                _ => report.Unclassified
            };
            AddRow(bucket, account, p.PeriodCredit - p.PeriodDebit);
        }

        report.TotalOperating = report.NetProfit + report.Operating.Sum(r => r.Balance);
        report.TotalInvesting = report.Investing.Sum(r => r.Balance);
        report.TotalFinancing = report.Financing.Sum(r => r.Balance);
        report.TotalUnclassified = report.Unclassified.Sum(r => r.Balance);
        report.NetCashFlow = report.TotalOperating + report.TotalInvesting + report.TotalFinancing + report.TotalUnclassified;
        report.ClosingCash = report.OpeningCash + report.CashMovement;
        report.CheckDifference = report.NetCashFlow - report.CashMovement;

        return Ok(report);
    }

    /// <summary>报表 Result 到 CSV Result 的统一包装（失败码/消息透传约定只写这一处）</summary>
    private static async Task<Result<string>> ToCsvAsync<T>(Task<Result<T>> report, Func<T, string> write)
    {
        var result = await report;
        return result.Succeeded
            ? Result<string>.Success(write(result.Data!))
            : Result<string>.Failure(result.Message ?? "Report failed.", result.Code ?? 400);
    }

    public Task<Result<string>> ExportTrialBalanceCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetTrialBalanceAsync(from, to, cancellationToken), ReportCsvWriter.TrialBalance);

    public Task<Result<string>> ExportBalanceSheetCsvAsync(DateTime asOf, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetBalanceSheetAsync(asOf, cancellationToken), ReportCsvWriter.BalanceSheet);

    public Task<Result<string>> ExportProfitAndLossCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetProfitAndLossAsync(from, to, cancellationToken), ReportCsvWriter.ProfitAndLoss);

    public async Task<Result<string>> ExportGeneralLedgerCsvAsync(Guid accountId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Fail<string>("The 'to' date must not be earlier than the 'from' date.");

        var account = await _accountRepository.GetAsync(accountId, cancellationToken);
        if (account == null)
            return Fail<string>("Account not found.", 404);

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        var openingSums = await PostedLines
            .Where(l => l.AccountId == accountId && l.PostingDate < fromDate)
            .GroupBy(l => 1)
            .Select(g => new { Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .FirstOrDefaultAsync(cancellationToken);

        var openingBalance = (openingSums?.Debit ?? 0m) - (openingSums?.Credit ?? 0m);

        // 成功路径单次扫描（多取一行探测超限）；拒绝超限而非静默截断：
        // 截断的运行余额会误导对账。精确行数仅在拒绝路径补一次未排序计数
        var lines = await ProjectLedgerLines(OrderedPeriodLines(accountId, fromDate, toExclusive))
            .Take(_options.ReportExportMaxRows + 1)
            .ToListAsync(cancellationToken);
        if (lines.Count > _options.ReportExportMaxRows)
        {
            var count = await PostedLines
                .CountAsync(l => l.AccountId == accountId && l.PostingDate >= fromDate && l.PostingDate < toExclusive, cancellationToken);
            return Fail<string>($"The export would contain {count} rows, exceeding the limit of {_options.ReportExportMaxRows}. Narrow the date range.", 400);
        }

        var running = openingBalance;
        foreach (var line in lines)
        {
            running += line.Debit - line.Credit;
            line.RunningBalance = running;
        }

        var header = new GeneralLedgerReportDto
        {
            AccountId = account.Id,
            Code = account.Code,
            Name = account.Name,
            From = fromDate,
            To = to.Date,
            BaseCurrency = _options.BaseCurrency,
            OpeningBalance = openingBalance,
            ClosingBalance = running
        };

        return Ok<string>(ReportCsvWriter.GeneralLedger(header, lines));
    }

    public Task<Result<string>> ExportArAgingCsvAsync(DateTime asOf, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetArAgingAsync(asOf, cancellationToken), ReportCsvWriter.Aging);

    public Task<Result<string>> ExportApAgingCsvAsync(DateTime asOf, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetApAgingAsync(asOf, cancellationToken), ReportCsvWriter.Aging);

    public Task<Result<string>> ExportTaxSummaryCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetTaxSummaryAsync(from, to, cancellationToken), ReportCsvWriter.TaxSummary);

    public Task<Result<string>> ExportCashFlowCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetCashFlowAsync(from, to, cancellationToken), ReportCsvWriter.CashFlow);
}
