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

        var guardResult = await _guards.CheckAsync(FinanceSourceTypes.Transfer, transfer.Id.ToString(), FinancePostingOperation.Post, transfer, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<TransferDto>(guardResult.Message ?? "Posting was rejected.", guardResult.Code ?? 403);

        var crossCurrency = IsCrossCurrency(transfer);

        // 过账时重校验双方科目（草稿期间科目可能被改动/停用/取消资金分类）
        var toCurrency = crossCurrency ? transfer.TargetCurrency! : transfer.Currency;
        var accountsResult = await ValidateAccountsAsync(transfer.FromAccountId, transfer.Currency, transfer.ToAccountId, toCurrency, cancellationToken);
        if (!accountsResult.Succeeded)
            return Fail<TransferDto>(accountsResult.Message ?? "Invalid accounts.", accountsResult.Code ?? 400);

        Result postResult;
        try
        {
            postResult = crossCurrency
                ? await PostCrossCurrencyAsync(transfer, cancellationToken)
                : await PostSameCurrencyAsync(transfer, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<TransferDto>("The transfer was modified by another operation. Reload and retry.", 409);
        }

        if (!postResult.Succeeded)
            return Fail<TransferDto>(postResult.Message ?? "Posting failed.", postResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentPostedEvent
        {
            DocType = FinanceSourceTypes.Transfer,
            DocId = transfer.Id,
            Number = transfer.Number!,
            JournalEntryId = transfer.JournalEntryId!.Value,
            DocDate = transfer.TransferDate,
            Total = transfer.Amount,
            TenantId = transfer.TenantId
        }, cancellationToken);

        return await GetAsync(transfer.Id, cancellationToken);
    }

    /// <summary>
    /// 同币种过账：一张凭证，借 转入科目 / 贷 转出科目（同交易币同额）。
    /// 单张凭证的缓冲式流程——引擎失败在触碰实体前返回，无需 UnitOfWorkAbortException。
    /// </summary>
    private async Task<Result> PostSameCurrencyAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        var entry = BuildTransferEntry(transfer, transfer.Currency, transfer.ExchangeRate,
            string.IsNullOrWhiteSpace(transfer.Memo) ? "Funds transfer" : transfer.Memo);
        entry.Lines.Add(new JournalLine { LineNumber = 1, AccountId = transfer.ToAccountId, TxnDebit = transfer.Amount, Currency = transfer.Currency });
        entry.Lines.Add(new JournalLine { LineNumber = 2, AccountId = transfer.FromAccountId, TxnCredit = transfer.Amount, Currency = transfer.Currency });

        return await ExecuteInUnitOfWorkAsync<Result>(async ct =>
        {
            var engineResult = await _engine.PostAsync(entry, ct);
            if (!engineResult.Succeeded)
                return engineResult;

            await _entryRepository.InsertAsync(entry, ct);

            transfer.Number = await _numberService.NextFormattedAsync(
                FinanceSourceTypes.Transfer, _options.TransferNumberPrefix, _options.JournalNumberPadding, ct);
            transfer.Status = FinanceDocumentStatus.Posted;
            transfer.ExchangeRate = entry.ExchangeRate;
            transfer.BaseAmount = entry.Lines.First(l => l.AccountId == transfer.ToAccountId).Debit;
            transfer.JournalEntryId = entry.Id;
            await _transferRepository.UpdateAsync(transfer, ct);

            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>
    /// 跨币种过账（路线 C）：三张单币凭证经换汇过渡科目在同工作单元内精确归零。
    /// ①凭证1（转出币）Cr From / Dr Clearing；②凭证2（转入币）Dr To / Cr Clearing；
    /// ③residual = 凭证1本位 − 凭证2本位，≠0 时凭证3（本位币）记 residual 到汇兑损益 + 归零过渡科目。
    /// 多写入循环：首次引擎过账（分配凭证号）之后的失败 MUST 抛 UnitOfWorkAbortException 以整体回滚。
    /// </summary>
    private async Task<Result> PostCrossCurrencyAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        // 过渡科目须无币种限定（收转出币/转入币/本位币三种行）；汇兑损益科目须可解析（提交前返回，安全）
        var clearingResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.CurrencyExchangeClearing, cancellationToken);
        if (!clearingResult.Succeeded)
            return clearingResult;
        var clearing = clearingResult.Data!;
        if (clearing.Currency != null)
            return Result.Failure($"The currency exchange clearing account '{clearing.Code}' must not be restricted to a single currency.", 400);

        var fxResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.ExchangeGainLoss, cancellationToken);
        if (!fxResult.Succeeded)
            return fxResult;
        var fx = fxResult.Data!;

        var baseCurrency = _helper.NormalizeCurrency(null);
        var memo = string.IsNullOrWhiteSpace(transfer.Memo) ? "Currency exchange transfer" : transfer.Memo;
        var targetAmount = transfer.TargetAmount!.Value;
        var targetCurrency = transfer.TargetCurrency!;

        try
        {
            return await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                // 凭证1（转出币）：Cr From / Dr Clearing
                var outEntry = BuildTransferEntry(transfer, transfer.Currency, transfer.ExchangeRate, $"{memo} (out)");
                outEntry.Lines.Add(new JournalLine { LineNumber = 1, AccountId = clearing.Id, TxnDebit = transfer.Amount, Currency = transfer.Currency });
                outEntry.Lines.Add(new JournalLine { LineNumber = 2, AccountId = transfer.FromAccountId, TxnCredit = transfer.Amount, Currency = transfer.Currency });
                var r1 = await _engine.PostAsync(outEntry, ct);
                if (!r1.Succeeded)
                    throw new UnitOfWorkAbortException(r1);
                await _entryRepository.InsertAsync(outEntry, ct);

                // 凭证2（转入币）：Dr To / Cr Clearing
                var inEntry = BuildTransferEntry(transfer, targetCurrency, transfer.TargetExchangeRate, $"{memo} (in)");
                inEntry.Lines.Add(new JournalLine { LineNumber = 1, AccountId = transfer.ToAccountId, TxnDebit = targetAmount, Currency = targetCurrency });
                inEntry.Lines.Add(new JournalLine { LineNumber = 2, AccountId = clearing.Id, TxnCredit = targetAmount, Currency = targetCurrency });
                var r2 = await _engine.PostAsync(inEntry, ct);
                if (!r2.Succeeded)
                    throw new UnitOfWorkAbortException(r2);
                await _entryRepository.InsertAsync(inEntry, ct);

                var baseOut = outEntry.Lines.First(l => l.AccountId == clearing.Id).Debit;
                var baseIn = inEntry.Lines.First(l => l.AccountId == clearing.Id).Credit;
                var residual = baseOut - baseIn;

                JournalEntry? fxEntry = null;
                if (residual != 0m)
                {
                    // residual > 0：转出本位价值 > 转入 → 汇兑损失（Dr FX / Cr Clearing）；residual < 0 镜像
                    var magnitude = Math.Abs(residual);
                    fxEntry = BuildTransferEntry(transfer, baseCurrency, 1m, $"{memo} (fx)");
                    if (residual > 0m)
                    {
                        fxEntry.Lines.Add(new JournalLine { LineNumber = 1, AccountId = fx.Id, TxnDebit = magnitude, Currency = baseCurrency });
                        fxEntry.Lines.Add(new JournalLine { LineNumber = 2, AccountId = clearing.Id, TxnCredit = magnitude, Currency = baseCurrency });
                    }
                    else
                    {
                        fxEntry.Lines.Add(new JournalLine { LineNumber = 1, AccountId = clearing.Id, TxnDebit = magnitude, Currency = baseCurrency });
                        fxEntry.Lines.Add(new JournalLine { LineNumber = 2, AccountId = fx.Id, TxnCredit = magnitude, Currency = baseCurrency });
                    }
                    var r3 = await _engine.PostAsync(fxEntry, ct);
                    if (!r3.Succeeded)
                        throw new UnitOfWorkAbortException(r3);
                    await _entryRepository.InsertAsync(fxEntry, ct);
                }

                transfer.Number = await _numberService.NextFormattedAsync(
                    FinanceSourceTypes.Transfer, _options.TransferNumberPrefix, _options.JournalNumberPadding, ct);
                transfer.Status = FinanceDocumentStatus.Posted;
                transfer.ExchangeRate = outEntry.ExchangeRate;
                transfer.BaseAmount = baseOut;
                transfer.TargetExchangeRate = inEntry.ExchangeRate;
                transfer.TargetBaseAmount = baseIn;
                transfer.JournalEntryId = outEntry.Id;
                transfer.TargetJournalEntryId = inEntry.Id;
                transfer.FxJournalEntryId = fxEntry?.Id;
                await _transferRepository.UpdateAsync(transfer, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (UnitOfWorkAbortException ex)
        {
            return ex.Result;
        }
    }

    public async Task<Result<TransferDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transfer = await _transferRepository.AsQueryable(true)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transfer == null)
            return Fail<TransferDto>("Transfer not found.", 404);
        if (transfer.Status != FinanceDocumentStatus.Posted)
            return Fail<TransferDto>("Only posted transfers can be voided.", 409);

        var guardResult = await _guards.CheckAsync(FinanceSourceTypes.Transfer, transfer.Id.ToString(), FinancePostingOperation.Void, transfer, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<TransferDto>(guardResult.Message ?? "Void was rejected.", guardResult.Code ?? 403);

        // 按 (SourceType, SourceId, Posted) 反查该划转过账的全部凭证（同币种 1 张 / 跨币种 2~3 张），逐张冲销
        var sourceId = transfer.Id.ToString();
        var vouchers = await _entryRepository.AsQueryable(true)
            .Include(e => e.Lines)
            .Where(e => e.SourceType == FinanceSourceTypes.Transfer && e.SourceId == sourceId && e.Status == JournalEntryStatus.Posted)
            .ToListAsync(cancellationToken);
        if (vouchers.Count == 0)
            return Fail<TransferDto>("The posting journal entries were not found.", 500);

        var primaryReversalId = Guid.Empty;
        Result voidResult;
        try
        {
            voidResult = await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                // 多写入循环：每次 BuildReversalAsync 分配冲销凭证号，任一失败以 UnitOfWorkAbortException 整体回滚
                foreach (var original in vouchers)
                {
                    var buildResult = await _engine.BuildReversalAsync(original, original.PostingDate, $"Void {transfer.Number}", ct);
                    if (!buildResult.Succeeded)
                        throw new UnitOfWorkAbortException(Result.Failure(buildResult.Message ?? "Void failed.", buildResult.Code ?? 400));

                    var reversal = buildResult.Data!;
                    await _entryRepository.InsertAsync(reversal, ct);

                    original.Status = JournalEntryStatus.Reversed;
                    original.ReversedByEntryId = reversal.Id;
                    await _entryRepository.UpdateAsync(original, ct);

                    if (original.Id == transfer.JournalEntryId)
                        primaryReversalId = reversal.Id;
                }

                transfer.Status = FinanceDocumentStatus.Voided;
                transfer.VoidJournalEntryId = primaryReversalId == Guid.Empty ? null : primaryReversalId;
                await _transferRepository.UpdateAsync(transfer, ct);

                return Result.Success();
            }, cancellationToken);
        }
        catch (UnitOfWorkAbortException ex)
        {
            return Fail<TransferDto>(ex.Result.Message ?? "Void failed.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<TransferDto>("The transfer was modified by another operation. Reload and retry.", 409);
        }

        if (!voidResult.Succeeded)
            return Fail<TransferDto>(voidResult.Message ?? "Void failed.", voidResult.Code ?? 400);

        await PublishEventAsync(new FinanceDocumentVoidedEvent
        {
            DocType = FinanceSourceTypes.Transfer,
            DocId = transfer.Id,
            Number = transfer.Number,
            VoidJournalEntryId = transfer.VoidJournalEntryId ?? primaryReversalId,
            TenantId = transfer.TenantId
        }, cancellationToken);

        return await GetAsync(transfer.Id, cancellationToken);
    }

    private async Task<Result> ApplyDraftAsync(Transfer transfer, CreateTransferDto input, CancellationToken cancellationToken)
    {
        if (input.Amount <= 0)
            return Fail("Transfer amount must be greater than zero.");

        var currency = _helper.NormalizeCurrency(input.Currency);
        var targetCurrency = string.IsNullOrWhiteSpace(input.TargetCurrency)
            ? null
            : input.TargetCurrency.Trim().ToUpperInvariant();
        var crossCurrency = targetCurrency != null && !string.Equals(targetCurrency, currency, StringComparison.OrdinalIgnoreCase);

        // 转入侧币种（跨币种模式 = TargetCurrency；同币种模式 = 转出币）
        var toCurrency = crossCurrency ? targetCurrency! : currency;

        if (!crossCurrency)
        {
            // 同币种模式：Target* 字段必须为空，显式传值 400（完全后向兼容）
            if (input.TargetAmount.HasValue || input.TargetExchangeRate.HasValue)
                return Fail("Target amount and target exchange rate are only valid for a cross-currency transfer whose TargetCurrency differs from Currency.");
        }
        else
        {
            // 跨币种模式：转入金额必填且为正
            if (!input.TargetAmount.HasValue || input.TargetAmount.Value <= 0)
                return Fail("A cross-currency transfer requires a target amount greater than zero.");
        }

        var accountsResult = await ValidateAccountsAsync(input.FromAccountId, currency, input.ToAccountId, toCurrency, cancellationToken);
        if (!accountsResult.Succeeded)
            return accountsResult;

        transfer.FromAccountId = input.FromAccountId;
        transfer.ToAccountId = input.ToAccountId;
        transfer.TransferDate = input.TransferDate.ToUtcDate();
        transfer.Currency = currency;
        transfer.ExchangeRate = input.ExchangeRate ?? 0m;
        transfer.Amount = _helper.Round(input.Amount);
        transfer.TargetCurrency = crossCurrency ? targetCurrency : null;
        transfer.TargetAmount = crossCurrency ? _helper.Round(input.TargetAmount!.Value) : null;
        transfer.TargetExchangeRate = crossCurrency ? input.TargetExchangeRate ?? 0m : 0m;
        transfer.Reference = input.Reference;
        transfer.Memo = input.Memo;
        return Ok();
    }

    /// <summary>
    /// 双方须为不同的可过账资金叶子科目（判据统一收口在 FinanceDocumentHelper.GetFundsAccountAsync），
    /// 各自与其侧币种兼容（同币种模式两侧同币；跨币种模式转出侧 = Currency、转入侧 = TargetCurrency）
    /// </summary>
    private async Task<Result> ValidateAccountsAsync(Guid fromAccountId, string fromCurrency, Guid toAccountId, string toCurrency, CancellationToken cancellationToken)
    {
        if (fromAccountId == toAccountId)
            return Fail("The source and destination accounts must be different.");

        var fromResult = await _helper.GetFundsAccountAsync(fromAccountId, fromCurrency, cancellationToken);
        if (!fromResult.Succeeded)
            return Fail($"Source account: {fromResult.Message}", fromResult.Code ?? 400);

        var toResult = await _helper.GetFundsAccountAsync(toAccountId, toCurrency, cancellationToken);
        if (!toResult.Succeeded)
            return Fail($"Destination account: {toResult.Message}", toResult.Code ?? 400);

        return Ok();
    }

    /// <summary>跨币种模式判据：转入侧币种非空且与转出侧不同</summary>
    private static bool IsCrossCurrency(Transfer transfer)
        => transfer.TargetCurrency != null && !string.Equals(transfer.TargetCurrency, transfer.Currency, StringComparison.OrdinalIgnoreCase);

    /// <summary>构建一张过账用凭证草稿（回链本划转单）</summary>
    private JournalEntry BuildTransferEntry(Transfer transfer, string currency, decimal exchangeRate, string memo)
        => new()
        {
            Status = JournalEntryStatus.Draft,
            PostingDate = transfer.TransferDate,
            Memo = memo,
            Currency = currency,
            ExchangeRate = exchangeRate,
            SourceType = FinanceSourceTypes.Transfer,
            SourceId = transfer.Id.ToString()
        };

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
