namespace Tnzi.Finance.Services;

/// <summary>
/// 收付款单服务
/// </summary>
/// <remarks>
/// 过账规则：Inbound（收款）借 存入科目（显式指定，或按 PostToUndepositedFunds 回退待存款项角色）、
/// 贷 应收账款（AR 角色）；Outbound（付款）借 应付账款（AP 角色）、贷 付出科目（必填）。
/// 与单据的核销独立于 GL（P2c 结算服务）。
/// </remarks>
public class PaymentEntryService : ApplicationService, IPaymentEntryService
{
    private readonly IRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceDocumentHelper _helper;
    private readonly PostingGuardRunner _guards;
    private readonly FinanceOptions _options;

    public PaymentEntryService(
        IServiceProvider serviceProvider,
        IRepository<PaymentEntry, Guid> paymentRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IDocumentNumberService numberService,
        LedgerPostingEngine engine,
        FinanceDocumentHelper helper,
        PostingGuardRunner guards,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _numberService = Check.NotNull(numberService);
        _engine = Check.NotNull(engine);
        _helper = Check.NotNull(helper);
        _guards = Check.NotNull(guards);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<PaymentEntryDto>>> GetPagedAsync(PaymentEntryQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _paymentRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(p => p.CreationTime)
            .ProjectTo<PaymentEntry, PaymentEntryDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        // 往来方名称补齐（客户/供应商两表，批量）
        await FillPartyNamesAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<PaymentEntryDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment == null)
            return Fail<PaymentEntryDto>("Payment not found.", 404);

        var dto = payment.MapTo<PaymentEntryDto>();
        await FillPartyNamesAsync(new List<PaymentEntryDto> { dto }, cancellationToken);
        return Ok(dto);
    }

    public async Task<Result<PaymentEntryDto>> CreateDraftAsync(CreatePaymentEntryDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var payment = new PaymentEntry();
        var applyResult = await ApplyDraftAsync(payment, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<PaymentEntryDto>(applyResult.Message ?? "Invalid payment.", applyResult.Code ?? 400);

        await _paymentRepository.InsertAsync(payment, cancellationToken);
        await _paymentRepository.SaveChangesAsync(cancellationToken);

        return await GetAsync(payment.Id, cancellationToken);
    }

    public async Task<Result<PaymentEntryDto>> UpdateDraftAsync(Guid id, CreatePaymentEntryDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var payment = await _paymentRepository.AsQueryable(true)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment == null)
            return Fail<PaymentEntryDto>("Payment not found.", 404);
        if (payment.Status != FinanceDocumentStatus.Draft)
            return Fail<PaymentEntryDto>("Only draft payments can be edited.", 409);

        var applyResult = await ApplyDraftAsync(payment, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<PaymentEntryDto>(applyResult.Message ?? "Invalid payment.", applyResult.Code ?? 400);

        try
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _paymentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PaymentEntryDto>("The payment was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(payment.Id, cancellationToken);
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.AsQueryable(true)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment == null)
            return Fail("Payment not found.", 404);
        if (payment.Status != FinanceDocumentStatus.Draft)
            return Fail("Only draft payments can be deleted. Posted payments must be voided.", 409);

        await _paymentRepository.DeleteAsync(payment, cancellationToken);
        return Ok();
    }

    public async Task<Result<PaymentEntryDto>> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.AsQueryable(true)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment == null)
            return Fail<PaymentEntryDto>("Payment not found.", 404);
        if (payment.Status != FinanceDocumentStatus.Draft)
            return Fail<PaymentEntryDto>("Only draft payments can be posted.", 409);
        if (payment.Amount <= 0)
            return Fail<PaymentEntryDto>("Payment amount must be greater than zero.", 400);

        var guardResult = await _guards.CheckAsync(FinanceSourceTypes.PaymentEntry, payment.Id.ToString(), FinancePostingOperation.Post, payment, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<PaymentEntryDto>(guardResult.Message ?? "Posting was rejected.", guardResult.Code ?? 403);

        var partyResult = await ValidatePartyAsync(payment.PartyType, payment.PartyId, payment.Direction, cancellationToken);
        if (!partyResult.Succeeded)
            return Fail<PaymentEntryDto>(partyResult.Message ?? "Invalid party.", partyResult.Code ?? 400);

        // 控制科目（AR/AP 角色）与资金科目解析
        var controlRole = payment.Direction == PaymentDirection.Inbound
            ? AccountSystemRole.AccountsReceivable
            : AccountSystemRole.AccountsPayable;
        var controlResult = await _helper.ResolveSystemAccountAsync(controlRole, cancellationToken);
        if (!controlResult.Succeeded)
            return Fail<PaymentEntryDto>(controlResult.Message!, controlResult.Code ?? 400);

        Guid fundsAccountId;
        if (payment.DepositToAccountId.HasValue)
        {
            var fundsResult = await _helper.GetPostableAccountAsync(payment.DepositToAccountId.Value, cancellationToken);
            if (!fundsResult.Succeeded)
                return Fail<PaymentEntryDto>($"Deposit account: {fundsResult.Message}", fundsResult.Code ?? 400);
            if (fundsResult.Data!.Id == controlResult.Data!.Id)
                return Fail<PaymentEntryDto>("The deposit/payment account must not be the AR/AP control account.", 400);
            fundsAccountId = fundsResult.Data.Id;
        }
        else if (payment.Direction == PaymentDirection.Inbound && _options.PostToUndepositedFunds)
        {
            var undeposited = await _helper.ResolveSystemAccountAsync(AccountSystemRole.UndepositedFunds, cancellationToken);
            if (!undeposited.Succeeded)
                return Fail<PaymentEntryDto>(undeposited.Message!, undeposited.Code ?? 400);
            fundsAccountId = undeposited.Data!.Id;
        }
        else
        {
            return Fail<PaymentEntryDto>("A deposit/payment account is required.", 400);
        }

        var partyTypeName = payment.PartyType == FinancePartyType.Customer ? nameof(Customer) : nameof(Vendor);
        var entry = new JournalEntry
        {
            Status = JournalEntryStatus.Draft,
            PostingDate = payment.DocDate,
            Memo = string.IsNullOrWhiteSpace(payment.Memo)
                ? (payment.Direction == PaymentDirection.Inbound ? "Payment received" : "Payment made")
                : payment.Memo,
            Currency = payment.Currency,
            ExchangeRate = payment.ExchangeRate,
            SourceType = FinanceSourceTypes.PaymentEntry,
            SourceId = payment.Id.ToString()
        };

        if (payment.Direction == PaymentDirection.Inbound)
        {
            entry.Lines.Add(new JournalLine { LineNumber = 1, AccountId = fundsAccountId, TxnDebit = payment.Amount, Currency = payment.Currency });
            entry.Lines.Add(new JournalLine
            {
                LineNumber = 2,
                AccountId = controlResult.Data!.Id,
                TxnCredit = payment.Amount,
                Currency = payment.Currency,
                PartyType = partyTypeName,
                PartyId = payment.PartyId.ToString()
            });
        }
        else
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = 1,
                AccountId = controlResult.Data!.Id,
                TxnDebit = payment.Amount,
                Currency = payment.Currency,
                PartyType = partyTypeName,
                PartyId = payment.PartyId.ToString()
            });
            entry.Lines.Add(new JournalLine { LineNumber = 2, AccountId = fundsAccountId, TxnCredit = payment.Amount, Currency = payment.Currency });
        }

        Result postResult;
        try
        {
            postResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var engineResult = await _engine.PostAsync(entry, ct);
                if (!engineResult.Succeeded)
                    return engineResult;

                await _entryRepository.InsertAsync(entry, ct);

                payment.Number = await _numberService.NextFormattedAsync(
                    FinanceSourceTypes.PaymentEntry, _options.PaymentNumberPrefix, _options.JournalNumberPadding, ct);
                payment.Status = FinanceDocumentStatus.Posted;
                payment.ExchangeRate = entry.ExchangeRate;
                payment.BaseAmount = entry.Lines.First(l => l.AccountId == controlResult.Data.Id).Debit
                    + entry.Lines.First(l => l.AccountId == controlResult.Data.Id).Credit;
                payment.DepositToAccountId ??= fundsAccountId;
                payment.JournalEntryId = entry.Id;
                await _paymentRepository.UpdateAsync(payment, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PaymentEntryDto>("The payment was modified by another operation. Reload and retry.", 409);
        }

        if (!postResult.Succeeded)
            return Fail<PaymentEntryDto>(postResult.Message ?? "Posting failed.", postResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentPostedEvent
        {
            DocType = FinanceSourceTypes.PaymentEntry,
            DocId = payment.Id,
            Number = payment.Number!,
            JournalEntryId = entry.Id,
            DocDate = payment.DocDate,
            Total = payment.Amount,
            TenantId = payment.TenantId
        }, cancellationToken);

        return await GetAsync(payment.Id, cancellationToken);
    }

    public async Task<Result<PaymentEntryDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.AsQueryable(true)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment == null)
            return Fail<PaymentEntryDto>("Payment not found.", 404);
        if (payment.Status != FinanceDocumentStatus.Posted)
            return Fail<PaymentEntryDto>("Only posted payments can be voided.", 409);
        if (payment.AppliedTotal != 0)
            return Fail<PaymentEntryDto>("The payment has been applied. Unapply it before voiding.", 409);

        var guardResult = await _guards.CheckAsync(FinanceSourceTypes.PaymentEntry, payment.Id.ToString(), FinancePostingOperation.Void, payment, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<PaymentEntryDto>(guardResult.Message ?? "Void was rejected.", guardResult.Code ?? 403);

        var original = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == payment.JournalEntryId, cancellationToken);
        if (original == null)
            return Fail<PaymentEntryDto>("The posting journal entry was not found.", 500);

        JournalEntry? reversal = null;
        Result voidResult;
        try
        {
            voidResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var buildResult = await _engine.BuildReversalAsync(original, original.PostingDate, $"Void {payment.Number}", ct);
                if (!buildResult.Succeeded)
                    return Result.Failure(buildResult.Message ?? "Void failed.", buildResult.Code ?? 400);

                reversal = buildResult.Data!;
                await _entryRepository.InsertAsync(reversal, ct);

                original.Status = JournalEntryStatus.Reversed;
                original.ReversedByEntryId = reversal.Id;
                await _entryRepository.UpdateAsync(original, ct);

                payment.Status = FinanceDocumentStatus.Voided;
                payment.VoidJournalEntryId = reversal.Id;
                await _paymentRepository.UpdateAsync(payment, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<PaymentEntryDto>("The payment was modified by another operation. Reload and retry.", 409);
        }

        if (!voidResult.Succeeded)
            return Fail<PaymentEntryDto>(voidResult.Message ?? "Void failed.", voidResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentVoidedEvent
        {
            DocType = FinanceSourceTypes.PaymentEntry,
            DocId = payment.Id,
            Number = payment.Number,
            VoidJournalEntryId = reversal!.Id,
            TenantId = payment.TenantId
        }, cancellationToken);

        return await GetAsync(payment.Id, cancellationToken);
    }

    public async Task<Result<PaymentEntryDto>> CreateFromExternalAsync(ExternalPaymentIngestDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (string.IsNullOrWhiteSpace(input.SourceType) || string.IsNullOrWhiteSpace(input.SourceId))
            return Fail<PaymentEntryDto>("SourceType and SourceId are required.");

        var sourceType = input.SourceType.Trim();
        var sourceId = input.SourceId.Trim();

        // 幂等：同来源已摄取过则返回既有单据。
        // 首次摄取 AutoPost 失败会留下草稿（草稿与过账是两次提交）——
        // 重摄取时对 Draft 补投过账（幂等自愈），临时故障（如科目未就绪）
        // 修复后款项自动进账，而不是永远以 Draft 搁浅且被"成功"返回掩盖
        var existing = await _paymentRepository.FindAsync(
            p => p.SourceType == sourceType && p.SourceId == sourceId, cancellationToken);
        if (existing != null)
        {
            if (existing.Status == FinanceDocumentStatus.Draft && input.AutoPost)
                return await PostAsync(existing.Id, cancellationToken);
            return await GetAsync(existing.Id, cancellationToken);
        }

        var payment = new PaymentEntry
        {
            SourceType = sourceType,
            SourceId = sourceId
        };
        var applyResult = await ApplyDraftAsync(payment, new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = input.CustomerId,
            DocDate = input.DocDate,
            Currency = input.Currency,
            ExchangeRate = input.ExchangeRate,
            Amount = input.Amount,
            DepositToAccountId = input.DepositToAccountId,
            PaymentMethod = input.PaymentMethod,
            Reference = input.Reference,
            Memo = input.Memo
        }, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<PaymentEntryDto>(applyResult.Message ?? "Invalid payment.", applyResult.Code ?? 400);

        try
        {
            await _paymentRepository.InsertAsync(payment, cancellationToken);
            await _paymentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 并发摄取同一来源：唯一索引兜底，返回赢家
            var winner = await _paymentRepository.FindAsync(
                p => p.SourceType == sourceType && p.SourceId == sourceId, cancellationToken);
            return winner != null
                ? await GetAsync(winner.Id, cancellationToken)
                : Fail<PaymentEntryDto>("Concurrent ingestion conflict. Retry the operation.", 409);
        }

        return input.AutoPost
            ? await PostAsync(payment.Id, cancellationToken)
            : await GetAsync(payment.Id, cancellationToken);
    }

    private async Task<Result> ApplyDraftAsync(PaymentEntry payment, CreatePaymentEntryDto input, CancellationToken cancellationToken)
    {
        if (input.Amount <= 0)
            return Fail("Payment amount must be greater than zero.");

        var partyResult = await ValidatePartyAsync(input.PartyType, input.PartyId, input.Direction, cancellationToken);
        if (!partyResult.Succeeded)
            return partyResult;

        if (input.DepositToAccountId.HasValue)
        {
            var fundsResult = await _helper.GetPostableAccountAsync(input.DepositToAccountId.Value, cancellationToken);
            if (!fundsResult.Succeeded)
                return Fail($"Deposit account: {fundsResult.Message}", fundsResult.Code ?? 400);
        }

        payment.Direction = input.Direction;
        payment.PartyType = input.PartyType;
        payment.PartyId = input.PartyId;
        payment.DocDate = input.DocDate.ToUtcDate();
        payment.Currency = _helper.NormalizeCurrency(input.Currency);
        payment.ExchangeRate = input.ExchangeRate ?? 0m;
        payment.Amount = _helper.Round(input.Amount);
        payment.DepositToAccountId = input.DepositToAccountId;
        payment.PaymentMethod = input.PaymentMethod;
        payment.Reference = input.Reference;
        payment.Memo = input.Memo;
        return Ok();
    }

    /// <summary>
    /// 往来方与方向一致性：收款方须为客户、付款方须为供应商，且往来方存在
    /// </summary>
    private async Task<Result> ValidatePartyAsync(FinancePartyType partyType, Guid partyId, PaymentDirection direction, CancellationToken cancellationToken)
    {
        if (direction == PaymentDirection.Inbound && partyType != FinancePartyType.Customer)
            return Fail("Inbound payments must reference a customer.");
        if (direction == PaymentDirection.Outbound && partyType != FinancePartyType.Vendor)
            return Fail("Outbound payments must reference a vendor.");

        var exists = partyType == FinancePartyType.Customer
            ? await _customerRepository.AnyAsync(c => c.Id == partyId, cancellationToken)
            : await _vendorRepository.AnyAsync(v => v.Id == partyId, cancellationToken);

        return exists ? Ok() : Fail($"{partyType} not found.", 404);
    }

    private async Task FillPartyNamesAsync(IList<PaymentEntryDto> items, CancellationToken cancellationToken)
    {
        var customerIds = items.Where(p => p.PartyType == FinancePartyType.Customer).Select(p => p.PartyId).Distinct().ToList();
        var vendorIds = items.Where(p => p.PartyType == FinancePartyType.Vendor).Select(p => p.PartyId).Distinct().ToList();

        var customers = customerIds.Count > 0
            ? await _customerRepository.AsNoTracking().Where(c => customerIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            : new Dictionary<Guid, string>();
        var vendors = vendorIds.Count > 0
            ? await _vendorRepository.AsNoTracking().Where(v => vendorIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Name }).ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        foreach (var dto in items)
        {
            dto.PartyName = dto.PartyType == FinancePartyType.Customer
                ? customers.GetValueOrDefault(dto.PartyId)
                : vendors.GetValueOrDefault(dto.PartyId);
        }
    }
}
