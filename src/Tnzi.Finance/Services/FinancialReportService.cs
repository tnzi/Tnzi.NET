namespace Tnzi.Finance.Services;

/// <summary>
/// 财务报表服务（全部从总账行数据库级聚合，本位币口径）
/// </summary>
public class FinancialReportService : ApplicationService, IFinancialReportService
{
    /// <summary>结构性 tie-out 校验容差（半分，吸收本位币舍入尾差；超出即聚合缺陷）。</summary>
    private const decimal TieOutTolerance = 0.005m;

    private readonly IReadOnlyRepository<JournalLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<Invoice, Guid> _invoiceRepository;
    private readonly IReadOnlyRepository<Bill, Guid> _billRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IReadOnlyRepository<TaxRate, Guid> _taxRateRepository;
    private readonly IReadOnlyRepository<CreditMemo, Guid> _creditMemoRepository;
    private readonly IReadOnlyRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IReadOnlyRepository<PaymentApplication, Guid> _applicationRepository;
    private readonly BalanceSummaryReader _reader;
    private readonly GeneralLedgerReader _ledgerReader;
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
        IReadOnlyRepository<CreditMemo, Guid> creditMemoRepository,
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<PaymentApplication, Guid> applicationRepository,
        BalanceSummaryReader reader,
        GeneralLedgerReader ledgerReader,
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
        _creditMemoRepository = Check.NotNull(creditMemoRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _applicationRepository = Check.NotNull(applicationRepository);
        _reader = Check.NotNull(reader);
        _ledgerReader = Check.NotNull(ledgerReader);
        _options = Check.NotNull(options).Value;
    }

    private IQueryable<JournalLine> PostedLines => _lineRepository.AsNoTracking().Where(l => l.IsPosted);

    public async Task<Result<TrialBalanceReportDto>> GetTrialBalanceAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Fail<TrialBalanceReportDto>("The 'to' date must not be earlier than the 'from' date.");

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        var sums = await _reader.SumOpeningAndPeriodByAccountAsync(fromDate, toExclusive, cancellationToken);

        var accounts = await GetPostableAccountsAsync(cancellationToken);

        var report = new TrialBalanceReportDto
        {
            From = fromDate,
            To = to.Date,
            BaseCurrency = _options.BaseCurrency
        };

        foreach (var account in accounts)
        {
            if (!sums.TryGetValue(account.Id, out var s))
                continue;

            var openingBalance = s.OpeningDebit - s.OpeningCredit;
            var row = new TrialBalanceRowDto
            {
                AccountId = account.Id,
                Code = account.Code,
                Name = account.Name,
                RootType = account.RootType,
                OpeningBalance = openingBalance,
                PeriodDebit = s.PeriodDebit,
                PeriodCredit = s.PeriodCredit,
                ClosingBalance = openingBalance + s.PeriodDebit - s.PeriodCredit
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

        var sums = await _reader.SumByAccountAsync(null, toExclusive, cancellationToken);
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
        // Structural tie-out: for a double-entry ledger the balance sheet must balance.
        // A non-zero check is never a data condition, it is a report aggregation defect,
        // so surface it server-side instead of only exposing a silent number in the DTO.
        if (Math.Abs(report.BalanceCheck) > TieOutTolerance)
            Logger.LogWarning(
                "Balance sheet does not tie out as of {AsOf}: assets {Assets} - liabilities {Liabilities} - equity {Equity} = {Diff}.",
                asOf, report.TotalAssets, report.TotalLiabilities, report.TotalEquity, report.BalanceCheck);

        return Ok(report);
    }

    public async Task<Result<ProfitAndLossReportDto>> GetProfitAndLossAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Fail<ProfitAndLossReportDto>("The 'to' date must not be earlier than the 'from' date.");

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        var sums = await _reader.SumByAccountAsync(fromDate, toExclusive, cancellationToken);
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

    public Task<Result<GeneralLedgerReportDto>> GetGeneralLedgerAsync(Guid accountId, DateTime from, DateTime to, PagedQueryDto paging, CancellationToken cancellationToken = default)
        => GetGeneralLedgerAsync(accountId, from, to, paging, null, cancellationToken);

    /// <summary>总账明细：委托 <see cref="GeneralLedgerReader"/>（行序/分页/筛选自成一套机制）。</summary>
    public Task<Result<GeneralLedgerReportDto>> GetGeneralLedgerAsync(
        Guid accountId, DateTime from, DateTime to, PagedQueryDto paging, GeneralLedgerFilterDto? filter,
        CancellationToken cancellationToken = default)
        => _ledgerReader.GetGeneralLedgerAsync(accountId, from, to, paging, filter, cancellationToken);

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

    // AR/AP 账龄的时点与 tie-out 铁律：
    // ① 时点（point-in-time）——每单据的已核销额按 PaymentApplication.CreationTime <= asOf 重建，
    //    而非读当前 AppliedTotal；故 asOf 之后才发生的核销/付清不会追溯抹掉历史账龄。
    // ② tie-out——除未清发票/账单（正行），还纳入未核销收付款（预收/超收现金）与未核销贷项（客户信用）
    //    作为负行；因每笔核销恰好把源(收付款/贷项)与目标(发票/账单)配平，账龄合计 = GL 控制科目余额
    //    （审计首查的子账↔总账对账关系）。
    // 局限：单据 as-of 判据用 DocDate（沿模块既有口径）；DocDate == 过账日 时与 GL 精确一致，
    //    倒填过账或作废时点晚于 asOf 的边缘情形不追溯（罕见，RequireFiscalYearForPosting 默认 false）。

    public async Task<Result<AgingReportDto>> GetArAgingAsync(DateTime asOf, CancellationToken cancellationToken = default)
        => Ok(await ComputeArAgingAsync(asOf.ToUtcDate(), partyId: null, cancellationToken));

    public async Task<Result<AgingReportDto>> GetApAgingAsync(DateTime asOf, CancellationToken cancellationToken = default)
        => Ok(await ComputeApAgingAsync(asOf.ToUtcDate(), partyId: null, cancellationToken));

    /// <remarks>
    /// 与全量账龄是**同一段计算**，只是每一条查询都带上往来方谓词，核销聚合也只取触及该往来方单据的那些。
    /// ★ 此前往来方工作面（客户 / 供应商详情页、对账单）为了读一行，先算整本账的账龄：全部发票（含早已付清的）、
    /// 全部贷项、全部收付款、全部核销分组，开销随账本寿命线性增长而不是随这个往来方的活动量，对账单还要算两遍。
    /// </remarks>
    public async Task<Result<AgingReportDto>> GetPartyAgingAsync(
        FinancePartyType partyType, Guid partyId, DateTime asOf, CancellationToken cancellationToken = default)
        => Ok(partyType == FinancePartyType.Customer
            ? await ComputeArAgingAsync(asOf.ToUtcDate(), partyId, cancellationToken)
            : await ComputeApAgingAsync(asOf.ToUtcDate(), partyId, cancellationToken));

    /// <param name="asOfDate">时点（date-only）</param>
    /// <param name="partyId">只算这一个客户（null = 全部客户）</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task<AgingReportDto> ComputeArAgingAsync(DateTime asOfDate, Guid? partyId, CancellationToken cancellationToken)
    {
        var appliedCutoff = asOfDate.AddDays(1); // 含 asOf 当日记账的核销

        var invoiceQuery = _invoiceRepository.AsNoTracking()
            .Where(i => i.JournalEntryId != null && i.Status != FinanceDocumentStatus.Voided && i.DocDate <= asOfDate);
        var creditMemoQuery = _creditMemoRepository.AsNoTracking()
            .Where(c => c.JournalEntryId != null && c.Status != FinanceDocumentStatus.Voided && c.DocDate <= asOfDate);
        var paymentQuery = _paymentRepository.AsNoTracking()
            .Where(p => p.JournalEntryId != null && p.Status != FinanceDocumentStatus.Voided
                     && p.Direction == PaymentDirection.Inbound && p.PartyType == FinancePartyType.Customer
                     && p.DocDate <= asOfDate);

        // 核销聚合的范围：全量时不设限；单个往来方时只取触及他名下单据的核销（数据库侧子查询）。
        IQueryable<Guid>? partyInvoiceIds = null;
        IQueryable<Guid>? partyPaymentIds = null;
        IQueryable<Guid>? partyCreditMemoIds = null;
        if (partyId is { } id)
        {
            invoiceQuery = invoiceQuery.Where(i => i.CustomerId == id);
            creditMemoQuery = creditMemoQuery.Where(c => c.CustomerId == id);
            paymentQuery = paymentQuery.Where(p => p.PartyId == id);
            partyInvoiceIds = _invoiceRepository.AsNoTracking().Where(i => i.CustomerId == id).Select(i => i.Id);
            partyPaymentIds = _paymentRepository.AsNoTracking().Where(p => p.PartyId == id).Select(p => p.Id);
            partyCreditMemoIds = _creditMemoRepository.AsNoTracking().Where(c => c.CustomerId == id).Select(c => c.Id);
        }

        var appliedToInvoice = await AppliedByTargetAsync(SettlementDocType.Invoice, appliedCutoff, partyInvoiceIds, cancellationToken);
        var (appliedByPayment, appliedByCreditMemo) = await AppliedBySourceAsync(appliedCutoff, partyPaymentIds, partyCreditMemoIds, cancellationToken);

        var items = new List<OpenAgingItem>();

        // 未清发票（正行）：本位币开口额 = (Total − 时点已核销) × 捕获汇率，按 DueDate 分桶
        var invoices = await invoiceQuery
            .Select(i => new { i.Id, i.CustomerId, Due = i.DueDate ?? i.DocDate, i.Total, i.ExchangeRate })
            .ToListAsync(cancellationToken);
        foreach (var inv in invoices)
        {
            var openTxn = inv.Total - appliedToInvoice.GetValueOrDefault(inv.Id);
            if (openTxn <= 0) continue;
            items.Add(new OpenAgingItem(inv.CustomerId, inv.Due, openTxn * inv.ExchangeRate));
        }

        // 未核销贷项（负行=客户信用，归 Current）
        var creditMemos = await creditMemoQuery
            .Select(c => new { c.Id, c.CustomerId, c.Total, c.ExchangeRate })
            .ToListAsync(cancellationToken);
        foreach (var cm in creditMemos)
        {
            var openTxn = cm.Total - appliedByCreditMemo.GetValueOrDefault(cm.Id);
            if (openTxn <= 0) continue;
            items.Add(new OpenAgingItem(cm.CustomerId, asOfDate, -openTxn * cm.ExchangeRate));
        }

        // 未核销收款（负行=预收/超收现金，归 Current）
        var payments = await paymentQuery
            .Select(p => new { p.Id, p.PartyId, p.Amount, p.ExchangeRate })
            .ToListAsync(cancellationToken);
        foreach (var pay in payments)
        {
            var openTxn = pay.Amount - appliedByPayment.GetValueOrDefault(pay.Id);
            if (openTxn <= 0) continue;
            items.Add(new OpenAgingItem(pay.PartyId, asOfDate, -openTxn * pay.ExchangeRate));
        }

        var partyIds = items.Select(i => i.PartyId).Distinct().ToList();
        var names = await _customerRepository.AsNoTracking()
            .Where(c => partyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return BuildAging(asOfDate, items, names);
    }

    /// <param name="asOfDate">时点（date-only）</param>
    /// <param name="partyId">只算这一个供应商（null = 全部供应商）</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task<AgingReportDto> ComputeApAgingAsync(DateTime asOfDate, Guid? partyId, CancellationToken cancellationToken)
    {
        var appliedCutoff = asOfDate.AddDays(1);

        var billQuery = _billRepository.AsNoTracking()
            .Where(b => b.JournalEntryId != null && b.Status != FinanceDocumentStatus.Voided && b.DocDate <= asOfDate);
        var paymentQuery = _paymentRepository.AsNoTracking()
            .Where(p => p.JournalEntryId != null && p.Status != FinanceDocumentStatus.Voided
                     && p.Direction == PaymentDirection.Outbound && p.PartyType == FinancePartyType.Vendor
                     && p.DocDate <= asOfDate);

        IQueryable<Guid>? partyBillIds = null;
        IQueryable<Guid>? partyPaymentIds = null;
        if (partyId is { } id)
        {
            billQuery = billQuery.Where(b => b.VendorId == id);
            paymentQuery = paymentQuery.Where(p => p.PartyId == id);
            partyBillIds = _billRepository.AsNoTracking().Where(b => b.VendorId == id).Select(b => b.Id);
            partyPaymentIds = _paymentRepository.AsNoTracking().Where(p => p.PartyId == id).Select(p => p.Id);
        }

        var appliedToBill = await AppliedByTargetAsync(SettlementDocType.Bill, appliedCutoff, partyBillIds, cancellationToken);
        // 应付侧没有贷项源：单个往来方时只给收付款范围，贷项核销不参与聚合。
        var (appliedByPayment, _) = await AppliedBySourceAsync(appliedCutoff, partyPaymentIds, creditMemoIds: null, cancellationToken);

        var items = new List<OpenAgingItem>();

        // 未清账单（正行）
        var bills = await billQuery
            .Select(b => new { b.Id, b.VendorId, Due = b.DueDate ?? b.DocDate, b.Total, b.ExchangeRate })
            .ToListAsync(cancellationToken);
        foreach (var bill in bills)
        {
            var openTxn = bill.Total - appliedToBill.GetValueOrDefault(bill.Id);
            if (openTxn <= 0) continue;
            items.Add(new OpenAgingItem(bill.VendorId, bill.Due, openTxn * bill.ExchangeRate));
        }

        // 未核销付款（负行=预付/超付现金，归 Current）
        var payments = await paymentQuery
            .Select(p => new { p.Id, p.PartyId, p.Amount, p.ExchangeRate })
            .ToListAsync(cancellationToken);
        foreach (var pay in payments)
        {
            var openTxn = pay.Amount - appliedByPayment.GetValueOrDefault(pay.Id);
            if (openTxn <= 0) continue;
            items.Add(new OpenAgingItem(pay.PartyId, asOfDate, -openTxn * pay.ExchangeRate));
        }

        var partyIds = items.Select(i => i.PartyId).Distinct().ToList();
        var names = await _vendorRepository.AsNoTracking()
            .Where(v => partyIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Name })
            .ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken);

        return BuildAging(asOfDate, items, names);
    }

    /// <summary>时点已核销（按目标单据聚合）：application 记账时刻严格早于 appliedCutoff（= asOf 次日零点）</summary>
    /// <param name="targetType">目标单据类型</param>
    /// <param name="appliedCutoff">核销记账时刻的上界（不含）</param>
    /// <param name="targetIds">只聚合这些目标单据（数据库侧子查询；null = 全部）</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task<Dictionary<Guid, decimal>> AppliedByTargetAsync(
        SettlementDocType targetType, DateTime appliedCutoff, IQueryable<Guid>? targetIds, CancellationToken cancellationToken)
    {
        var query = _applicationRepository.AsNoTracking()
            .Where(a => a.TargetType == targetType && a.CreationTime < appliedCutoff);
        if (targetIds != null)
            query = query.Where(a => targetIds.Contains(a.TargetId));

        return await query
            .GroupBy(a => a.TargetId)
            .Select(g => new { TargetId = g.Key, Applied = g.Sum(x => x.AppliedAmount) })
            .ToDictionaryAsync(x => x.TargetId, x => x.Applied, cancellationToken);
    }

    /// <summary>时点已核销（按核销源聚合）：拆出收付款源与贷项源两张字典</summary>
    /// <param name="appliedCutoff">核销记账时刻的上界（不含）</param>
    /// <param name="paymentIds">只聚合这些收付款源（数据库侧子查询）</param>
    /// <param name="creditMemoIds">只聚合这些贷项源（数据库侧子查询）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <remarks>
    /// 两个范围都为 null = 全量。任一给出即进入限定模式：只聚合给出了范围的那几种源
    /// （应付侧没有贷项源，只给收付款范围）。
    /// </remarks>
    private async Task<(Dictionary<Guid, decimal> ByPayment, Dictionary<Guid, decimal> ByCreditMemo)> AppliedBySourceAsync(
        DateTime appliedCutoff, IQueryable<Guid>? paymentIds, IQueryable<Guid>? creditMemoIds, CancellationToken cancellationToken)
    {
        var query = _applicationRepository.AsNoTracking()
            .Where(a => a.CreationTime < appliedCutoff);
        if (paymentIds != null && creditMemoIds != null)
        {
            query = query.Where(a =>
                (a.SourceType == SettlementDocType.PaymentEntry && paymentIds.Contains(a.SourceId))
                || (a.SourceType == SettlementDocType.CreditMemo && creditMemoIds.Contains(a.SourceId)));
        }
        else if (paymentIds != null)
        {
            query = query.Where(a => a.SourceType == SettlementDocType.PaymentEntry && paymentIds.Contains(a.SourceId));
        }
        else if (creditMemoIds != null)
        {
            query = query.Where(a => a.SourceType == SettlementDocType.CreditMemo && creditMemoIds.Contains(a.SourceId));
        }

        var rows = await query
            .GroupBy(a => new { a.SourceType, a.SourceId })
            .Select(g => new { g.Key.SourceType, g.Key.SourceId, Applied = g.Sum(x => x.AppliedAmount) })
            .ToListAsync(cancellationToken);
        var byPayment = rows.Where(r => r.SourceType == SettlementDocType.PaymentEntry).ToDictionary(r => r.SourceId, r => r.Applied);
        var byCreditMemo = rows.Where(r => r.SourceType == SettlementDocType.CreditMemo).ToDictionary(r => r.SourceId, r => r.Applied);
        return (byPayment, byCreditMemo);
    }

    private sealed record OpenAgingItem(Guid PartyId, DateTime DueDate, decimal OutstandingBase);

    private AgingReportDto BuildAging(DateTime asOf, List<OpenAgingItem> items, Dictionary<Guid, string> names)
    {
        var report = new AgingReportDto { AsOf = asOf, BaseCurrency = _options.BaseCurrency };

        // 切分点来自配置（未配则 30/60/90）：北美惯例不是法律，按周结算的行业
        // 常用 7/14/21。桶数固定五档，只有切分点可配——桶数可变等于 DTO 形状可变。
        var cuts = _options.ResolveAgingBucketDays();
        var first = cuts[0];
        var second = cuts[1];
        var third = cuts[2];

        // 切分点随桶下发：呈现端（渲染器/CSV/前端）据此生成标签，不得写死 30/60/90
        report.Totals.AgingBucketDays = cuts;

        void AddToBucket(AgingBucketsDto buckets, int overdueDays, decimal amount)
        {
            if (overdueDays <= 0) buckets.Current += amount;
            else if (overdueDays <= first) buckets.Days1To30 += amount;
            else if (overdueDays <= second) buckets.Days31To60 += amount;
            else if (overdueDays <= third) buckets.Days61To90 += amount;
            else buckets.Over90 += amount;
            buckets.Total += amount;
        }

        foreach (var group in items.GroupBy(i => i.PartyId).OrderBy(g => names.GetValueOrDefault(g.Key)))
        {
            var row = new AgingRowDto
            {
                PartyId = group.Key,
                PartyName = names.GetValueOrDefault(group.Key) ?? group.Key.ToString(),
                AgingBucketDays = cuts
            };
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

        // 税维度行按（税率 × 来源单据类型 × 科目角色）单次聚合，归类见 ClassifyTaxLine：
        // 销项取贷方净额、进项取借方净额，归不进任一侧的税维度行不计入申报口径
        var sums = await PostedLines
            .Where(l => l.TaxRateId != null && l.PostingDate >= fromDate && l.PostingDate < toExclusive)
            .GroupBy(l => new { l.TaxRateId, l.JournalEntry!.SourceType, l.Account!.SystemRole })
            .Select(g => new
            {
                g.Key.TaxRateId,
                g.Key.SourceType,
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
            var ofRate = sums.Where(s => s.TaxRateId == rateId).ToList();
            var output = ofRate.Where(s => ClassifyTaxLine(s.SourceType, s.SystemRole) == TaxLineSide.Output)
                .Sum(s => s.Credit - s.Debit);
            var input = ofRate.Where(s => ClassifyTaxLine(s.SourceType, s.SystemRole) == TaxLineSide.Input)
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

    private enum TaxLineSide
    {
        None,
        Output,
        Input
    }

    /// <summary>
    /// 一条税维度行算销项还是进项。
    /// </summary>
    /// <remarks>
    /// ★ 本模块单据的税行按**来源单据类型**归类：发票 / 贷项是销项，账单 / 费用是进项（冲销沿用原来源类型，
    /// 方向随之一致）。过账那一刻它们落在当时持有 TaxPayable / TaxReceivable 角色的科目上，所以对没有迁移过
    /// 角色的数据，这与「按科目角色」逐分相同。区别只在角色迁移之后：科目表重组把税角色挪到新科目（受支持的操作），
    /// 按科目**当前**角色归类会让旧期间的税行全部落空，重跑汇总与申报销项进项都是 0，且不出现任何「未归入」行。
    /// 其余来源（手工凭证、消费方经 <c>ILedgerPostingService</c> 编程式过账）没有可依的单据类型，仍按科目当前角色归类。
    /// </remarks>
    private static TaxLineSide ClassifyTaxLine(string? sourceType, AccountSystemRole? role) => sourceType switch
    {
        FinanceSourceTypes.Invoice or FinanceSourceTypes.CreditMemo => TaxLineSide.Output,
        FinanceSourceTypes.Bill or FinanceSourceTypes.Expense => TaxLineSide.Input,
        _ => role switch
        {
            AccountSystemRole.TaxPayable => TaxLineSide.Output,
            AccountSystemRole.TaxReceivable => TaxLineSide.Input,
            _ => TaxLineSide.None
        }
    };

    public async Task<Result<CashFlowReportDto>> GetCashFlowAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Fail<CashFlowReportDto>("The 'to' date must not be earlier than the 'from' date.");

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        // 期初 + 期间借贷（读路径按开关走汇总桶或单次全账本条件求和）
        var sums = await _reader.SumOpeningAndPeriodByAccountAsync(fromDate, toExclusive, cancellationToken);
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
        // Structural tie-out (see GetBalanceSheetAsync): net cash flow must equal the
        // movement in cash accounts. A non-zero difference is an aggregation defect.
        if (Math.Abs(report.CheckDifference) > TieOutTolerance)
            Logger.LogWarning(
                "Cash flow statement does not tie out for {From}..{To}: net cash flow {Net} - cash movement {Movement} = {Diff}.",
                from, to, report.NetCashFlow, report.CashMovement, report.CheckDifference);

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

    public Task<Result<string>> ExportGeneralLedgerCsvAsync(Guid accountId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => _ledgerReader.ExportGeneralLedgerCsvAsync(accountId, from, to, cancellationToken);

    public Task<Result<string>> ExportArAgingCsvAsync(DateTime asOf, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetArAgingAsync(asOf, cancellationToken), r => ReportCsvWriter.Aging(r, _options.ResolveAgingBucketDays()));

    public Task<Result<string>> ExportApAgingCsvAsync(DateTime asOf, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetApAgingAsync(asOf, cancellationToken), r => ReportCsvWriter.Aging(r, _options.ResolveAgingBucketDays()));

    public Task<Result<string>> ExportTaxSummaryCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetTaxSummaryAsync(from, to, cancellationToken), ReportCsvWriter.TaxSummary);

    public Task<Result<string>> ExportCashFlowCsvAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => ToCsvAsync(GetCashFlowAsync(from, to, cancellationToken), ReportCsvWriter.CashFlow);
}
