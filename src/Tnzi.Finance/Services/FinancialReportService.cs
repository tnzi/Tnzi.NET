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
    private readonly FinanceOptions _options;

    public FinancialReportService(
        IServiceProvider serviceProvider,
        IReadOnlyRepository<JournalLine, Guid> lineRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<Invoice, Guid> invoiceRepository,
        IReadOnlyRepository<Bill, Guid> billRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IOptions<FinanceOptions> options)
        : base(serviceProvider)
    {
        _lineRepository = Check.NotNull(lineRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _billRepository = Check.NotNull(billRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
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

        var lines = await PostedLines
            .Where(l => l.AccountId == accountId && l.PostingDate >= fromDate && l.PostingDate < toExclusive)
            .OrderBy(l => l.PostingDate)
            .ThenBy(l => l.LineNumber)
            .Select(l => new GeneralLedgerLineDto
            {
                JournalEntryId = l.JournalEntryId,
                EntryNumber = l.JournalEntry!.Number,
                PostingDate = l.PostingDate,
                Memo = l.Memo ?? l.JournalEntry.Memo,
                Debit = l.Debit,
                Credit = l.Credit,
                PartyType = l.PartyType,
                PartyId = l.PartyId
            })
            .CreateAsync(paging.PageIndex, paging.PageSize, cancellationToken);

        var openingBalance = openingDebit - openingCredit;
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
}
