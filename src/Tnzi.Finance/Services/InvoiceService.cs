namespace Tnzi.Finance.Services;

/// <summary>
/// 销售发票服务
/// </summary>
/// <remarks>
/// 过账规则：借 应收账款（AR 角色）价税合计；贷 各行收入科目（行覆盖 ?? 目录项默认）；
/// 贷 应交税费（TaxPayable 角色）按税率组件拆行。作废 = 冲销过账凭证。
/// 原子性与并发范式同凭证服务（引擎缓冲-提交 + IConcurrencyStamp 409）。
/// </remarks>
public class InvoiceService : ApplicationService, IInvoiceService
{
    private readonly IRepository<Invoice, Guid> _invoiceRepository;
    private readonly IRepository<InvoiceLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceDocumentHelper _helper;
    private readonly PostingGuardRunner _guards;
    private readonly FinanceOptions _options;

    public InvoiceService(
        IServiceProvider serviceProvider,
        IRepository<Invoice, Guid> invoiceRepository,
        IRepository<InvoiceLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IDocumentNumberService numberService,
        LedgerPostingEngine engine,
        FinanceDocumentHelper helper,
        PostingGuardRunner guards,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _invoiceRepository = Check.NotNull(invoiceRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _numberService = Check.NotNull(numberService);
        _engine = Check.NotNull(engine);
        _helper = Check.NotNull(helper);
        _guards = Check.NotNull(guards);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<InvoiceDto>>> GetPagedAsync(InvoiceQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _invoiceRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(i => i.CreationTime)
            .Select(i => new InvoiceDto
            {
                Id = i.Id,
                Number = i.Number,
                Status = i.Status,
                CustomerId = i.CustomerId,
                CustomerName = i.Customer!.Name,
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

    public async Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invoice == null)
            return Fail<InvoiceDto>("Invoice not found.", 404);

        return Ok(ToDto(invoice));
    }

    public async Task<Result<InvoiceDto>> CreateDraftAsync(CreateInvoiceDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var invoice = new Invoice();
        var applyResult = await ApplyDraftAsync(invoice, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<InvoiceDto>(applyResult.Message ?? "Invalid invoice.", applyResult.Code ?? 400);

        await _invoiceRepository.InsertAsync(invoice, cancellationToken);
        await _invoiceRepository.SaveChangesAsync(cancellationToken);

        return await GetAsync(invoice.Id, cancellationToken);
    }

    public async Task<Result<InvoiceDto>> UpdateDraftAsync(Guid id, CreateInvoiceDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var invoice = await _invoiceRepository.AsQueryable(true)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invoice == null)
            return Fail<InvoiceDto>("Invoice not found.", 404);
        if (invoice.Status != FinanceDocumentStatus.Draft)
            return Fail<InvoiceDto>("Only draft invoices can be edited.", 409);

        var oldLines = invoice.Lines.ToList();
        var applyResult = await ApplyDraftAsync(invoice, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<InvoiceDto>(applyResult.Message ?? "Invalid invoice.", applyResult.Code ?? 400);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (oldLines.Count > 0)
                    await _lineRepository.DeleteManyAsync(oldLines, ct);
                await _invoiceRepository.UpdateAsync(invoice, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<InvoiceDto>("The invoice was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(invoice.Id, cancellationToken);
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.AsQueryable(true)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invoice == null)
            return Fail("Invoice not found.", 404);
        if (invoice.Status != FinanceDocumentStatus.Draft)
            return Fail("Only draft invoices can be deleted. Posted invoices must be voided.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (invoice.Lines.Count > 0)
                    await _lineRepository.DeleteManyAsync(invoice.Lines.ToList(), ct);
                await _invoiceRepository.DeleteAsync(invoice, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The invoice was modified by another operation. Reload and retry.", 409);
        }

        return Ok();
    }

    public async Task<Result<InvoiceDto>> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.AsQueryable(true)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invoice == null)
            return Fail<InvoiceDto>("Invoice not found.", 404);
        if (invoice.Status != FinanceDocumentStatus.Draft)
            return Fail<InvoiceDto>("Only draft invoices can be posted.", 409);
        if (invoice.Lines.Count == 0)
            return Fail<InvoiceDto>("The invoice has no lines.", 400);

        var guardResult = await _guards.CheckAsync(nameof(Invoice), invoice.Id.ToString(), FinancePostingOperation.Post, invoice, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<InvoiceDto>(guardResult.Message ?? "Posting was rejected.", guardResult.Code ?? 403);

        var customer = await _customerRepository.FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken);
        if (customer == null || !customer.IsActive)
            return Fail<InvoiceDto>("Customer not found or inactive.", 400);

        // 行收入科目解析（行覆盖 ?? 目录项默认）
        var accountResult = await ResolveLineAccountsAsync(invoice.Lines, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail<InvoiceDto>(accountResult.Message ?? "Unable to resolve income accounts.", accountResult.Code ?? 400);
        var lineAccounts = accountResult.Data!;

        // 税额（过账时权威重算）
        var taxResult = await _helper.CalculateTaxAsync(
            invoice.Lines.Select(l => new TaxCalculationLine { Amount = l.Amount, TaxCodeId = l.TaxCodeId }).ToList(),
            cancellationToken);
        if (!taxResult.Succeeded)
            return Fail<InvoiceDto>(taxResult.Message ?? "Tax calculation failed.", taxResult.Code ?? 400);
        var tax = taxResult.Data!;

        var subTotal = _helper.Round(invoice.Lines.Sum(l => l.Amount));
        var total = subTotal + tax.TaxTotal;
        if (total <= 0)
            return Fail<InvoiceDto>("Invoice total must be greater than zero.", 400);

        var arResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.AccountsReceivable, cancellationToken);
        if (!arResult.Succeeded)
            return Fail<InvoiceDto>(arResult.Message!, arResult.Code ?? 400);

        Account? taxAccount = null;
        if (tax.TaxTotal != 0)
        {
            var taxAccountResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.TaxPayable, cancellationToken);
            if (!taxAccountResult.Succeeded)
                return Fail<InvoiceDto>(taxAccountResult.Message!, taxAccountResult.Code ?? 400);
            taxAccount = taxAccountResult.Data;
        }

        var entry = new JournalEntry
        {
            Status = JournalEntryStatus.Draft,
            PostingDate = invoice.DocDate,
            Memo = string.IsNullOrWhiteSpace(invoice.Memo) ? "Invoice" : $"Invoice: {invoice.Memo}",
            Currency = invoice.Currency,
            ExchangeRate = invoice.ExchangeRate,
            SourceType = nameof(Invoice),
            SourceId = invoice.Id.ToString()
        };

        var lineNo = 1;
        entry.Lines.Add(new JournalLine
        {
            LineNumber = lineNo++,
            AccountId = arResult.Data!.Id,
            TxnDebit = total,
            Currency = invoice.Currency,
            PartyType = nameof(Customer),
            PartyId = invoice.CustomerId.ToString()
        });

        foreach (var line in invoice.Lines.OrderBy(l => l.LineNumber))
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = lineNo++,
                AccountId = lineAccounts[line.Id],
                TxnCredit = line.Amount,
                Currency = invoice.Currency,
                Memo = line.Description
            });
        }

        foreach (var component in tax.Components.Where(c => c.TaxAmount != 0))
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = lineNo++,
                AccountId = taxAccount!.Id,
                TxnCredit = component.TaxAmount,
                Currency = invoice.Currency,
                Memo = component.RateName,
                TaxRateId = component.TaxRateId
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
                invoice.Number = await _numberService.NextFormattedAsync(
                    nameof(Invoice), _options.InvoiceNumberPrefix, _options.JournalNumberPadding, ct);
                invoice.Status = FinanceDocumentStatus.Posted;
                invoice.SubTotal = subTotal;
                invoice.TaxTotal = tax.TaxTotal;
                invoice.Total = total;
                invoice.ExchangeRate = entry.ExchangeRate;
                invoice.BaseTotal = entry.Lines.First(l => l.AccountId == arResult.Data.Id).Debit;
                invoice.DueDate = (invoice.DueDate ?? invoice.DocDate.AddDays(
                    customer.PaymentTermsDays ?? _options.DefaultPaymentTermsDays)).ToUtcDate();
                invoice.JournalEntryId = entry.Id;
                await _invoiceRepository.UpdateAsync(invoice, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<InvoiceDto>("The invoice was modified by another operation. Reload and retry.", 409);
        }

        if (!postResult.Succeeded)
            return Fail<InvoiceDto>(postResult.Message ?? "Posting failed.", postResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentPostedEvent
        {
            DocType = nameof(Invoice),
            DocId = invoice.Id,
            Number = invoice.Number!,
            JournalEntryId = entry.Id,
            DocDate = invoice.DocDate,
            Total = invoice.Total,
            TenantId = invoice.TenantId
        }, cancellationToken);

        return await GetAsync(invoice.Id, cancellationToken);
    }

    public async Task<Result<InvoiceDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.AsQueryable(true)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invoice == null)
            return Fail<InvoiceDto>("Invoice not found.", 404);
        if (invoice.Status is not (FinanceDocumentStatus.Posted or FinanceDocumentStatus.PartiallyPaid))
            return Fail<InvoiceDto>("Only posted invoices can be voided.", 409);
        if (invoice.AppliedTotal != 0)
            return Fail<InvoiceDto>("The invoice has applied payments. Unapply them before voiding.", 409);

        var guardResult = await _guards.CheckAsync(nameof(Invoice), invoice.Id.ToString(), FinancePostingOperation.Void, invoice, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<InvoiceDto>(guardResult.Message ?? "Void was rejected.", guardResult.Code ?? 403);

        var original = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == invoice.JournalEntryId, cancellationToken);
        if (original == null)
            return Fail<InvoiceDto>("The posting journal entry was not found.", 500);

        JournalEntry? reversal = null;
        Result voidResult;
        try
        {
            voidResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var buildResult = await _engine.BuildReversalAsync(original, original.PostingDate, $"Void {invoice.Number}", ct);
                if (!buildResult.Succeeded)
                    return Result.Failure(buildResult.Message ?? "Void failed.", buildResult.Code ?? 400);

                reversal = buildResult.Data!;
                await _entryRepository.InsertAsync(reversal, ct);

                original.Status = JournalEntryStatus.Reversed;
                original.ReversedByEntryId = reversal.Id;
                await _entryRepository.UpdateAsync(original, ct);

                invoice.Status = FinanceDocumentStatus.Voided;
                invoice.VoidJournalEntryId = reversal.Id;
                await _invoiceRepository.UpdateAsync(invoice, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<InvoiceDto>("The invoice was modified by another operation. Reload and retry.", 409);
        }

        if (!voidResult.Succeeded)
            return Fail<InvoiceDto>(voidResult.Message ?? "Void failed.", voidResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentVoidedEvent
        {
            DocType = nameof(Invoice),
            DocId = invoice.Id,
            Number = invoice.Number,
            VoidJournalEntryId = reversal!.Id,
            TenantId = invoice.TenantId
        }, cancellationToken);

        return await GetAsync(invoice.Id, cancellationToken);
    }

    /// <summary>
    /// 草稿写入：校验客户/目录项/科目，行全量重建并计算金额（草稿总额为预估，过账时权威重算）
    /// </summary>
    private async Task<Result> ApplyDraftAsync(Invoice invoice, CreateInvoiceDto input, CancellationToken cancellationToken)
    {
        if (input.Lines == null || input.Lines.Count == 0)
            return Fail("At least one line is required.");
        if (input.Lines.Count > _options.MaxLinesPerEntry)
            return Fail($"Too many lines (max {_options.MaxLinesPerEntry}).");

        if (!await _customerRepository.AnyAsync(c => c.Id == input.CustomerId, cancellationToken))
            return Fail("Customer not found.", 404);

        var itemIds = input.Lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
        var itemsResult = await _helper.LoadItemsAsync(itemIds, cancellationToken);
        if (!itemsResult.Succeeded)
            return Fail(itemsResult.Message ?? "Invalid items.", itemsResult.Code ?? 400);

        invoice.CustomerId = input.CustomerId;
        invoice.DocDate = input.DocDate.ToUtcDate();
        invoice.DueDate = input.DueDate?.ToUtcDate();
        invoice.Currency = _helper.NormalizeCurrency(input.Currency);
        invoice.ExchangeRate = input.ExchangeRate ?? 0m;
        invoice.Memo = input.Memo;

        invoice.Lines.Clear();
        var lineNo = 1;
        foreach (var line in input.Lines)
        {
            if (line.Quantity <= 0)
                return Fail($"Line {lineNo}: quantity must be greater than zero.");
            if (line.UnitPrice < 0)
                return Fail($"Line {lineNo}: unit price must not be negative.");

            var item = line.ItemId.HasValue ? itemsResult.Data![line.ItemId.Value] : null;
            invoice.Lines.Add(new InvoiceLine
            {
                InvoiceId = invoice.Id,
                LineNumber = lineNo++,
                ItemId = line.ItemId,
                Description = line.Description ?? item?.Description,
                AccountId = line.AccountId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = _helper.Round(line.Quantity * line.UnitPrice),
                TaxCodeId = line.TaxCodeId
            });
        }

        // 草稿预估总额（过账时权威重算并覆盖）
        invoice.SubTotal = _helper.Round(invoice.Lines.Sum(l => l.Amount));
        var draftTax = await _helper.CalculateTaxAsync(
            invoice.Lines.Select(l => new TaxCalculationLine { Amount = l.Amount, TaxCodeId = l.TaxCodeId }).ToList(),
            cancellationToken);
        if (!draftTax.Succeeded)
            return Fail(draftTax.Message ?? "Tax calculation failed.", draftTax.Code ?? 400);

        invoice.TaxTotal = draftTax.Data!.TaxTotal;
        invoice.Total = invoice.SubTotal + invoice.TaxTotal;
        return Ok();
    }

    /// <summary>
    /// 解析每行收入科目（行覆盖 ?? 目录项 IncomeAccountId），全部须可过账
    /// </summary>
    private async Task<Result<Dictionary<Guid, Guid>>> ResolveLineAccountsAsync(ICollection<InvoiceLine> lines, CancellationToken cancellationToken)
    {
        var itemIds = lines.Where(l => !l.AccountId.HasValue && l.ItemId.HasValue).Select(l => l.ItemId!.Value).Distinct().ToList();
        var itemsResult = await _helper.LoadItemsAsync(itemIds, cancellationToken);
        if (!itemsResult.Succeeded)
            return Fail<Dictionary<Guid, Guid>>(itemsResult.Message!, itemsResult.Code ?? 400);

        var resolved = new Dictionary<Guid, Guid>();
        foreach (var line in lines)
        {
            var accountId = line.AccountId
                ?? (line.ItemId.HasValue ? itemsResult.Data![line.ItemId.Value].IncomeAccountId : null);
            if (!accountId.HasValue)
                return Fail<Dictionary<Guid, Guid>>($"Line {line.LineNumber}: no income account (set the line account or the item default).", 400);

            var accountResult = await _helper.GetPostableAccountAsync(accountId.Value, cancellationToken);
            if (!accountResult.Succeeded)
                return Fail<Dictionary<Guid, Guid>>($"Line {line.LineNumber}: {accountResult.Message}", accountResult.Code ?? 400);

            resolved[line.Id] = accountId.Value;
        }

        return Ok(resolved);
    }

    private static InvoiceDto ToDto(Invoice invoice)
    {
        var dto = invoice.MapTo<InvoiceDto>();
        dto.CustomerName = invoice.Customer?.Name;
        dto.Lines = invoice.Lines.OrderBy(l => l.LineNumber).Select(l => l.MapTo<InvoiceLineDto>()).ToList();
        return dto;
    }
}
