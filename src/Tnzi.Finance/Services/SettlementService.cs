namespace Tnzi.Finance.Services;

/// <summary>
/// 结算服务
/// </summary>
/// <remarks>
/// 核销本身不产生 GL 分录（两侧单据已各自过账进同一控制科目）；唯一例外是
/// realized FX：源与目标捕获汇率不同的外币核销，把控制科目上的折算残差
/// 调整到汇兑损益（ExchangeGainLoss 角色），撤销核销时冲销该凭证。
/// 并发防护：源与目标单据实现 IConcurrencyStamp，并发双核销的冲突方整体回滚 409。
/// </remarks>
public class SettlementService : ApplicationService, ISettlementService
{
    private readonly IRepository<PaymentApplication, Guid> _applicationRepository;
    private readonly IRepository<Invoice, Guid> _invoiceRepository;
    private readonly IRepository<Bill, Guid> _billRepository;
    private readonly IRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IRepository<CreditMemo, Guid> _creditMemoRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceDocumentHelper _helper;

    public SettlementService(
        IServiceProvider serviceProvider,
        IRepository<PaymentApplication, Guid> applicationRepository,
        IRepository<Invoice, Guid> invoiceRepository,
        IRepository<Bill, Guid> billRepository,
        IRepository<PaymentEntry, Guid> paymentRepository,
        IRepository<CreditMemo, Guid> creditMemoRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        LedgerPostingEngine engine,
        FinanceDocumentHelper helper)
        : base(serviceProvider)
    {
        _applicationRepository = Check.NotNull(applicationRepository);
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _billRepository = Check.NotNull(billRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _creditMemoRepository = Check.NotNull(creditMemoRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _engine = Check.NotNull(engine);
        _helper = Check.NotNull(helper);
    }

    public async Task<Result<List<PaymentApplicationDto>>> GetApplicationsAsync(SettlementDocType docType, Guid docId, CancellationToken cancellationToken = default)
    {
        var applications = await _applicationRepository.AsNoTracking()
            .Where(a => (a.SourceType == docType && a.SourceId == docId) ||
                        (a.TargetType == docType && a.TargetId == docId))
            .OrderBy(a => a.CreationTime)
            .ToListAsync(cancellationToken);

        var dtos = applications.Select(a => a.MapTo<PaymentApplicationDto>()).ToList();
        await FillNumbersAsync(dtos, cancellationToken);
        return Ok(dtos);
    }

    public async Task<Result<List<OpenDocumentDto>>> GetOpenDocumentsAsync(FinancePartyType partyType, Guid partyId, CancellationToken cancellationToken = default)
    {
        var open = new List<OpenDocumentDto>();

        if (partyType == FinancePartyType.Customer)
        {
            open.AddRange(await _invoiceRepository.AsNoTracking()
                .Where(i => i.CustomerId == partyId &&
                            (i.Status == FinanceDocumentStatus.Posted || i.Status == FinanceDocumentStatus.PartiallyPaid) &&
                            i.AppliedTotal < i.Total)
                .OrderBy(i => i.DocDate)
                .Select(i => new OpenDocumentDto
                {
                    DocType = SettlementDocType.Invoice,
                    DocId = i.Id,
                    Number = i.Number,
                    DocDate = i.DocDate,
                    DueDate = i.DueDate,
                    Currency = i.Currency,
                    Total = i.Total,
                    AppliedTotal = i.AppliedTotal
                })
                .ToListAsync(cancellationToken));
        }
        else
        {
            open.AddRange(await _billRepository.AsNoTracking()
                .Where(b => b.VendorId == partyId &&
                            (b.Status == FinanceDocumentStatus.Posted || b.Status == FinanceDocumentStatus.PartiallyPaid) &&
                            b.AppliedTotal < b.Total)
                .OrderBy(b => b.DocDate)
                .Select(b => new OpenDocumentDto
                {
                    DocType = SettlementDocType.Bill,
                    DocId = b.Id,
                    Number = b.Number,
                    DocDate = b.DocDate,
                    DueDate = b.DueDate,
                    Currency = b.Currency,
                    Total = b.Total,
                    AppliedTotal = b.AppliedTotal
                })
                .ToListAsync(cancellationToken));
        }

        return Ok(open);
    }

    public async Task<Result<List<PaymentApplicationDto>>> ApplyAsync(ApplySettlementDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (input.Targets == null || input.Targets.Count == 0)
            return Fail<List<PaymentApplicationDto>>("At least one target allocation is required.");
        if (input.Targets.Any(t => t.Amount <= 0))
            return Fail<List<PaymentApplicationDto>>("Allocation amounts must be greater than zero.");
        if (input.Targets.GroupBy(t => (t.TargetType, t.TargetId)).Any(g => g.Count() > 1))
            return Fail<List<PaymentApplicationDto>>("Duplicate targets in one application.");

        // 源加载与规则校验
        var sourceResult = await LoadSourceAsync(input.SourceType, input.SourceId, cancellationToken);
        if (!sourceResult.Succeeded)
            return Fail<List<PaymentApplicationDto>>(sourceResult.Message!, sourceResult.Code ?? 400);
        var source = sourceResult.Data!;

        var allocateTotal = input.Targets.Sum(t => t.Amount);
        if (allocateTotal > source.Remaining)
            return Fail<List<PaymentApplicationDto>>($"Allocation {allocateTotal} exceeds the source's remaining amount {source.Remaining}.", 400);

        var applications = new List<PaymentApplication>();
        Result applyResult;
        try
        {
            applyResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                foreach (var allocation in input.Targets)
                {
                    if (allocation.TargetType != source.AllowedTargetType)
                        return Result.Failure($"A {input.SourceType} can only be applied to {source.AllowedTargetType} documents.", 400);

                    var targetResult = await LoadTargetAsync(allocation.TargetType, allocation.TargetId, ct);
                    if (!targetResult.Succeeded)
                        return Result.Failure(targetResult.Message!, targetResult.Code ?? 400);
                    var target = targetResult.Data!;

                    if (!string.Equals(target.Currency, source.Currency, StringComparison.OrdinalIgnoreCase))
                        return Result.Failure($"Currency mismatch: source is {source.Currency}, target {target.Number} is {target.Currency}. Cross-currency settlement is not supported.", 400);
                    if (target.PartyId != source.PartyId)
                        return Result.Failure($"Target {target.Number} belongs to a different party.", 400);
                    if (allocation.Amount > target.Outstanding)
                        return Result.Failure($"Allocation {allocation.Amount} exceeds the outstanding {target.Outstanding} of {target.Number}.", 400);

                    var application = new PaymentApplication
                    {
                        SourceType = input.SourceType,
                        SourceId = input.SourceId,
                        TargetType = allocation.TargetType,
                        TargetId = allocation.TargetId,
                        AppliedAmount = allocation.Amount
                    };

                    // realized FX：同交易币但捕获汇率不同 → 控制科目残差调整到汇兑损益
                    var fxResult = await PostRealizedFxAsync(source, target, allocation.Amount, ct);
                    if (!fxResult.Succeeded)
                        return Result.Failure(fxResult.Message!, fxResult.Code ?? 400);
                    application.RealizedFxJournalEntryId = fxResult.Data;

                    await _applicationRepository.InsertAsync(application, ct);
                    applications.Add(application);

                    await ApplyToTargetAsync(target, allocation.Amount, ct);
                }

                await ApplyToSourceAsync(source, allocateTotal, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<List<PaymentApplicationDto>>("A document was modified by another operation (concurrent settlement). Reload and retry.", 409);
        }

        if (!applyResult.Succeeded)
            return Fail<List<PaymentApplicationDto>>(applyResult.Message ?? "Settlement failed.", applyResult.Code ?? 400);

        var dtos = applications.Select(a => a.MapTo<PaymentApplicationDto>()).ToList();
        await FillNumbersAsync(dtos, cancellationToken);
        return Ok(dtos);
    }

    public async Task<Result> UnapplyAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _applicationRepository.AsQueryable(true)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        if (application == null)
            return Fail("Application not found.", 404);

        Result unapplyResult;
        try
        {
            unapplyResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var sourceResult = await LoadSourceAsync(application.SourceType, application.SourceId, ct);
                if (!sourceResult.Succeeded)
                    return Result.Failure(sourceResult.Message!, sourceResult.Code ?? 400);
                var targetResult = await LoadTargetAsync(application.TargetType, application.TargetId, ct);
                if (!targetResult.Succeeded)
                    return Result.Failure(targetResult.Message!, targetResult.Code ?? 400);

                // 冲销 realized FX 凭证
                if (application.RealizedFxJournalEntryId.HasValue)
                {
                    var fxEntry = await _entryRepository.AsQueryable(true)
                        .Include(e => e.Lines)
                        .FirstOrDefaultAsync(e => e.Id == application.RealizedFxJournalEntryId.Value, ct);
                    if (fxEntry != null && fxEntry.Status == JournalEntryStatus.Posted)
                    {
                        var reversalResult = await _engine.BuildReversalAsync(fxEntry, fxEntry.PostingDate, "Unapply settlement", ct);
                        if (!reversalResult.Succeeded)
                            return Result.Failure(reversalResult.Message ?? "FX reversal failed.", reversalResult.Code ?? 400);

                        await _entryRepository.InsertAsync(reversalResult.Data!, ct);
                        fxEntry.Status = JournalEntryStatus.Reversed;
                        fxEntry.ReversedByEntryId = reversalResult.Data!.Id;
                        await _entryRepository.UpdateAsync(fxEntry, ct);
                    }
                }

                await ApplyToTargetAsync(targetResult.Data!, -application.AppliedAmount, ct);
                await ApplyToSourceAsync(sourceResult.Data!, -application.AppliedAmount, ct);
                await _applicationRepository.DeleteAsync(application, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("A document was modified by another operation. Reload and retry.", 409);
        }

        return unapplyResult.Succeeded
            ? Ok()
            : Fail(unapplyResult.Message ?? "Unapply failed.", unapplyResult.Code ?? 400);
    }

    // ── 内部：源/目标的统一视图 ─────────────────────────────────

    private sealed class SourceView
    {
        public required object Entity { get; init; }
        public required DateTime DocDate { get; init; }
        public required string Currency { get; init; }
        public required decimal ExchangeRate { get; init; }
        public required decimal Remaining { get; init; }
        public required Guid PartyId { get; init; }
        public required SettlementDocType AllowedTargetType { get; init; }
        public required FinancePartyType PartyType { get; init; }
        public string? Number { get; init; }
    }

    private sealed class TargetView
    {
        public required object Entity { get; init; }
        public required DateTime DocDate { get; init; }
        public required SettlementDocType DocType { get; init; }
        public required string Currency { get; init; }
        public required decimal ExchangeRate { get; init; }
        public required decimal Outstanding { get; init; }
        public required Guid PartyId { get; init; }
        public string? Number { get; init; }
    }

    private async Task<Result<SourceView>> LoadSourceAsync(SettlementDocType type, Guid id, CancellationToken ct)
    {
        switch (type)
        {
            case SettlementDocType.PaymentEntry:
            {
                var payment = await _paymentRepository.AsQueryable(true).FirstOrDefaultAsync(p => p.Id == id, ct);
                if (payment == null)
                    return Fail<SourceView>("Payment not found.", 404);
                if (payment.Status != FinanceDocumentStatus.Posted)
                    return Fail<SourceView>("Only posted payments can be applied.", 409);

                return Ok(new SourceView
                {
                    Entity = payment,
                    DocDate = payment.DocDate,
                    Currency = payment.Currency,
                    ExchangeRate = payment.ExchangeRate,
                    Remaining = payment.Amount - payment.AppliedTotal,
                    PartyId = payment.PartyId,
                    PartyType = payment.PartyType,
                    AllowedTargetType = payment.Direction == PaymentDirection.Inbound ? SettlementDocType.Invoice : SettlementDocType.Bill,
                    Number = payment.Number
                });
            }
            case SettlementDocType.CreditMemo:
            {
                var memo = await _creditMemoRepository.AsQueryable(true).FirstOrDefaultAsync(m => m.Id == id, ct);
                if (memo == null)
                    return Fail<SourceView>("Credit memo not found.", 404);
                if (memo.Status is not (FinanceDocumentStatus.Posted or FinanceDocumentStatus.PartiallyPaid))
                    return Fail<SourceView>("Only posted credit memos can be applied.", 409);

                return Ok(new SourceView
                {
                    Entity = memo,
                    DocDate = memo.DocDate,
                    Currency = memo.Currency,
                    ExchangeRate = memo.ExchangeRate,
                    Remaining = memo.Total - memo.AppliedTotal,
                    PartyId = memo.CustomerId,
                    PartyType = FinancePartyType.Customer,
                    AllowedTargetType = SettlementDocType.Invoice,
                    Number = memo.Number
                });
            }
            default:
                return Fail<SourceView>("The source must be a payment entry or a credit memo.", 400);
        }
    }

    private async Task<Result<TargetView>> LoadTargetAsync(SettlementDocType type, Guid id, CancellationToken ct)
    {
        switch (type)
        {
            case SettlementDocType.Invoice:
            {
                var invoice = await _invoiceRepository.AsQueryable(true).FirstOrDefaultAsync(i => i.Id == id, ct);
                if (invoice == null)
                    return Fail<TargetView>("Invoice not found.", 404);
                if (invoice.Status is not (FinanceDocumentStatus.Posted or FinanceDocumentStatus.PartiallyPaid or FinanceDocumentStatus.Paid))
                    return Fail<TargetView>($"Invoice {invoice.Number} is not open.", 409);

                return Ok(new TargetView
                {
                    Entity = invoice,
                    DocDate = invoice.DocDate,
                    DocType = type,
                    Currency = invoice.Currency,
                    ExchangeRate = invoice.ExchangeRate,
                    Outstanding = invoice.Total - invoice.AppliedTotal,
                    PartyId = invoice.CustomerId,
                    Number = invoice.Number
                });
            }
            case SettlementDocType.Bill:
            {
                var bill = await _billRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == id, ct);
                if (bill == null)
                    return Fail<TargetView>("Bill not found.", 404);
                if (bill.Status is not (FinanceDocumentStatus.Posted or FinanceDocumentStatus.PartiallyPaid or FinanceDocumentStatus.Paid))
                    return Fail<TargetView>($"Bill {bill.Number} is not open.", 409);

                return Ok(new TargetView
                {
                    Entity = bill,
                    DocDate = bill.DocDate,
                    DocType = type,
                    Currency = bill.Currency,
                    ExchangeRate = bill.ExchangeRate,
                    Outstanding = bill.Total - bill.AppliedTotal,
                    PartyId = bill.VendorId,
                    Number = bill.Number
                });
            }
            default:
                return Fail<TargetView>("The target must be an invoice or a bill.", 400);
        }
    }

    private async Task ApplyToTargetAsync(TargetView target, decimal delta, CancellationToken ct)
    {
        switch (target.Entity)
        {
            case Invoice invoice:
                invoice.AppliedTotal += delta;
                invoice.Status = invoice.AppliedTotal <= 0
                    ? FinanceDocumentStatus.Posted
                    : invoice.AppliedTotal >= invoice.Total ? FinanceDocumentStatus.Paid : FinanceDocumentStatus.PartiallyPaid;
                await _invoiceRepository.UpdateAsync(invoice, ct);
                break;
            case Bill bill:
                bill.AppliedTotal += delta;
                bill.Status = bill.AppliedTotal <= 0
                    ? FinanceDocumentStatus.Posted
                    : bill.AppliedTotal >= bill.Total ? FinanceDocumentStatus.Paid : FinanceDocumentStatus.PartiallyPaid;
                await _billRepository.UpdateAsync(bill, ct);
                break;
        }
    }

    private async Task ApplyToSourceAsync(SourceView source, decimal delta, CancellationToken ct)
    {
        switch (source.Entity)
        {
            case PaymentEntry payment:
                payment.AppliedTotal += delta;
                await _paymentRepository.UpdateAsync(payment, ct);
                break;
            case CreditMemo memo:
                memo.AppliedTotal += delta;
                memo.Status = memo.AppliedTotal <= 0
                    ? FinanceDocumentStatus.Posted
                    : memo.AppliedTotal >= memo.Total ? FinanceDocumentStatus.Paid : FinanceDocumentStatus.PartiallyPaid;
                await _creditMemoRepository.UpdateAsync(memo, ct);
                break;
        }
    }

    /// <summary>
    /// realized FX：同交易币、汇率不同的核销，把控制科目（AR/AP）的本位币残差调整到汇兑损益。
    /// 返回 FX 凭证 Id（无差异时为 null）
    /// </summary>
    private async Task<Result<Guid?>> PostRealizedFxAsync(SourceView source, TargetView target, decimal amount, CancellationToken ct)
    {
        var targetBase = _helper.Round(amount * target.ExchangeRate);
        var sourceBase = _helper.Round(amount * source.ExchangeRate);
        var residual = targetBase - sourceBase;
        if (residual == 0)
            return Result<Guid?>.Success(null);

        var controlRole = target.DocType == SettlementDocType.Invoice
            ? AccountSystemRole.AccountsReceivable
            : AccountSystemRole.AccountsPayable;
        var controlResult = await _helper.ResolveSystemAccountAsync(controlRole, ct);
        if (!controlResult.Succeeded)
            return Result<Guid?>.Failure(controlResult.Message!, controlResult.Code ?? 400);
        var fxResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.ExchangeGainLoss, ct);
        if (!fxResult.Succeeded)
            return Result<Guid?>.Failure(fxResult.Message!, fxResult.Code ?? 400);

        // AR：目标(发票)已借 targetBase，源(收款)已贷 sourceBase → 残差留在 AR 借方(residual>0)或贷方
        // AP 镜像：残差方向相反。凭证以本位币记账（TxnDebit/TxnCredit 即本位币金额）。
        var isAr = controlRole == AccountSystemRole.AccountsReceivable;
        var controlCredit = isAr ? residual : -residual;

        var entry = new JournalEntry
        {
            Status = JournalEntryStatus.Draft,
            // 汇兑损益实现于结算涉及的后一事件：记入两单据较晚的记账日，
            // 回溯核销不会把损益归入"今天"的期间
            PostingDate = source.DocDate > target.DocDate ? source.DocDate : target.DocDate,
            Memo = $"Realized FX on settlement of {target.Number}",
            Currency = _helper.NormalizeCurrency(null),
            ExchangeRate = 1m,
            SourceType = nameof(PaymentApplication),
            SourceId = $"{source.Number}->{target.Number}"
        };

        entry.Lines.Add(new JournalLine
        {
            LineNumber = 1,
            AccountId = controlResult.Data!.Id,
            TxnDebit = controlCredit < 0 ? -controlCredit : 0,
            TxnCredit = controlCredit > 0 ? controlCredit : 0,
            Currency = entry.Currency,
            PartyType = source.PartyType == FinancePartyType.Customer ? nameof(Customer) : nameof(Vendor),
            PartyId = source.PartyId.ToString()
        });
        entry.Lines.Add(new JournalLine
        {
            LineNumber = 2,
            AccountId = fxResult.Data!.Id,
            TxnDebit = controlCredit > 0 ? controlCredit : 0,
            TxnCredit = controlCredit < 0 ? -controlCredit : 0,
            Currency = entry.Currency
        });

        var engineResult = await _engine.PostAsync(entry, ct);
        if (!engineResult.Succeeded)
            return Result<Guid?>.Failure(engineResult.Message ?? "FX posting failed.", engineResult.Code ?? 400);

        await _entryRepository.InsertAsync(entry, ct);
        return Result<Guid?>.Success(entry.Id);
    }

    /// <summary>
    /// 批量补齐核销记录两侧的单据编号（展示用）
    /// </summary>
    private async Task FillNumbersAsync(List<PaymentApplicationDto> dtos, CancellationToken ct)
    {
        async Task<Dictionary<Guid, string?>> NumbersAsync(SettlementDocType type, List<Guid> ids)
        {
            if (ids.Count == 0)
                return new Dictionary<Guid, string?>();
            return type switch
            {
                SettlementDocType.Invoice => await _invoiceRepository.AsNoTracking().Where(d => ids.Contains(d.Id))
                    .Select(d => new { d.Id, d.Number }).ToDictionaryAsync(d => d.Id, d => d.Number, ct),
                SettlementDocType.Bill => await _billRepository.AsNoTracking().Where(d => ids.Contains(d.Id))
                    .Select(d => new { d.Id, d.Number }).ToDictionaryAsync(d => d.Id, d => d.Number, ct),
                SettlementDocType.PaymentEntry => await _paymentRepository.AsNoTracking().Where(d => ids.Contains(d.Id))
                    .Select(d => new { d.Id, d.Number }).ToDictionaryAsync(d => d.Id, d => d.Number, ct),
                SettlementDocType.CreditMemo => await _creditMemoRepository.AsNoTracking().Where(d => ids.Contains(d.Id))
                    .Select(d => new { d.Id, d.Number }).ToDictionaryAsync(d => d.Id, d => d.Number, ct),
                _ => new Dictionary<Guid, string?>()
            };
        }

        foreach (var group in dtos.GroupBy(d => d.SourceType))
        {
            var numbers = await NumbersAsync(group.Key, group.Select(g => g.SourceId).Distinct().ToList());
            foreach (var dto in group)
                dto.SourceNumber = numbers.GetValueOrDefault(dto.SourceId);
        }

        foreach (var group in dtos.GroupBy(d => d.TargetType))
        {
            var numbers = await NumbersAsync(group.Key, group.Select(g => g.TargetId).Distinct().ToList());
            foreach (var dto in group)
                dto.TargetNumber = numbers.GetValueOrDefault(dto.TargetId);
        }
    }
}
