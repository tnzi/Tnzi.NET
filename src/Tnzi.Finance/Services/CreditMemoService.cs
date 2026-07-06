namespace Tnzi.Finance.Services;

/// <summary>
/// 销售贷项单服务（GL 投影为发票的镜像）
/// </summary>
/// <remarks>
/// 过账规则：借 各行收入科目；借 应交税费（TaxPayable 角色）；贷 应收账款（AR 角色）价税合计。
/// 作废 = 冲销过账凭证。核销到发票见 P2c 结算服务。
/// </remarks>
public class CreditMemoService : ApplicationService, ICreditMemoService
{
    private readonly IRepository<CreditMemo, Guid> _creditMemoRepository;
    private readonly IRepository<CreditMemoLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceDocumentHelper _helper;
    private readonly FinanceOptions _options;

    public CreditMemoService(
        IServiceProvider serviceProvider,
        IRepository<CreditMemo, Guid> creditMemoRepository,
        IRepository<CreditMemoLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IDocumentNumberService numberService,
        LedgerPostingEngine engine,
        FinanceDocumentHelper helper,
        IOptions<FinanceOptions> options)
        : base(serviceProvider)
    {
        _creditMemoRepository = Check.NotNull(creditMemoRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _numberService = Check.NotNull(numberService);
        _engine = Check.NotNull(engine);
        _helper = Check.NotNull(helper);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<CreditMemoDto>>> GetPagedAsync(CreditMemoQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _creditMemoRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(i => i.CreationTime)
            .Select(i => new CreditMemoDto
            {
                Id = i.Id,
                Number = i.Number,
                Status = i.Status,
                CustomerId = i.CustomerId,
                CustomerName = i.Customer!.Name,
                DocDate = i.DocDate,
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

    public async Task<Result<CreditMemoDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var creditMemo = await _creditMemoRepository.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (creditMemo == null)
            return Fail<CreditMemoDto>("CreditMemo not found.", 404);

        return Ok(ToDto(creditMemo));
    }

    public async Task<Result<CreditMemoDto>> CreateDraftAsync(CreateCreditMemoDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var creditMemo = new CreditMemo();
        var applyResult = await ApplyDraftAsync(creditMemo, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<CreditMemoDto>(applyResult.Message ?? "Invalid creditMemo.", applyResult.Code ?? 400);

        await _creditMemoRepository.InsertAsync(creditMemo, cancellationToken);
        await _creditMemoRepository.SaveChangesAsync(cancellationToken);

        return await GetAsync(creditMemo.Id, cancellationToken);
    }

    public async Task<Result<CreditMemoDto>> UpdateDraftAsync(Guid id, CreateCreditMemoDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var creditMemo = await _creditMemoRepository.AsQueryable(true)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (creditMemo == null)
            return Fail<CreditMemoDto>("CreditMemo not found.", 404);
        if (creditMemo.Status != FinanceDocumentStatus.Draft)
            return Fail<CreditMemoDto>("Only draft creditMemos can be edited.", 409);

        var oldLines = creditMemo.Lines.ToList();
        var applyResult = await ApplyDraftAsync(creditMemo, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<CreditMemoDto>(applyResult.Message ?? "Invalid creditMemo.", applyResult.Code ?? 400);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (oldLines.Count > 0)
                    await _lineRepository.DeleteManyAsync(oldLines, ct);
                await _creditMemoRepository.UpdateAsync(creditMemo, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<CreditMemoDto>("The creditMemo was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(creditMemo.Id, cancellationToken);
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var creditMemo = await _creditMemoRepository.AsQueryable(true)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (creditMemo == null)
            return Fail("CreditMemo not found.", 404);
        if (creditMemo.Status != FinanceDocumentStatus.Draft)
            return Fail("Only draft creditMemos can be deleted. Posted creditMemos must be voided.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (creditMemo.Lines.Count > 0)
                    await _lineRepository.DeleteManyAsync(creditMemo.Lines.ToList(), ct);
                await _creditMemoRepository.DeleteAsync(creditMemo, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The creditMemo was modified by another operation. Reload and retry.", 409);
        }

        return Ok();
    }

    public async Task<Result<CreditMemoDto>> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var creditMemo = await _creditMemoRepository.AsQueryable(true)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (creditMemo == null)
            return Fail<CreditMemoDto>("CreditMemo not found.", 404);
        if (creditMemo.Status != FinanceDocumentStatus.Draft)
            return Fail<CreditMemoDto>("Only draft creditMemos can be posted.", 409);
        if (creditMemo.Lines.Count == 0)
            return Fail<CreditMemoDto>("The creditMemo has no lines.", 400);

        var customer = await _customerRepository.FirstOrDefaultAsync(c => c.Id == creditMemo.CustomerId, cancellationToken);
        if (customer == null || !customer.IsActive)
            return Fail<CreditMemoDto>("Customer not found or inactive.", 400);

        // 行收入科目解析（行覆盖 ?? 目录项默认）
        var accountResult = await ResolveLineAccountsAsync(creditMemo.Lines, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail<CreditMemoDto>(accountResult.Message ?? "Unable to resolve income accounts.", accountResult.Code ?? 400);
        var lineAccounts = accountResult.Data!;

        // 税额（过账时权威重算）
        var taxResult = await _helper.CalculateTaxAsync(
            creditMemo.Lines.Select(l => new TaxCalculationLine { Amount = l.Amount, TaxCodeId = l.TaxCodeId }).ToList(),
            cancellationToken);
        if (!taxResult.Succeeded)
            return Fail<CreditMemoDto>(taxResult.Message ?? "Tax calculation failed.", taxResult.Code ?? 400);
        var tax = taxResult.Data!;

        var subTotal = _helper.Round(creditMemo.Lines.Sum(l => l.Amount));
        var total = subTotal + tax.TaxTotal;
        if (total <= 0)
            return Fail<CreditMemoDto>("CreditMemo total must be greater than zero.", 400);

        var arResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.AccountsReceivable, cancellationToken);
        if (!arResult.Succeeded)
            return Fail<CreditMemoDto>(arResult.Message!, arResult.Code ?? 400);

        Account? taxAccount = null;
        if (tax.TaxTotal != 0)
        {
            var taxAccountResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.TaxPayable, cancellationToken);
            if (!taxAccountResult.Succeeded)
                return Fail<CreditMemoDto>(taxAccountResult.Message!, taxAccountResult.Code ?? 400);
            taxAccount = taxAccountResult.Data;
        }

        var entry = new JournalEntry
        {
            Status = JournalEntryStatus.Draft,
            PostingDate = creditMemo.DocDate,
            Memo = string.IsNullOrWhiteSpace(creditMemo.Memo) ? "CreditMemo" : $"Credit memo: {creditMemo.Memo}",
            Currency = creditMemo.Currency,
            ExchangeRate = creditMemo.ExchangeRate,
            SourceType = nameof(CreditMemo),
            SourceId = creditMemo.Id.ToString()
        };

        var lineNo = 1;
        entry.Lines.Add(new JournalLine
        {
            LineNumber = lineNo++,
            AccountId = arResult.Data!.Id,
            TxnCredit = total,
            Currency = creditMemo.Currency,
            PartyType = nameof(Customer),
            PartyId = creditMemo.CustomerId.ToString()
        });

        foreach (var line in creditMemo.Lines.OrderBy(l => l.LineNumber))
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = lineNo++,
                AccountId = lineAccounts[line.Id],
                TxnDebit = line.Amount,
                Currency = creditMemo.Currency,
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
                Currency = creditMemo.Currency,
                Memo = component.RateName
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
                creditMemo.Number = await _numberService.NextFormattedAsync(
                    nameof(CreditMemo), _options.CreditMemoNumberPrefix, _options.JournalNumberPadding, ct);
                creditMemo.Status = FinanceDocumentStatus.Posted;
                creditMemo.SubTotal = subTotal;
                creditMemo.TaxTotal = tax.TaxTotal;
                creditMemo.Total = total;
                creditMemo.ExchangeRate = entry.ExchangeRate;
                creditMemo.BaseTotal = entry.Lines.First(l => l.AccountId == arResult.Data.Id).Credit;
                creditMemo.JournalEntryId = entry.Id;
                await _creditMemoRepository.UpdateAsync(creditMemo, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<CreditMemoDto>("The creditMemo was modified by another operation. Reload and retry.", 409);
        }

        if (!postResult.Succeeded)
            return Fail<CreditMemoDto>(postResult.Message ?? "Posting failed.", postResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentPostedEvent
        {
            DocType = nameof(CreditMemo),
            DocId = creditMemo.Id,
            Number = creditMemo.Number!,
            JournalEntryId = entry.Id,
            DocDate = creditMemo.DocDate,
            Total = creditMemo.Total,
            TenantId = creditMemo.TenantId
        }, cancellationToken);

        return await GetAsync(creditMemo.Id, cancellationToken);
    }

    public async Task<Result<CreditMemoDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var creditMemo = await _creditMemoRepository.AsQueryable(true)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (creditMemo == null)
            return Fail<CreditMemoDto>("CreditMemo not found.", 404);
        if (creditMemo.Status is not (FinanceDocumentStatus.Posted or FinanceDocumentStatus.PartiallyPaid))
            return Fail<CreditMemoDto>("Only posted creditMemos can be voided.", 409);
        if (creditMemo.AppliedTotal != 0)
            return Fail<CreditMemoDto>("The credit memo has been applied. Unapply it before voiding.", 409);

        var original = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == creditMemo.JournalEntryId, cancellationToken);
        if (original == null)
            return Fail<CreditMemoDto>("The posting journal entry was not found.", 500);

        JournalEntry? reversal = null;
        Result voidResult;
        try
        {
            voidResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var buildResult = await _engine.BuildReversalAsync(original, original.PostingDate, $"Void {creditMemo.Number}", ct);
                if (!buildResult.Succeeded)
                    return Result.Failure(buildResult.Message ?? "Void failed.", buildResult.Code ?? 400);

                reversal = buildResult.Data!;
                await _entryRepository.InsertAsync(reversal, ct);

                original.Status = JournalEntryStatus.Reversed;
                original.ReversedByEntryId = reversal.Id;
                await _entryRepository.UpdateAsync(original, ct);

                creditMemo.Status = FinanceDocumentStatus.Voided;
                creditMemo.VoidJournalEntryId = reversal.Id;
                await _creditMemoRepository.UpdateAsync(creditMemo, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<CreditMemoDto>("The creditMemo was modified by another operation. Reload and retry.", 409);
        }

        if (!voidResult.Succeeded)
            return Fail<CreditMemoDto>(voidResult.Message ?? "Void failed.", voidResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentVoidedEvent
        {
            DocType = nameof(CreditMemo),
            DocId = creditMemo.Id,
            Number = creditMemo.Number,
            VoidJournalEntryId = reversal!.Id,
            TenantId = creditMemo.TenantId
        }, cancellationToken);

        return await GetAsync(creditMemo.Id, cancellationToken);
    }

    /// <summary>
    /// 草稿写入：校验客户/目录项/科目，行全量重建并计算金额（草稿总额为预估，过账时权威重算）
    /// </summary>
    private async Task<Result> ApplyDraftAsync(CreditMemo creditMemo, CreateCreditMemoDto input, CancellationToken cancellationToken)
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

        creditMemo.CustomerId = input.CustomerId;
        creditMemo.DocDate = input.DocDate.ToUtcDate();
        creditMemo.Currency = _helper.NormalizeCurrency(input.Currency);
        creditMemo.ExchangeRate = input.ExchangeRate ?? 0m;
        creditMemo.Memo = input.Memo;

        creditMemo.Lines.Clear();
        var lineNo = 1;
        foreach (var line in input.Lines)
        {
            if (line.Quantity <= 0)
                return Fail($"Line {lineNo}: quantity must be greater than zero.");
            if (line.UnitPrice < 0)
                return Fail($"Line {lineNo}: unit price must not be negative.");

            var item = line.ItemId.HasValue ? itemsResult.Data![line.ItemId.Value] : null;
            creditMemo.Lines.Add(new CreditMemoLine
            {
                CreditMemoId = creditMemo.Id,
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
        creditMemo.SubTotal = _helper.Round(creditMemo.Lines.Sum(l => l.Amount));
        var draftTax = await _helper.CalculateTaxAsync(
            creditMemo.Lines.Select(l => new TaxCalculationLine { Amount = l.Amount, TaxCodeId = l.TaxCodeId }).ToList(),
            cancellationToken);
        if (!draftTax.Succeeded)
            return Fail(draftTax.Message ?? "Tax calculation failed.", draftTax.Code ?? 400);

        creditMemo.TaxTotal = draftTax.Data!.TaxTotal;
        creditMemo.Total = creditMemo.SubTotal + creditMemo.TaxTotal;
        return Ok();
    }

    /// <summary>
    /// 解析每行收入科目（行覆盖 ?? 目录项 IncomeAccountId），全部须可过账
    /// </summary>
    private async Task<Result<Dictionary<Guid, Guid>>> ResolveLineAccountsAsync(ICollection<CreditMemoLine> lines, CancellationToken cancellationToken)
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

    private static CreditMemoDto ToDto(CreditMemo creditMemo)
    {
        var dto = creditMemo.MapTo<CreditMemoDto>();
        dto.CustomerName = creditMemo.Customer?.Name;
        dto.Lines = creditMemo.Lines.OrderBy(l => l.LineNumber).Select(l => l.MapTo<CreditMemoLineDto>()).ToList();
        return dto;
    }
}
