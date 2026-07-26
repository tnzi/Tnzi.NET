namespace Tnzi.Finance.Services;

/// <summary>
/// 往来方账面视图服务
/// </summary>
public class PartyLedgerService : ApplicationService, IPartyLedgerService
{
    private readonly IFinancialReportService _reportService;
    private readonly IReadOnlyRepository<Invoice, Guid> _invoiceRepository;
    private readonly IReadOnlyRepository<CreditMemo, Guid> _creditMemoRepository;
    private readonly IReadOnlyRepository<Bill, Guid> _billRepository;
    private readonly IReadOnlyRepository<Expense, Guid> _expenseRepository;
    private readonly IReadOnlyRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly FinanceOptions _options;

    public PartyLedgerService(
        IServiceProvider serviceProvider,
        IFinancialReportService reportService,
        IReadOnlyRepository<Invoice, Guid> invoiceRepository,
        IReadOnlyRepository<CreditMemo, Guid> creditMemoRepository,
        IReadOnlyRepository<Bill, Guid> billRepository,
        IReadOnlyRepository<Expense, Guid> expenseRepository,
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _reportService = Check.NotNull(reportService);
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _creditMemoRepository = Check.NotNull(creditMemoRepository);
        _billRepository = Check.NotNull(billRepository);
        _expenseRepository = Check.NotNull(expenseRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<PartyLedgerSummaryDto>> GetSummaryAsync(
        FinancePartyType partyType,
        Guid partyId,
        DateTime? asOf = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var name = await ResolveNameAsync(partyType, partyId, cancellationToken);
        if (name == null)
            return Fail<PartyLedgerSummaryDto>("Party not found.", 404);

        var asOfDate = (asOf ?? DateTime.UtcNow).ToUtcDate();
        var periodTo = (to ?? asOfDate).ToUtcDate();
        var periodFrom = (from ?? new DateTime(periodTo.Year, 1, 1)).ToUtcDate();

        // 未清与分桶直接取账龄的那一行：这是"欠多少"的唯一权威口径（时点已核销重建 +
        // 未核销收付款/贷项的负行 + 与 GL 控制科目 tie-out）。在这里另写一遍必然漂移。
        var aging = partyType == FinancePartyType.Customer
            ? await _reportService.GetArAgingAsync(asOfDate, cancellationToken)
            : await _reportService.GetApAgingAsync(asOfDate, cancellationToken);
        if (!aging.Succeeded)
            return Fail<PartyLedgerSummaryDto>(aging.Message!, aging.Code ?? 400);

        var row = aging.Data!.Rows.FirstOrDefault(r => r.PartyId == partyId);

        var summary = new PartyLedgerSummaryDto
        {
            PartyId = partyId,
            PartyName = name,
            PartyType = partyType,
            BaseCurrency = _options.BaseCurrency,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            OpenBalance = row?.Total ?? 0m,
            // 逾期 = 非 Current 桶之和。用减法而不是逐桶相加：桶的划分将来会参数化
            // （见架构契约 §2.1），减法对分桶方案免疫。
            Overdue = (row?.Total ?? 0m) - (row?.Current ?? 0m),
            Buckets = row == null
                ? new AgingBucketsDto()
                : new AgingBucketsDto
                {
                    Current = row.Current,
                    Days1To30 = row.Days1To30,
                    Days31To60 = row.Days31To60,
                    Days61To90 = row.Days61To90,
                    Over90 = row.Over90,
                    Total = row.Total,
                },
        };

        summary.PeriodTotal = await PeriodTotalAsync(partyType, partyId, periodFrom, periodTo, cancellationToken);

        var entries = await LoadEntriesAsync(partyType, partyId, null, null, cancellationToken);
        summary.OpenDocumentCount = entries.Count(e => e.Outstanding > 0);
        summary.LastTransactionDate = entries.Count == 0 ? null : entries.Max(e => e.DocDate);

        return Ok(summary);
    }

    public async Task<Result<IPagedList<PartyLedgerEntryDto>>> GetTransactionsAsync(
        FinancePartyType partyType,
        Guid partyId,
        PartyLedgerQueryDto query,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var entries = await LoadEntriesAsync(partyType, partyId, query.From, query.To, cancellationToken);
        if (query.OpenOnly)
            entries = entries.Where(e => e.Outstanding > 0).ToList();

        // 排序：日期倒序（最近的在最上，网银式），同日按单据号稳定收敛，
        // 否则同日多张单在翻页时会互相换位。
        var ordered = entries
            .OrderByDescending(e => e.DocDate)
            .ThenByDescending(e => e.Number ?? string.Empty)
            .ThenBy(e => e.DocId)
            .ToList();

        var pageIndex = query.PageIndex < 1 ? 1 : query.PageIndex;
        var pageSize = query.PageSize < 1 ? 20 : query.PageSize;
        var page = ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();

        return Ok<IPagedList<PartyLedgerEntryDto>>(
            new PagedList<PartyLedgerEntryDto>(page, pageIndex, pageSize, ordered.Count));
    }

    private async Task<string?> ResolveNameAsync(FinancePartyType partyType, Guid partyId, CancellationToken cancellationToken)
        => partyType == FinancePartyType.Customer
            ? await _customerRepository.AsNoTracking().Where(c => c.Id == partyId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : await _vendorRepository.AsNoTracking().Where(v => v.Id == partyId).Select(v => v.Name).FirstOrDefaultAsync(cancellationToken);

    /// <summary>期间发生额（已过账单据的本位币合计，作废的不计）。</summary>
    private async Task<decimal> PeriodTotalAsync(
        FinancePartyType partyType, Guid partyId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var toExclusive = to.AddDays(1);

        if (partyType == FinancePartyType.Customer)
        {
            return await _invoiceRepository.AsNoTracking()
                .Where(i => i.CustomerId == partyId && i.JournalEntryId != null
                            && i.Status != FinanceDocumentStatus.Voided
                            && i.DocDate >= from && i.DocDate < toExclusive)
                .SumAsync(i => (decimal?)i.BaseTotal, cancellationToken) ?? 0m;
        }

        var bills = await _billRepository.AsNoTracking()
            .Where(b => b.VendorId == partyId && b.JournalEntryId != null
                        && b.Status != FinanceDocumentStatus.Voided
                        && b.DocDate >= from && b.DocDate < toExclusive)
            .SumAsync(b => (decimal?)b.BaseTotal, cancellationToken) ?? 0m;

        var expenses = await _expenseRepository.AsNoTracking()
            .Where(e => e.VendorId == partyId && e.JournalEntryId != null
                        && e.Status != FinanceDocumentStatus.Voided
                        && e.DocDate >= from && e.DocDate < toExclusive)
            .SumAsync(e => (decimal?)e.BaseTotal, cancellationToken) ?? 0m;

        return bills + expenses;
    }

    /// <summary>
    /// 把该往来方的各类单据铺成一条统一流水。
    /// </summary>
    /// <remarks>
    /// 日期区间下推 SQL，跨类型的合并与排序在内存完成（几种单据表各查一次，无法用一条
    /// SQL 表达且不值得为此建视图）。规模按**单个往来方**的单据数计，不是全库。
    /// </remarks>
    private async Task<List<PartyLedgerEntryDto>> LoadEntriesAsync(
        FinancePartyType partyType, Guid partyId, DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        var fromDate = from?.ToUtcDate();
        var toExclusive = to?.ToUtcDate().AddDays(1);
        var today = DateTime.UtcNow.ToUtcDate();

        var entries = new List<PartyLedgerEntryDto>();

        if (partyType == FinancePartyType.Customer)
        {
            var invoices = await _invoiceRepository.AsNoTracking()
                .Where(i => i.CustomerId == partyId && i.Status != FinanceDocumentStatus.Draft
                            && (fromDate == null || i.DocDate >= fromDate)
                            && (toExclusive == null || i.DocDate < toExclusive))
                .Select(i => new { i.Id, i.Number, i.DocDate, i.DueDate, i.Currency, i.Total, i.AppliedTotal, i.Status })
                .ToListAsync(cancellationToken);
            entries.AddRange(invoices.Select(i => Entry(
                FinanceSourceTypes.Invoice, i.Id, i.Number, i.DocDate, i.DueDate, i.Currency,
                i.Total, i.Status == FinanceDocumentStatus.Voided ? 0m : i.Total - i.AppliedTotal, i.Status, today)));

            var memos = await _creditMemoRepository.AsNoTracking()
                .Where(c => c.CustomerId == partyId && c.Status != FinanceDocumentStatus.Draft
                            && (fromDate == null || c.DocDate >= fromDate)
                            && (toExclusive == null || c.DocDate < toExclusive))
                .Select(c => new { c.Id, c.Number, c.DocDate, c.Currency, c.Total, c.Status })
                .ToListAsync(cancellationToken);
            entries.AddRange(memos.Select(c => Entry(
                FinanceSourceTypes.CreditMemo, c.Id, c.Number, c.DocDate, null, c.Currency,
                -c.Total, 0m, c.Status, today)));
        }
        else
        {
            var bills = await _billRepository.AsNoTracking()
                .Where(b => b.VendorId == partyId && b.Status != FinanceDocumentStatus.Draft
                            && (fromDate == null || b.DocDate >= fromDate)
                            && (toExclusive == null || b.DocDate < toExclusive))
                .Select(b => new { b.Id, b.Number, b.DocDate, b.DueDate, b.Currency, b.Total, b.AppliedTotal, b.Status })
                .ToListAsync(cancellationToken);
            entries.AddRange(bills.Select(b => Entry(
                FinanceSourceTypes.Bill, b.Id, b.Number, b.DocDate, b.DueDate, b.Currency,
                b.Total, b.Status == FinanceDocumentStatus.Voided ? 0m : b.Total - b.AppliedTotal, b.Status, today)));

            var expenses = await _expenseRepository.AsNoTracking()
                .Where(e => e.VendorId == partyId && e.Status != FinanceDocumentStatus.Draft
                            && (fromDate == null || e.DocDate >= fromDate)
                            && (toExclusive == null || e.DocDate < toExclusive))
                .Select(e => new { e.Id, e.Number, e.DocDate, e.Currency, e.Total, e.Status })
                .ToListAsync(cancellationToken);
            entries.AddRange(expenses.Select(e => Entry(
                FinanceSourceTypes.Expense, e.Id, e.Number, e.DocDate, null, e.Currency,
                e.Total, 0m, e.Status, today)));
        }

        // 收付款：减少对方欠款，故取负号（客户收款与供应商付款同理）
        var payments = await _paymentRepository.AsNoTracking()
            .Where(p => p.PartyId == partyId && p.PartyType == partyType && p.Status != FinanceDocumentStatus.Draft
                        && (fromDate == null || p.DocDate >= fromDate)
                        && (toExclusive == null || p.DocDate < toExclusive))
            .Select(p => new { p.Id, p.Number, p.DocDate, p.Currency, p.Amount, p.Status })
            .ToListAsync(cancellationToken);
        entries.AddRange(payments.Select(p => Entry(
            FinanceSourceTypes.PaymentEntry, p.Id, p.Number, p.DocDate, null, p.Currency,
            -p.Amount, 0m, p.Status, today)));

        return entries;
    }

    private static PartyLedgerEntryDto Entry(
        string docType, Guid id, string? number, DateTime docDate, DateTime? dueDate,
        string currency, decimal amount, decimal outstanding, FinanceDocumentStatus status, DateTime today)
        => new()
        {
            DocType = docType,
            DocId = id,
            Number = number,
            DocDate = docDate,
            DueDate = dueDate,
            Currency = currency,
            Amount = amount,
            Outstanding = outstanding,
            Status = status,
            // 逾期只对"还欠着的"有意义：付清的单据即便当初拖过期，今天也不是逾期。
            OverdueDays = outstanding > 0 && dueDate != null && dueDate.Value < today
                ? (int)(today - dueDate.Value).TotalDays
                : 0,
        };
}
