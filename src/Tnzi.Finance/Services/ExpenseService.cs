namespace Tnzi.Finance.Services;

/// <summary>
/// 费用支出服务（直接支付，不经 A/P）
/// </summary>
/// <remarks>
/// 过账规则：借 各行费用科目 + 借 进项税（TaxReceivable 角色）；贷 付款科目（银行/现金/信用卡）。
/// 状态仅 Draft/Posted/Voided。
/// </remarks>
public class ExpenseService : ApplicationService, IExpenseService
{
    private readonly IRepository<Expense, Guid> _expenseRepository;
    private readonly IRepository<ExpenseLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceDocumentHelper _helper;
    private readonly FinanceOptions _options;

    public ExpenseService(
        IServiceProvider serviceProvider,
        IRepository<Expense, Guid> expenseRepository,
        IRepository<ExpenseLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IDocumentNumberService numberService,
        LedgerPostingEngine engine,
        FinanceDocumentHelper helper,
        IOptions<FinanceOptions> options)
        : base(serviceProvider)
    {
        _expenseRepository = Check.NotNull(expenseRepository);
        _lineRepository = Check.NotNull(lineRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _numberService = Check.NotNull(numberService);
        _engine = Check.NotNull(engine);
        _helper = Check.NotNull(helper);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<ExpenseDto>>> GetPagedAsync(ExpenseQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        // 显式投影供应商名（镜像 Bill.GetPagedAsync）——VendorId 可空则 VendorName 为 null。
        var pagedList = await _expenseRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(e => e.CreationTime)
            .Select(e => new ExpenseDto
            {
                Id = e.Id,
                Number = e.Number,
                Status = e.Status,
                VendorId = e.VendorId,
                VendorName = e.Vendor != null ? e.Vendor.Name : null,
                PaidFromAccountId = e.PaidFromAccountId,
                DocDate = e.DocDate,
                Currency = e.Currency,
                ExchangeRate = e.ExchangeRate,
                SubTotal = e.SubTotal,
                TaxTotal = e.TaxTotal,
                Total = e.Total,
                BaseTotal = e.BaseTotal,
                Memo = e.Memo,
                JournalEntryId = e.JournalEntryId,
                VoidJournalEntryId = e.VoidJournalEntryId,
                CreationTime = e.CreationTime
            })
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<ExpenseDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var expense = await _expenseRepository.AsNoTracking()
            .Include(e => e.Lines)
            .Include(e => e.Vendor)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (expense == null)
            return Fail<ExpenseDto>("Expense not found.", 404);

        var dto = expense.MapTo<ExpenseDto>();
        dto.VendorName = expense.Vendor?.Name;
        dto.Lines = expense.Lines.OrderBy(l => l.LineNumber).Select(l => l.MapTo<ExpenseLineDto>()).ToList();

        // 付款科目名（详情解析；无 PaidFromAccount 导航，走一次轻量查找）
        var paidFrom = await _accountRepository.FirstOrDefaultAsync(a => a.Id == expense.PaidFromAccountId, cancellationToken);
        dto.PaidFromAccountName = paidFrom?.Name;

        return Ok(dto);
    }

    public async Task<Result<ExpenseDto>> CreateDraftAsync(CreateExpenseDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var expense = new Expense();
        var applyResult = await ApplyDraftAsync(expense, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<ExpenseDto>(applyResult.Message ?? "Invalid expense.", applyResult.Code ?? 400);

        await _expenseRepository.InsertAsync(expense, cancellationToken);
        await _expenseRepository.SaveChangesAsync(cancellationToken);

        return await GetAsync(expense.Id, cancellationToken);
    }

    public async Task<Result<ExpenseDto>> UpdateDraftAsync(Guid id, CreateExpenseDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var expense = await _expenseRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (expense == null)
            return Fail<ExpenseDto>("Expense not found.", 404);
        if (expense.Status != FinanceDocumentStatus.Draft)
            return Fail<ExpenseDto>("Only draft expenses can be edited.", 409);

        var oldLines = expense.Lines.ToList();
        var applyResult = await ApplyDraftAsync(expense, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<ExpenseDto>(applyResult.Message ?? "Invalid expense.", applyResult.Code ?? 400);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (oldLines.Count > 0)
                    await _lineRepository.DeleteManyAsync(oldLines, ct);
                await _expenseRepository.UpdateAsync(expense, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ExpenseDto>("The expense was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(expense.Id, cancellationToken);
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var expense = await _expenseRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (expense == null)
            return Fail("Expense not found.", 404);
        if (expense.Status != FinanceDocumentStatus.Draft)
            return Fail("Only draft expenses can be deleted. Posted expenses must be voided.", 409);

        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                if (expense.Lines.Count > 0)
                    await _lineRepository.DeleteManyAsync(expense.Lines.ToList(), ct);
                await _expenseRepository.DeleteAsync(expense, ct);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The expense was modified by another operation. Reload and retry.", 409);
        }

        return Ok();
    }

    public async Task<Result<ExpenseDto>> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var expense = await _expenseRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (expense == null)
            return Fail<ExpenseDto>("Expense not found.", 404);
        if (expense.Status != FinanceDocumentStatus.Draft)
            return Fail<ExpenseDto>("Only draft expenses can be posted.", 409);
        if (expense.Lines.Count == 0)
            return Fail<ExpenseDto>("The expense has no lines.", 400);

        var paidFromResult = await _helper.GetPostableAccountAsync(expense.PaidFromAccountId, cancellationToken);
        if (!paidFromResult.Succeeded)
            return Fail<ExpenseDto>($"Paid-from account: {paidFromResult.Message}", paidFromResult.Code ?? 400);

        foreach (var line in expense.Lines)
        {
            var accountResult = await _helper.GetPostableAccountAsync(line.AccountId, cancellationToken);
            if (!accountResult.Succeeded)
                return Fail<ExpenseDto>($"Line {line.LineNumber}: {accountResult.Message}", accountResult.Code ?? 400);
        }

        var taxResult = await _helper.CalculateTaxAsync(
            expense.Lines.Select(l => new TaxCalculationLine { Amount = l.Amount, TaxCodeId = l.TaxCodeId }).ToList(),
            cancellationToken);
        if (!taxResult.Succeeded)
            return Fail<ExpenseDto>(taxResult.Message ?? "Tax calculation failed.", taxResult.Code ?? 400);
        var tax = taxResult.Data!;

        var subTotal = _helper.Round(expense.Lines.Sum(l => l.Amount));
        var total = subTotal + tax.TaxTotal;
        if (total <= 0)
            return Fail<ExpenseDto>("Expense total must be greater than zero.", 400);

        Account? taxAccount = null;
        if (tax.TaxTotal != 0)
        {
            var taxAccountResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.TaxReceivable, cancellationToken);
            if (!taxAccountResult.Succeeded)
                return Fail<ExpenseDto>(taxAccountResult.Message!, taxAccountResult.Code ?? 400);
            taxAccount = taxAccountResult.Data;
        }

        var entry = new JournalEntry
        {
            Status = JournalEntryStatus.Draft,
            PostingDate = expense.DocDate,
            Memo = string.IsNullOrWhiteSpace(expense.Memo) ? "Expense" : $"Expense: {expense.Memo}",
            Currency = expense.Currency,
            ExchangeRate = expense.ExchangeRate,
            SourceType = nameof(Expense),
            SourceId = expense.Id.ToString()
        };

        var lineNo = 1;
        foreach (var line in expense.Lines.OrderBy(l => l.LineNumber))
        {
            entry.Lines.Add(new JournalLine
            {
                LineNumber = lineNo++,
                AccountId = line.AccountId,
                TxnDebit = line.Amount,
                Currency = expense.Currency,
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
                Currency = expense.Currency,
                Memo = component.RateName
            });
        }

        entry.Lines.Add(new JournalLine
        {
            LineNumber = lineNo,
            AccountId = expense.PaidFromAccountId,
            TxnCredit = total,
            Currency = expense.Currency
        });

        Result postResult;
        try
        {
            postResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var engineResult = await _engine.PostAsync(entry, ct);
                if (!engineResult.Succeeded)
                    return engineResult;

                await _entryRepository.InsertAsync(entry, ct);

                expense.Number = await _numberService.NextFormattedAsync(
                    nameof(Expense), _options.ExpenseNumberPrefix, _options.JournalNumberPadding, ct);
                expense.Status = FinanceDocumentStatus.Posted;
                expense.SubTotal = subTotal;
                expense.TaxTotal = tax.TaxTotal;
                expense.Total = total;
                expense.ExchangeRate = entry.ExchangeRate;
                expense.BaseTotal = entry.Lines.First(l => l.AccountId == expense.PaidFromAccountId).Credit;
                expense.JournalEntryId = entry.Id;
                await _expenseRepository.UpdateAsync(expense, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ExpenseDto>("The expense was modified by another operation. Reload and retry.", 409);
        }

        if (!postResult.Succeeded)
            return Fail<ExpenseDto>(postResult.Message ?? "Posting failed.", postResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentPostedEvent
        {
            DocType = nameof(Expense),
            DocId = expense.Id,
            Number = expense.Number!,
            JournalEntryId = entry.Id,
            DocDate = expense.DocDate,
            Total = expense.Total,
            TenantId = expense.TenantId
        }, cancellationToken);

        return await GetAsync(expense.Id, cancellationToken);
    }

    public async Task<Result<ExpenseDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var expense = await _expenseRepository.AsQueryable(true)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (expense == null)
            return Fail<ExpenseDto>("Expense not found.", 404);
        if (expense.Status != FinanceDocumentStatus.Posted)
            return Fail<ExpenseDto>("Only posted expenses can be voided.", 409);

        var original = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == expense.JournalEntryId, cancellationToken);
        if (original == null)
            return Fail<ExpenseDto>("The posting journal entry was not found.", 500);

        JournalEntry? reversal = null;
        Result voidResult;
        try
        {
            voidResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var buildResult = await _engine.BuildReversalAsync(original, original.PostingDate, $"Void {expense.Number}", ct);
                if (!buildResult.Succeeded)
                    return Result.Failure(buildResult.Message ?? "Void failed.", buildResult.Code ?? 400);

                reversal = buildResult.Data!;
                await _entryRepository.InsertAsync(reversal, ct);

                original.Status = JournalEntryStatus.Reversed;
                original.ReversedByEntryId = reversal.Id;
                await _entryRepository.UpdateAsync(original, ct);

                expense.Status = FinanceDocumentStatus.Voided;
                expense.VoidJournalEntryId = reversal.Id;
                await _expenseRepository.UpdateAsync(expense, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<ExpenseDto>("The expense was modified by another operation. Reload and retry.", 409);
        }

        if (!voidResult.Succeeded)
            return Fail<ExpenseDto>(voidResult.Message ?? "Void failed.", voidResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentVoidedEvent
        {
            DocType = nameof(Expense),
            DocId = expense.Id,
            Number = expense.Number,
            VoidJournalEntryId = reversal!.Id,
            TenantId = expense.TenantId
        }, cancellationToken);

        return await GetAsync(expense.Id, cancellationToken);
    }

    private async Task<Result> ApplyDraftAsync(Expense expense, CreateExpenseDto input, CancellationToken cancellationToken)
    {
        if (input.Lines == null || input.Lines.Count == 0)
            return Fail("At least one line is required.");
        if (input.Lines.Count > _options.MaxLinesPerEntry)
            return Fail($"Too many lines (max {_options.MaxLinesPerEntry}).");

        if (input.VendorId.HasValue &&
            !await _vendorRepository.AnyAsync(v => v.Id == input.VendorId.Value, cancellationToken))
        {
            return Fail("Vendor not found.", 404);
        }

        var paidFromResult = await _helper.GetPostableAccountAsync(input.PaidFromAccountId, cancellationToken);
        if (!paidFromResult.Succeeded)
            return Fail($"Paid-from account: {paidFromResult.Message}", paidFromResult.Code ?? 400);

        expense.VendorId = input.VendorId;
        expense.PaidFromAccountId = input.PaidFromAccountId;
        expense.DocDate = input.DocDate.ToUtcDate();
        expense.Currency = _helper.NormalizeCurrency(input.Currency);
        expense.ExchangeRate = input.ExchangeRate ?? 0m;
        expense.Memo = input.Memo;

        expense.Lines.Clear();
        var lineNo = 1;
        foreach (var line in input.Lines)
        {
            if (line.Amount <= 0)
                return Fail($"Line {lineNo}: amount must be greater than zero.");

            expense.Lines.Add(new ExpenseLine
            {
                ExpenseId = expense.Id,
                LineNumber = lineNo++,
                Description = line.Description,
                AccountId = line.AccountId,
                Amount = _helper.Round(line.Amount),
                TaxCodeId = line.TaxCodeId
            });
        }

        expense.SubTotal = _helper.Round(expense.Lines.Sum(l => l.Amount));
        var draftTax = await _helper.CalculateTaxAsync(
            expense.Lines.Select(l => new TaxCalculationLine { Amount = l.Amount, TaxCodeId = l.TaxCodeId }).ToList(),
            cancellationToken);
        if (!draftTax.Succeeded)
            return Fail(draftTax.Message ?? "Tax calculation failed.", draftTax.Code ?? 400);

        expense.TaxTotal = draftTax.Data!.TaxTotal;
        expense.Total = expense.SubTotal + expense.TaxTotal;
        return Ok();
    }
}
