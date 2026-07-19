namespace Tnzi.Finance.Services;

/// <summary>
/// 采购账单服务
/// </summary>
/// <remarks>
/// 过账规则：借 各行费用科目（行覆盖 ?? 目录项默认）；借 进项税（TaxReceivable 角色）
/// 按税率组件拆行；贷 应付账款（AP 角色）价税合计。作废 = 冲销过账凭证。
/// </remarks>
public class BillService : ApplicationService, IBillService
{
    private readonly IRepository<Bill, Guid> _billRepository;
    private readonly IRepository<BillLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceDocumentHelper _helper;
    private readonly PostingGuardRunner _guards;
    private readonly FinanceOptions _options;

    public BillService(
        IServiceProvider serviceProvider,
        IRepository<Bill, Guid> billRepository,
        IRepository<BillLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IDocumentNumberService numberService,
        LedgerPostingEngine engine,
        FinanceDocumentHelper helper,
        PostingGuardRunner guards,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _billRepository = Check.NotNull(billRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _numberService = Check.NotNull(numberService);
        _engine = Check.NotNull(engine);
        _helper = Check.NotNull(helper);
        _guards = Check.NotNull(guards);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<BillDto>>> GetPagedAsync(BillQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _billRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(i => i.CreationTime)
            .Select(i => new BillDto
            {
                Id = i.Id,
                Number = i.Number,
                Status = i.Status,
                VendorId = i.VendorId,
                VendorName = i.Vendor!.Name,
                DocDate = i.DocDate,
                DueDate = i.DueDate,
                Currency = i.Currency,
                ExchangeRate = i.ExchangeRate,
                SubTotal = i.SubTotal,
                TaxTotal = i.TaxTotal,
                Total = i.Total,
                BaseTotal = i.BaseTotal,
                AppliedTotal = i.AppliedTotal,
                Memo = i.Memo,
                JournalEntryId = i.JournalEntryId,
                VoidJournalEntryId = i.VoidJournalEntryId,
                CreationTime = i.CreationTime
            })
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<BillDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bill = await _billRepository.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Vendor)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (bill == null)
            return Fail<BillDto>("Bill not found.", 404);

        return Ok(ToDto(bill));
    }

    public async Task<Result<BillDto>> CreateDraftAsync(CreateBillDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var bill = new Bill();
        var applyResult = await ApplyDraftAsync(bill, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<BillDto>(applyResult.Message ?? "Invalid bill.", applyResult.Code ?? 400);

        await _billRepository.InsertAsync(bill, cancellationToken);
        await _billRepository.SaveChangesAsync(cancellationToken);

        return await GetAsync(bill.Id, cancellationToken);
    }

    public async Task<Result<BillDto>> UpdateDraftAsync(Guid id, CreateBillDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var bill = await _billRepository.AsQueryable(true)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (bill == null)
            return Fail<BillDto>("Bill not found.", 404);
        if (bill.Status != FinanceDocumentStatus.Draft)
            return Fail<BillDto>("Only draft bills can be edited.", 409);

        var oldLines = bill.Lines.ToList();
        var applyResult = await ApplyDraftAsync(bill, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<BillDto>(applyResult.Message ?? "Invalid bill.", applyResult.Code ?? 400);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (oldLines.Count > 0)
                    await _lineRepository.DeleteManyAsync(oldLines, ct);
                await _billRepository.UpdateAsync(bill, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BillDto>("The bill was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(bill.Id, cancellationToken);
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bill = await _billRepository.AsQueryable(true)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (bill == null)
            return Fail("Bill not found.", 404);
        if (bill.Status != FinanceDocumentStatus.Draft)
            return Fail("Only draft bills can be deleted. Posted bills must be voided.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (bill.Lines.Count > 0)
                    await _lineRepository.DeleteManyAsync(bill.Lines.ToList(), ct);
                await _billRepository.DeleteAsync(bill, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The bill was modified by another operation. Reload and retry.", 409);
        }

        return Ok();
    }

    public async Task<Result<BillDto>> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bill = await _billRepository.AsQueryable(true)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (bill == null)
            return Fail<BillDto>("Bill not found.", 404);
        if (bill.Status != FinanceDocumentStatus.Draft)
            return Fail<BillDto>("Only draft bills can be posted.", 409);
        if (bill.Lines.Count == 0)
            return Fail<BillDto>("The bill has no lines.", 400);

        var guardResult = await _guards.CheckAsync(FinanceSourceTypes.Bill, bill.Id.ToString(), FinancePostingOperation.Post, bill, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<BillDto>(guardResult.Message ?? "Posting was rejected.", guardResult.Code ?? 403);

        var vendor = await _vendorRepository.FirstOrDefaultAsync(c => c.Id == bill.VendorId, cancellationToken);
        if (vendor == null || !vendor.IsActive)
            return Fail<BillDto>("Vendor not found or inactive.", 400);

        // 行费用科目解析（行覆盖 ?? 目录项默认）
        var accountResult = await ResolveLineAccountsAsync(bill.Lines, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail<BillDto>(accountResult.Message ?? "Unable to resolve expense accounts.", accountResult.Code ?? 400);
        var lineAccounts = accountResult.Data!;

        // 税额（过账时权威重算；行手动覆盖额透传）
        var taxResult = await _helper.CalculateTaxAsync(
            bill.Lines.Select(l => new TaxCalculationLine { Amount = l.Amount, TaxCodeId = l.TaxCodeId, TaxAmount = l.TaxAmount }).ToList(),
            cancellationToken, isPurchase: true);
        if (!taxResult.Succeeded)
            return Fail<BillDto>(taxResult.Message ?? "Tax calculation failed.", taxResult.Code ?? 400);
        var tax = taxResult.Data!;

        var subTotal = _helper.Round(bill.Lines.Sum(l => l.Amount));
        var total = subTotal + tax.TaxTotal;
        if (total <= 0)
            return Fail<BillDto>("Bill total must be greater than zero.", 400);

        var apResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.AccountsPayable, cancellationToken);
        if (!apResult.Succeeded)
            return Fail<BillDto>(apResult.Message!, apResult.Code ?? 400);

        // 可抵扣税进 TaxReceivable（进项抵扣 + 申报口径）；不可抵扣税作为成本进 NonRecoverableTaxExpense。
        Account? taxAccount = null;
        if (tax.Components.Any(c => c.TaxAmount != 0))
        {
            var taxAccountResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.TaxReceivable, cancellationToken);
            if (!taxAccountResult.Succeeded)
                return Fail<BillDto>(taxAccountResult.Message!, taxAccountResult.Code ?? 400);
            taxAccount = taxAccountResult.Data;
        }

        Account? nonRecoverableAccount = null;
        if (tax.NonRecoverableTotal != 0)
        {
            var nrResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.NonRecoverableTaxExpense, cancellationToken);
            if (!nrResult.Succeeded)
                return Fail<BillDto>(nrResult.Message!, nrResult.Code ?? 400);
            nonRecoverableAccount = nrResult.Data;
        }

        var entry = new JournalEntry
        {
            Status = JournalEntryStatus.Draft,
            PostingDate = bill.DocDate,
            Memo = string.IsNullOrWhiteSpace(bill.Memo) ? "Bill" : $"Bill: {bill.Memo}",
            Currency = bill.Currency,
            ExchangeRate = bill.ExchangeRate,
            SourceType = FinanceSourceTypes.Bill,
            SourceId = bill.Id.ToString()
        };

        var lineNo = 1;
        entry.Lines.Add(new JournalLine
        {
            LineNumber = lineNo++,
            AccountId = apResult.Data!.Id,
            TxnCredit = total,
            Currency = bill.Currency,
            PartyType = nameof(Vendor),
            PartyId = bill.VendorId.ToString()
        });

        foreach (var line in bill.Lines.OrderBy(l => l.LineNumber))
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = lineNo++,
                AccountId = lineAccounts[line.Id],
                TxnDebit = line.Amount,
                Currency = bill.Currency,
                Memo = line.Description
            });
        }

        foreach (var component in tax.Components.Where(c => c.TaxAmount != 0))
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = lineNo++,
                AccountId = taxAccount!.Id,
                TxnDebit = component.TaxAmount,
                Currency = bill.Currency,
                Memo = component.RateName,
                TaxRateId = component.TaxRateId
            });
        }

        if (nonRecoverableAccount != null)
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = lineNo++,
                AccountId = nonRecoverableAccount.Id,
                TxnDebit = tax.NonRecoverableTotal,
                Currency = bill.Currency,
                Memo = "Non-recoverable tax"
            });
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

                // 单据号分配在全部可失败校验之后（回滚回收）
                bill.Number = await _numberService.NextFormattedAsync(
                    FinanceSourceTypes.Bill, _options.BillNumberPrefix, _options.JournalNumberPadding, ct);
                bill.Status = FinanceDocumentStatus.Posted;
                bill.SubTotal = subTotal;
                bill.TaxTotal = tax.TaxTotal;
                bill.Total = total;
                bill.ExchangeRate = entry.ExchangeRate;
                bill.BaseTotal = entry.Lines.First(l => l.AccountId == apResult.Data.Id).Credit;
                bill.DueDate = (bill.DueDate ?? bill.DocDate.AddDays(
                    vendor.PaymentTermsDays ?? _options.DefaultPaymentTermsDays)).ToUtcDate();
                bill.JournalEntryId = entry.Id;
                await _billRepository.UpdateAsync(bill, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BillDto>("The bill was modified by another operation. Reload and retry.", 409);
        }

        if (!postResult.Succeeded)
            return Fail<BillDto>(postResult.Message ?? "Posting failed.", postResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentPostedEvent
        {
            DocType = FinanceSourceTypes.Bill,
            DocId = bill.Id,
            Number = bill.Number!,
            JournalEntryId = entry.Id,
            DocDate = bill.DocDate,
            Total = bill.Total,
            TenantId = bill.TenantId
        }, cancellationToken);

        return await GetAsync(bill.Id, cancellationToken);
    }

    public async Task<Result<BillDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bill = await _billRepository.AsQueryable(true)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (bill == null)
            return Fail<BillDto>("Bill not found.", 404);
        if (bill.Status is not (FinanceDocumentStatus.Posted or FinanceDocumentStatus.PartiallyPaid))
            return Fail<BillDto>("Only posted bills can be voided.", 409);
        if (bill.AppliedTotal != 0)
            return Fail<BillDto>("The bill has applied payments. Unapply them before voiding.", 409);

        var guardResult = await _guards.CheckAsync(FinanceSourceTypes.Bill, bill.Id.ToString(), FinancePostingOperation.Void, bill, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<BillDto>(guardResult.Message ?? "Void was rejected.", guardResult.Code ?? 403);

        var original = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == bill.JournalEntryId, cancellationToken);
        if (original == null)
            return Fail<BillDto>("The posting journal entry was not found.", 500);

        JournalEntry? reversal = null;
        Result voidResult;
        try
        {
            voidResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var buildResult = await _engine.BuildReversalAsync(original, original.PostingDate, $"Void {bill.Number}", ct);
                if (!buildResult.Succeeded)
                    return Result.Failure(buildResult.Message ?? "Void failed.", buildResult.Code ?? 400);

                reversal = buildResult.Data!;
                await _entryRepository.InsertAsync(reversal, ct);

                original.Status = JournalEntryStatus.Reversed;
                original.ReversedByEntryId = reversal.Id;
                await _entryRepository.UpdateAsync(original, ct);

                bill.Status = FinanceDocumentStatus.Voided;
                bill.VoidJournalEntryId = reversal.Id;
                await _billRepository.UpdateAsync(bill, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BillDto>("The bill was modified by another operation. Reload and retry.", 409);
        }

        if (!voidResult.Succeeded)
            return Fail<BillDto>(voidResult.Message ?? "Void failed.", voidResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentVoidedEvent
        {
            DocType = FinanceSourceTypes.Bill,
            DocId = bill.Id,
            Number = bill.Number,
            VoidJournalEntryId = reversal!.Id,
            TenantId = bill.TenantId
        }, cancellationToken);

        return await GetAsync(bill.Id, cancellationToken);
    }

    /// <summary>
    /// 草稿写入：校验供应商/目录项/科目，行全量重建并计算金额（草稿总额为预估，过账时权威重算）
    /// </summary>
    private async Task<Result> ApplyDraftAsync(Bill bill, CreateBillDto input, CancellationToken cancellationToken)
    {
        if (input.Lines == null || input.Lines.Count == 0)
            return Fail("At least one line is required.");
        if (input.Lines.Count > _options.MaxLinesPerEntry)
            return Fail($"Too many lines (max {_options.MaxLinesPerEntry}).");

        if (!await _vendorRepository.AnyAsync(c => c.Id == input.VendorId, cancellationToken))
            return Fail("Vendor not found.", 404);

        var itemIds = input.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
        var itemsResult = await _helper.LoadItemsAsync(itemIds, cancellationToken);
        if (!itemsResult.Succeeded)
            return Fail(itemsResult.Message ?? "Invalid items.", itemsResult.Code ?? 400);

        bill.VendorId = input.VendorId;
        bill.DocDate = input.DocDate.ToUtcDate();
        bill.DueDate = input.DueDate?.ToUtcDate();
        bill.Currency = _helper.NormalizeCurrency(input.Currency);
        bill.ExchangeRate = input.ExchangeRate ?? 0m;
        bill.Memo = input.Memo;

        bill.Lines.Clear();
        var lineNo = 1;
        foreach (var line in input.Lines)
        {
            if (line.Quantity <= 0)
                return Fail($"Line {lineNo}: quantity must be greater than zero.");
            if (line.UnitPrice < 0)
                return Fail($"Line {lineNo}: unit price must not be negative.");
            if (line.TaxAmount < 0)
                return Fail($"Line {lineNo}: the manual tax amount must not be negative.");
            if (line.TaxAmount.HasValue && !line.TaxCodeId.HasValue)
                return Fail($"Line {lineNo}: a manual tax amount requires a tax code.");

            var item = line.ItemId.HasValue ? itemsResult.Data![line.ItemId.Value] : null;
            bill.Lines.Add(new BillLine
            {
                BillId = bill.Id,
                LineNumber = lineNo++,
                ItemId = line.ItemId,
                Description = line.Description ?? item?.Description,
                AccountId = line.AccountId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = _helper.Round(line.Quantity * line.UnitPrice),
                TaxCodeId = line.TaxCodeId,
                TaxAmount = line.TaxAmount.HasValue ? _helper.Round(line.TaxAmount.Value) : null
            });
        }

        // 草稿预估总额（过账时权威重算并覆盖）
        bill.SubTotal = _helper.Round(bill.Lines.Sum(l => l.Amount));
        var draftTax = await _helper.CalculateTaxAsync(
            bill.Lines.Select(l => new TaxCalculationLine { Amount = l.Amount, TaxCodeId = l.TaxCodeId, TaxAmount = l.TaxAmount }).ToList(),
            cancellationToken, isPurchase: true);
        if (!draftTax.Succeeded)
            return Fail(draftTax.Message ?? "Tax calculation failed.", draftTax.Code ?? 400);

        bill.TaxTotal = draftTax.Data!.TaxTotal;
        bill.Total = bill.SubTotal + bill.TaxTotal;
        return Ok();
    }

    /// <summary>
    /// 解析每行费用科目（行覆盖 ?? 目录项 ExpenseAccountId），全部须可过账
    /// </summary>
    private async Task<Result<Dictionary<Guid, Guid>>> ResolveLineAccountsAsync(ICollection<BillLine> lines, CancellationToken cancellationToken)
    {
        var itemIds = lines.Where(l => !l.AccountId.HasValue && l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
        var itemsResult = await _helper.LoadItemsAsync(itemIds, cancellationToken);
        if (!itemsResult.Succeeded)
            return Fail<Dictionary<Guid, Guid>>(itemsResult.Message!, itemsResult.Code ?? 400);

        var resolved = new Dictionary<Guid, Guid>();
        foreach (var line in lines)
        {
            var accountId = line.AccountId
                ?? (line.ItemId.HasValue ? itemsResult.Data![line.ItemId.Value].ExpenseAccountId : null);
            if (!accountId.HasValue)
                return Fail<Dictionary<Guid, Guid>>($"Line {line.LineNumber}: no expense account (set the line account or the item default).", 400);

            var accountResult = await _helper.GetPostableAccountAsync(accountId.Value, cancellationToken);
            if (!accountResult.Succeeded)
                return Fail<Dictionary<Guid, Guid>>($"Line {line.LineNumber}: {accountResult.Message}", accountResult.Code ?? 400);

            resolved[line.Id] = accountId.Value;
        }

        return Ok(resolved);
    }

    private static BillDto ToDto(Bill bill)
    {
        var dto = bill.MapTo<BillDto>();
        dto.VendorName = bill.Vendor?.Name;
        dto.Lines = bill.Lines.OrderBy(l => l.LineNumber).Select(l => l.MapTo<BillLineDto>()).ToList();
        return dto;
    }
}
