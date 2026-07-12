namespace Tnzi.Finance.Services;

/// <summary>
/// 资金划转单服务
/// </summary>
/// <remarks>
/// 过账规则：借 转入科目 / 贷 转出科目（同交易币同额，引擎负责本位币换算与容差配平）。
/// 双方科目须为可过账的资金叶子（CashFlowActivity = CashEquivalent）；
/// 首版要求两科目与交易币种兼容（科目限定币种时须相等），跨币种换汇划转留待后续版本。
/// </remarks>
public class TransferService : ApplicationService, ITransferService
{
    private readonly IRepository<Transfer, Guid> _transferRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IDocumentNumberService _numberService;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceDocumentHelper _helper;
    private readonly PostingGuardRunner _guards;
    private readonly FinanceOptions _options;

    public TransferService(
        IServiceProvider serviceProvider,
        IRepository<Transfer, Guid> transferRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IDocumentNumberService numberService,
        LedgerPostingEngine engine,
        FinanceDocumentHelper helper,
        PostingGuardRunner guards,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _transferRepository = Check.NotNull(transferRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _numberService = Check.NotNull(numberService);
        _engine = Check.NotNull(engine);
        _helper = Check.NotNull(helper);
        _guards = Check.NotNull(guards);
        _options = Check.NotNull(options).Value;
    }

    public async Task<Result<IPagedList<TransferDto>>> GetPagedAsync(TransferQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _transferRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(t => t.CreationTime)
            .ProjectTo<Transfer, TransferDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        await FillAccountNamesAsync(pagedList.Items, cancellationToken);
        return Ok(pagedList);
    }

    public async Task<Result<TransferDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transfer = await _transferRepository.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transfer == null)
            return Fail<TransferDto>("Transfer not found.", 404);

        var dto = transfer.MapTo<TransferDto>();
        await FillAccountNamesAsync(new List<TransferDto> { dto }, cancellationToken);
        return Ok(dto);
    }

    public async Task<Result<TransferDto>> CreateDraftAsync(CreateTransferDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var transfer = new Transfer();
        var applyResult = await ApplyDraftAsync(transfer, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<TransferDto>(applyResult.Message ?? "Invalid transfer.", applyResult.Code ?? 400);

        await _transferRepository.InsertAsync(transfer, cancellationToken);
        await _transferRepository.SaveChangesAsync(cancellationToken);

        return await GetAsync(transfer.Id, cancellationToken);
    }

    public async Task<Result<TransferDto>> UpdateDraftAsync(Guid id, CreateTransferDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var transfer = await _transferRepository.AsQueryable(true)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transfer == null)
            return Fail<TransferDto>("Transfer not found.", 404);
        if (transfer.Status != FinanceDocumentStatus.Draft)
            return Fail<TransferDto>("Only draft transfers can be edited.", 409);

        var applyResult = await ApplyDraftAsync(transfer, input, cancellationToken);
        if (!applyResult.Succeeded)
            return Fail<TransferDto>(applyResult.Message ?? "Invalid transfer.", applyResult.Code ?? 400);

        try
        {
            await _transferRepository.UpdateAsync(transfer, cancellationToken);
            await _transferRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<TransferDto>("The transfer was modified by another operation. Reload and retry.", 409);
        }

        return await GetAsync(transfer.Id, cancellationToken);
    }

    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transfer = await _transferRepository.AsQueryable(true)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transfer == null)
            return Fail("Transfer not found.", 404);
        if (transfer.Status != FinanceDocumentStatus.Draft)
            return Fail("Only draft transfers can be deleted. Posted transfers must be voided.", 409);

        await _transferRepository.DeleteAsync(transfer, cancellationToken);
        return Ok();
    }

    public async Task<Result<TransferDto>> PostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transfer = await _transferRepository.AsQueryable(true)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transfer == null)
            return Fail<TransferDto>("Transfer not found.", 404);
        if (transfer.Status != FinanceDocumentStatus.Draft)
            return Fail<TransferDto>("Only draft transfers can be posted.", 409);

        var guardResult = await _guards.CheckAsync(nameof(Transfer), transfer.Id.ToString(), FinancePostingOperation.Post, transfer, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<TransferDto>(guardResult.Message ?? "Posting was rejected.", guardResult.Code ?? 403);

        // 过账时重校验双方科目（草稿期间科目可能被改动/停用/取消资金分类）
        var accountsResult = await ValidateAccountsAsync(transfer.FromAccountId, transfer.ToAccountId, transfer.Currency, cancellationToken);
        if (!accountsResult.Succeeded)
            return Fail<TransferDto>(accountsResult.Message ?? "Invalid accounts.", accountsResult.Code ?? 400);

        var entry = new JournalEntry
        {
            Status = JournalEntryStatus.Draft,
            PostingDate = transfer.TransferDate,
            Memo = string.IsNullOrWhiteSpace(transfer.Memo) ? "Funds transfer" : transfer.Memo,
            Currency = transfer.Currency,
            ExchangeRate = transfer.ExchangeRate,
            SourceType = nameof(Transfer),
            SourceId = transfer.Id.ToString()
        };
        entry.Lines.Add(new JournalLine { LineNumber = 1, AccountId = transfer.ToAccountId, TxnDebit = transfer.Amount, Currency = transfer.Currency });
        entry.Lines.Add(new JournalLine { LineNumber = 2, AccountId = transfer.FromAccountId, TxnCredit = transfer.Amount, Currency = transfer.Currency });

        Result postResult;
        try
        {
            postResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var engineResult = await _engine.PostAsync(entry, ct);
                if (!engineResult.Succeeded)
                    return engineResult;

                await _entryRepository.InsertAsync(entry, ct);

                transfer.Number = await _numberService.NextFormattedAsync(
                    nameof(Transfer), _options.TransferNumberPrefix, _options.JournalNumberPadding, ct);
                transfer.Status = FinanceDocumentStatus.Posted;
                transfer.ExchangeRate = entry.ExchangeRate;
                transfer.BaseAmount = entry.Lines.First(l => l.AccountId == transfer.ToAccountId).Debit;
                transfer.JournalEntryId = entry.Id;
                await _transferRepository.UpdateAsync(transfer, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<TransferDto>("The transfer was modified by another operation. Reload and retry.", 409);
        }

        if (!postResult.Succeeded)
            return Fail<TransferDto>(postResult.Message ?? "Posting failed.", postResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentPostedEvent
        {
            DocType = nameof(Transfer),
            DocId = transfer.Id,
            Number = transfer.Number!,
            JournalEntryId = entry.Id,
            DocDate = transfer.TransferDate,
            Total = transfer.Amount,
            TenantId = transfer.TenantId
        }, cancellationToken);

        return await GetAsync(transfer.Id, cancellationToken);
    }

    public async Task<Result<TransferDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transfer = await _transferRepository.AsQueryable(true)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transfer == null)
            return Fail<TransferDto>("Transfer not found.", 404);
        if (transfer.Status != FinanceDocumentStatus.Posted)
            return Fail<TransferDto>("Only posted transfers can be voided.", 409);

        var guardResult = await _guards.CheckAsync(nameof(Transfer), transfer.Id.ToString(), FinancePostingOperation.Void, transfer, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<TransferDto>(guardResult.Message ?? "Void was rejected.", guardResult.Code ?? 403);

        var original = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == transfer.JournalEntryId, cancellationToken);
        if (original == null)
            return Fail<TransferDto>("The posting journal entry was not found.", 500);

        JournalEntry? reversal = null;
        Result voidResult;
        try
        {
            voidResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var buildResult = await _engine.BuildReversalAsync(original, original.PostingDate, $"Void {transfer.Number}", ct);
                if (!buildResult.Succeeded)
                    return Result.Failure(buildResult.Message ?? "Void failed.", buildResult.Code ?? 400);

                reversal = buildResult.Data!;
                await _entryRepository.InsertAsync(reversal, ct);

                original.Status = JournalEntryStatus.Reversed;
                original.ReversedByEntryId = reversal.Id;
                await _entryRepository.UpdateAsync(original, ct);

                transfer.Status = FinanceDocumentStatus.Voided;
                transfer.VoidJournalEntryId = reversal.Id;
                await _transferRepository.UpdateAsync(transfer, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<TransferDto>("The transfer was modified by another operation. Reload and retry.", 409);
        }

        if (!voidResult.Succeeded)
            return Fail<TransferDto>(voidResult.Message ?? "Void failed.", voidResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentVoidedEvent
        {
            DocType = nameof(Transfer),
            DocId = transfer.Id,
            Number = transfer.Number,
            VoidJournalEntryId = reversal!.Id,
            TenantId = transfer.TenantId
        }, cancellationToken);

        return await GetAsync(transfer.Id, cancellationToken);
    }

    private async Task<Result> ApplyDraftAsync(Transfer transfer, CreateTransferDto input, CancellationToken cancellationToken)
    {
        if (input.Amount <= 0)
            return Fail("Transfer amount must be greater than zero.");

        var currency = _helper.NormalizeCurrency(input.Currency);
        var accountsResult = await ValidateAccountsAsync(input.FromAccountId, input.ToAccountId, currency, cancellationToken);
        if (!accountsResult.Succeeded)
            return accountsResult;

        transfer.FromAccountId = input.FromAccountId;
        transfer.ToAccountId = input.ToAccountId;
        transfer.TransferDate = input.TransferDate.ToUtcDate();
        transfer.Currency = currency;
        transfer.ExchangeRate = input.ExchangeRate ?? 0m;
        transfer.Amount = _helper.Round(input.Amount);
        transfer.Reference = input.Reference;
        transfer.Memo = input.Memo;
        return Ok();
    }

    /// <summary>
    /// 双方须为不同的可过账资金叶子科目（判据统一收口在 FinanceDocumentHelper.GetFundsAccountAsync），
    /// 且科目限定币种时须与交易币种一致（跨币种换汇划转留待后续版本）
    /// </summary>
    private async Task<Result> ValidateAccountsAsync(Guid fromAccountId, Guid toAccountId, string currency, CancellationToken cancellationToken)
    {
        if (fromAccountId == toAccountId)
            return Fail("The source and destination accounts must be different.");

        foreach (var (accountId, label) in new[] { (fromAccountId, "Source"), (toAccountId, "Destination") })
        {
            var accountResult = await _helper.GetFundsAccountAsync(accountId, currency, cancellationToken);
            if (!accountResult.Succeeded)
                return Fail($"{label} account: {accountResult.Message}", accountResult.Code ?? 400);
        }

        return Ok();
    }

    private async Task FillAccountNamesAsync(IList<TransferDto> items, CancellationToken cancellationToken)
    {
        var accountIds = items.SelectMany(t => new[] { t.FromAccountId, t.ToAccountId }).Distinct().ToList();
        if (accountIds.Count == 0)
            return;

        var names = await _accountRepository.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.Name })
            .ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Name}", cancellationToken);

        foreach (var dto in items)
        {
            dto.FromAccountName = names.GetValueOrDefault(dto.FromAccountId);
            dto.ToAccountName = names.GetValueOrDefault(dto.ToAccountId);
        }
    }
}
