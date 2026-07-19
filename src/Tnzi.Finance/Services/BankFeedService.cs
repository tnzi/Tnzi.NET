using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Tnzi.Finance.Services;

/// <summary>
/// 银行流水导入与匹配服务
/// </summary>
/// <remarks>
/// 导入去重 → 匹配引擎建议 → 确认在当前 Draft 对账生成勾选行（ReconciliationService 零改动）。
/// 匹配/确认限本位币科目（外币可导入，suggest/confirm 返回 400）。确认的并发由
/// <see cref="ReconciliationLine"/>.JournalLineId 全局唯一索引兜底（catch 包住 InsertAsync 翻译 409）。
/// </remarks>
public class BankFeedService : ApplicationService, IBankFeedService
{
    private readonly IRepository<BankTransaction, Guid> _txnRepository;
    private readonly IRepository<BankImportBatch, Guid> _batchRepository;
    private readonly IRepository<ReconciliationLine, Guid> _reconLineRepository;
    private readonly IRepository<BankAccount, Guid> _bankAccountRepository;
    private readonly IRepository<Reconciliation, Guid> _reconRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly FinanceDocumentHelper _helper;
    private readonly BankMatchEngine _engine;
    private readonly IExpenseService _expenseService;
    private readonly IPaymentEntryService _paymentEntryService;
    private readonly ITransferService _transferService;
    private readonly IEnumerable<IBankFeedProvider> _providers;
    private readonly FinanceOptions _options;

    public BankFeedService(
        IServiceProvider serviceProvider,
        IRepository<BankTransaction, Guid> txnRepository,
        IRepository<BankImportBatch, Guid> batchRepository,
        IRepository<ReconciliationLine, Guid> reconLineRepository,
        IRepository<BankAccount, Guid> bankAccountRepository,
        IRepository<Reconciliation, Guid> reconRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        FinanceDocumentHelper helper,
        BankMatchEngine engine,
        IExpenseService expenseService,
        IPaymentEntryService paymentEntryService,
        ITransferService transferService,
        IOptionsSnapshot<FinanceOptions> options,
        IEnumerable<IBankFeedProvider>? providers = null)
        : base(serviceProvider)
    {
        _txnRepository = Check.NotNull(txnRepository);
        _batchRepository = Check.NotNull(batchRepository);
        _reconLineRepository = Check.NotNull(reconLineRepository);
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
        _reconRepository = Check.NotNull(reconRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _helper = Check.NotNull(helper);
        _engine = Check.NotNull(engine);
        _expenseService = Check.NotNull(expenseService);
        _paymentEntryService = Check.NotNull(paymentEntryService);
        _transferService = Check.NotNull(transferService);
        _options = Check.NotNull(options).Value;
        _providers = providers ?? Enumerable.Empty<IBankFeedProvider>();
    }

    private string BaseCurrency => _helper.NormalizeCurrency(null);

    private bool IsForeignAccount(string? accountCurrency)
        => !string.IsNullOrEmpty(accountCurrency) &&
           !string.Equals(accountCurrency.Trim(), BaseCurrency, StringComparison.OrdinalIgnoreCase);

    public async Task<Result<IPagedList<BankTransactionDto>>> GetPagedAsync(BankTransactionQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _txnRepository.AsNoTracking();
        if (query.AccountId.HasValue)
            queryable = queryable.Where(t => t.AccountId == query.AccountId.Value);
        if (query.ImportBatchId.HasValue)
            queryable = queryable.Where(t => t.ImportBatchId == query.ImportBatchId.Value);
        if (query.Status.HasValue)
            queryable = queryable.Where(t => t.Status == query.Status.Value);
        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.ToUtcDate();
            queryable = queryable.Where(t => t.TxnDate >= from);
        }
        if (query.DateTo.HasValue)
        {
            var toExclusive = query.DateTo.Value.ToUtcDate().AddDays(1);
            queryable = queryable.Where(t => t.TxnDate < toExclusive);
        }
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            queryable = queryable.Where(t =>
                (t.Description != null && t.Description.ToLower().Contains(keyword)) ||
                (t.Payee != null && t.Payee.ToLower().Contains(keyword)) ||
                (t.Reference != null && t.Reference.ToLower().Contains(keyword)));
        }

        var pagedList = await queryable
            .OrderByDescending(t => t.TxnDate)
            .ThenByDescending(t => t.CreationTime)
            .ProjectTo<BankTransaction, BankTransactionDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<BankImportResultDto>> ImportStatementAsync(Guid accountId, BankTransactionSource source, string? fileName, string content, CsvMappingDto? mapping, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Fail<BankImportResultDto>("The statement file is empty.", 400);

        var accountResult = await _helper.GetFundsAccountAsync(accountId, requiredCurrency: null, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail<BankImportResultDto>(accountResult.Message!, accountResult.Code ?? 400);
        var account = accountResult.Data!;

        BankStatementParseResult parsed;
        try
        {
            parsed = source switch
            {
                BankTransactionSource.Ofx => OfxStatementParser.Parse(content),
                BankTransactionSource.Csv => CsvStatementParser.Parse(content, mapping ?? throw new BusinessException("A CSV column mapping is required.")),
                _ => throw new BusinessException("Only OFX and CSV files can be imported. Use the provider pull for feed sources.")
            };
        }
        catch (BusinessException ex)
        {
            return Fail<BankImportResultDto>(ex.Message, ex.HttpStatusCode);
        }

        if (parsed.Transactions.Count > _options.BankImportMaxRows)
            return Fail<BankImportResultDto>($"The statement has {parsed.Transactions.Count} rows, exceeding the import limit of {_options.BankImportMaxRows}.", 400);

        var defaultCurrency = _helper.NormalizeCurrency(account.Currency);

        // 账号/币种交叉校验：OFX 携带 ACCTID/CURDEF 正是为让客户端确认对账单属于目标账户与币种。
        // 防把 A 账户的对账单导进 B 账户台账（仅当账户档案配了 ExternalAccountId 时可判），
        // 或把外币对账单导进本位币账户（否则外币流水会对着本位币 GL 行清算）。
        var profile = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.AccountId == accountId, cancellationToken);
        if (profile != null && !string.IsNullOrWhiteSpace(profile.ExternalAccountId) && !string.IsNullOrWhiteSpace(parsed.StatementAccountId)
            && !string.Equals(profile.ExternalAccountId.Trim(), parsed.StatementAccountId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Fail<BankImportResultDto>($"The statement is for account '{parsed.StatementAccountId}', which does not match this account's configured account id. Import it into the correct account.", 400);
        }
        if (!string.IsNullOrWhiteSpace(parsed.Currency)
            && !string.Equals(parsed.Currency.Trim(), defaultCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Fail<BankImportResultDto>($"The statement currency '{parsed.Currency}' does not match the account currency '{defaultCurrency}'. Import it into a matching-currency account.", 400);
        }

        var batch = new BankImportBatch
        {
            AccountId = accountId,
            Source = source,
            FileName = fileName,
            PeriodFrom = parsed.PeriodFrom,
            PeriodTo = parsed.PeriodTo,
            StatementEndBalance = parsed.LedgerBalance
        };
        await _batchRepository.InsertAsync(batch, cancellationToken);
        await _batchRepository.SaveChangesAsync(cancellationToken);

        var (imported, skipped) = await PersistDeduplicatedTransactionsAsync(
            accountId, batch.Id, source, parsed.Transactions, defaultCurrency, cancellationToken);

        batch.ImportedCount = imported;
        batch.SkippedCount = skipped;
        await _batchRepository.UpdateAsync(batch, cancellationToken);
        await _batchRepository.SaveChangesAsync(cancellationToken);

        await PublishEventAsync(new BankStatementImportedEvent
        {
            BatchId = batch.Id,
            AccountId = accountId,
            Source = source,
            ImportedCount = imported,
            SkippedCount = skipped,
            TenantId = batch.TenantId
        }, cancellationToken);

        return Ok(new BankImportResultDto { BatchId = batch.Id, ImportedCount = imported, SkippedCount = skipped });
    }

    public async Task<Result<BankImportResultDto>> PullFromProviderAsync(PullBankFeedDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var bankAccount = await _bankAccountRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.AccountId == input.AccountId, cancellationToken);
        if (bankAccount == null)
            return Fail<BankImportResultDto>("No bank account profile is configured for this ledger account.", 404);
        if (string.IsNullOrWhiteSpace(bankAccount.FeedProviderKey))
            return Fail<BankImportResultDto>("This bank account has no feed provider configured.", 400);

        var provider = _providers.FirstOrDefault(p => string.Equals(p.Key, bankAccount.FeedProviderKey, StringComparison.OrdinalIgnoreCase));
        if (provider == null)
            return Fail<BankImportResultDto>($"No bank feed provider is registered for key '{bankAccount.FeedProviderKey}'.", 400);

        var accountResult = await _helper.GetFundsAccountAsync(input.AccountId, requiredCurrency: null, cancellationToken);
        if (!accountResult.Succeeded)
            return Fail<BankImportResultDto>(accountResult.Message!, accountResult.Code ?? 400);
        var account = accountResult.Data!;

        BankFeedPullResult pull;
        try
        {
            pull = await provider.PullAsync(new BankFeedPullRequest(input.AccountId, bankAccount.ExternalAccountId, bankAccount.FeedCursor, bankAccount.LastFeedSyncTime), cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail<BankImportResultDto>($"The bank feed provider failed: {ex.Message}", 502);
        }

        if (pull.Transactions.Count > _options.BankImportMaxRows)
            return Fail<BankImportResultDto>($"The provider returned {pull.Transactions.Count} rows, exceeding the import limit of {_options.BankImportMaxRows}.", 400);

        var defaultCurrency = _helper.NormalizeCurrency(account.Currency);
        var batch = new BankImportBatch
        {
            AccountId = input.AccountId,
            Source = BankTransactionSource.Provider,
            StatementEndBalance = pull.LedgerBalance
        };
        await _batchRepository.InsertAsync(batch, cancellationToken);
        await _batchRepository.SaveChangesAsync(cancellationToken);

        var parsedList = pull.Transactions
            .Select(t => new ParsedBankTransaction(t.PostedDate, t.Amount, t.Currency, t.ExternalId, t.Description, t.Payee, t.Reference))
            .ToList();
        var (imported, skipped) = await PersistDeduplicatedTransactionsAsync(
            input.AccountId, batch.Id, BankTransactionSource.Provider, parsedList, defaultCurrency, cancellationToken);

        batch.ImportedCount = imported;
        batch.SkippedCount = skipped;
        await _batchRepository.UpdateAsync(batch, cancellationToken);

        // 成功后回写游标 / 同步时间
        bankAccount.FeedCursor = pull.NextCursor ?? bankAccount.FeedCursor;
        bankAccount.LastFeedSyncTime = DateTime.UtcNow;
        await _bankAccountRepository.UpdateAsync(bankAccount, cancellationToken);
        await _batchRepository.SaveChangesAsync(cancellationToken);

        await PublishEventAsync(new BankStatementImportedEvent
        {
            BatchId = batch.Id,
            AccountId = input.AccountId,
            Source = BankTransactionSource.Provider,
            ImportedCount = imported,
            SkippedCount = skipped,
            TenantId = batch.TenantId
        }, cancellationToken);

        return Ok(new BankImportResultDto { BatchId = batch.Id, ImportedCount = imported, SkippedCount = skipped });
    }

    public async Task<Result<BankSuggestResultDto>> SuggestMatchesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account == null)
            return Fail<BankSuggestResultDto>("Account not found.", 404);
        if (IsForeignAccount(account.Currency))
            return Fail<BankSuggestResultDto>("Automatic matching is limited to base-currency accounts in this version.", 400);

        var pending = await _txnRepository.AsQueryable(true)
            .Where(t => t.AccountId == accountId && t.Status == BankTransactionStatus.Pending)
            .OrderBy(t => t.TxnDate)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
            return Ok(new BankSuggestResultDto());

        var autoConfirm = _options.BankFeedAutoConfirmExactMatches;
        Reconciliation? draft = null;
        if (autoConfirm)
            draft = await _reconRepository.AsNoTracking().FirstOrDefaultAsync(r => r.AccountId == accountId && r.Status == ReconciliationStatus.Draft, cancellationToken);

        var summary = new BankSuggestResultDto { Evaluated = pending.Count };
        var assigned = new HashSet<Guid>();

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                foreach (var txn in pending)
                {
                    var suggestion = await _engine.SuggestAsync(txn, ct);
                    if (suggestion == null || assigned.Contains(suggestion.JournalLineId))
                    {
                        txn.SuggestedJournalLineId = null;
                        txn.MatchConfidence = null;
                        txn.MatchRule = null;
                        await _txnRepository.UpdateAsync(txn, ct);
                        continue;
                    }

                    if (autoConfirm && draft != null && suggestion.Rule == "exact-ref")
                    {
                        var line = new ReconciliationLine
                        {
                            ReconciliationId = draft.Id,
                            JournalLineId = suggestion.JournalLineId,
                            TenantId = draft.TenantId
                        };
                        await _reconLineRepository.InsertAsync(line, ct);

                        txn.Status = BankTransactionStatus.Matched;
                        txn.MatchedJournalLineId = suggestion.JournalLineId;
                        txn.ReconciliationLineId = line.Id;
                        txn.MatchConfidence = suggestion.Confidence;
                        txn.MatchRule = suggestion.Rule;
                        txn.SuggestedJournalLineId = null;
                        await _txnRepository.UpdateAsync(txn, ct);

                        assigned.Add(suggestion.JournalLineId);
                        summary.AutoConfirmed++;
                    }
                    else
                    {
                        txn.SuggestedJournalLineId = suggestion.JournalLineId;
                        txn.MatchConfidence = suggestion.Confidence;
                        txn.MatchRule = suggestion.Rule;
                        await _txnRepository.UpdateAsync(txn, ct);
                        summary.Suggested++;
                    }
                }

                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<BankSuggestResultDto>("A suggested line was cleared concurrently. Reload and retry.", 409);
        }

        return Ok(summary);
    }

    public async Task<Result<List<BankMatchCandidateDto>>> GetCandidatesAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
    {
        var txn = await _txnRepository.AsNoTracking().FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<List<BankMatchCandidateDto>>("Bank transaction not found.", 404);

        var account = await _accountRepository.AsNoTracking().FirstOrDefaultAsync(a => a.Id == txn.AccountId, cancellationToken);
        if (account != null && IsForeignAccount(account.Currency))
            return Fail<List<BankMatchCandidateDto>>("Matching is limited to base-currency accounts in this version.", 400);

        var candidates = await _engine.GetCandidatesAsync(txn.AccountId, txn.Amount, cancellationToken);
        var list = candidates.Select(c => new BankMatchCandidateDto
        {
            JournalLineId = c.JournalLineId,
            JournalEntryId = c.JournalEntryId,
            EntryNumber = c.EntryNumber,
            PostingDate = c.PostingDate,
            Memo = c.Memo,
            Amount = c.NetAmount
        }).ToList();

        return Ok(list);
    }

    public async Task<Result<BankTransactionDto>> ConfirmMatchAsync(Guid bankTransactionId, ConfirmBankMatchDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        if (txn.Status != BankTransactionStatus.Pending)
            return Fail<BankTransactionDto>("Only pending transactions can be matched.", 409);

        var account = await _accountRepository.AsNoTracking().FirstOrDefaultAsync(a => a.Id == txn.AccountId, cancellationToken);
        if (account != null && IsForeignAccount(account.Currency))
            return Fail<BankTransactionDto>("Matching is limited to base-currency accounts in this version.", 400);

        var journalLineId = input.JournalLineId ?? txn.SuggestedJournalLineId;
        if (journalLineId == null)
            return Fail<BankTransactionDto>("No journal line was provided and there is no suggested match.", 400);

        // 用引擎候选集重校验：命中即行属于科目、已过账、未 cleared、未被占用、金额精确相等
        var candidates = await _engine.GetCandidatesAsync(txn.AccountId, txn.Amount, cancellationToken);
        if (candidates.All(c => c.JournalLineId != journalLineId.Value))
            return Fail<BankTransactionDto>("The selected journal line is not a valid, unmatched candidate for this transaction.", 400);

        // tracked 加载：确认时同 UoW 内 bump 父对账的乐观戳，与 CompleteAsync 的父行更新互斥，
        // 杜绝"读到 Draft → 并发完成 → 再插勾选行进已完成对账"的 TOCTOU（累计 cleared 锚点被静默漂移）。
        var draft = await _reconRepository.AsQueryable(true).FirstOrDefaultAsync(r => r.AccountId == txn.AccountId && r.Status == ReconciliationStatus.Draft, cancellationToken);
        if (draft == null)
            return Fail<BankTransactionDto>("Create a draft reconciliation for this account before confirming matches.", 400);

        Guid reconLineId = Guid.Empty;
        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var line = new ReconciliationLine
                {
                    ReconciliationId = draft.Id,
                    JournalLineId = journalLineId.Value,
                    TenantId = draft.TenantId
                };
                await _reconLineRepository.InsertAsync(line, ct);

                txn.Status = BankTransactionStatus.Matched;
                txn.MatchedJournalLineId = journalLineId.Value;
                txn.ReconciliationLineId = line.Id;
                if (input.JournalLineId != null)
                {
                    // 用户显式挑选：记为人工确认（置信度 1.0）
                    txn.MatchConfidence = 1.0m;
                    txn.MatchRule = "manual";
                }
                txn.SuggestedJournalLineId = null;
                await _txnRepository.UpdateAsync(txn, ct);

                // 触碰父对账行以轮换其并发戳（WHERE stamp=old）：若对账已被并发 CompleteAsync 完成，
                // 此更新影响 0 行 → DbUpdateConcurrencyException → 整批回滚 + 409
                await _reconRepository.UpdateAsync(draft, ct);

                reconLineId = line.Id;
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<BankTransactionDto>("The journal line was cleared concurrently by another reconciliation. Reload and retry.", 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BankTransactionDto>("The transaction was modified by another operation. Reload and retry.", 409);
        }

        await PublishEventAsync(new BankTransactionMatchedEvent
        {
            BankTransactionId = txn.Id,
            AccountId = txn.AccountId,
            JournalLineId = journalLineId.Value,
            ReconciliationLineId = reconLineId,
            TenantId = txn.TenantId
        }, cancellationToken);

        return await GetDtoAsync(txn.Id, cancellationToken);
    }

    public async Task<Result<BankTransactionDto>> UnmatchAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
    {
        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        if (txn.Status != BankTransactionStatus.Matched || txn.ReconciliationLineId == null)
            return Fail<BankTransactionDto>("Only matched transactions can be unmatched.", 409);

        var line = await _reconLineRepository.AsQueryable(true).FirstOrDefaultAsync(l => l.Id == txn.ReconciliationLineId.Value, cancellationToken);
        if (line != null)
        {
            var recon = await _reconRepository.AsNoTracking().FirstOrDefaultAsync(r => r.Id == line.ReconciliationId, cancellationToken);
            if (recon != null && recon.Status == ReconciliationStatus.Completed)
                return Fail<BankTransactionDto>("The reconciliation is completed and locked; the match cannot be undone.", 409);
        }

        try
        {
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                if (line != null)
                    await _reconLineRepository.DeleteAsync(line, ct);

                txn.Status = BankTransactionStatus.Pending;
                txn.MatchedJournalLineId = null;
                txn.ReconciliationLineId = null;
                txn.MatchConfidence = null;
                txn.MatchRule = null;
                await _txnRepository.UpdateAsync(txn, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<BankTransactionDto>("The transaction was modified by another operation. Reload and retry.", 409);
        }

        return await GetDtoAsync(txn.Id, cancellationToken);
    }

    public async Task<Result<BankTransactionDto>> ExcludeAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
    {
        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        if (txn.Status == BankTransactionStatus.Matched)
            return Fail<BankTransactionDto>("Unmatch the transaction before excluding it.", 409);
        if (txn.Status == BankTransactionStatus.Excluded)
            return Ok((await GetDtoAsync(txn.Id, cancellationToken)).Data!);

        txn.Status = BankTransactionStatus.Excluded;
        txn.SuggestedJournalLineId = null;
        txn.MatchConfidence = null;
        txn.MatchRule = null;
        await _txnRepository.UpdateAsync(txn, cancellationToken);
        await _txnRepository.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(txn.Id, cancellationToken);
    }

    public async Task<Result<BankTransactionDto>> RestoreAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
    {
        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        if (txn.Status != BankTransactionStatus.Excluded)
            return Fail<BankTransactionDto>("Only excluded transactions can be restored.", 409);

        txn.Status = BankTransactionStatus.Pending;
        await _txnRepository.UpdateAsync(txn, cancellationToken);
        await _txnRepository.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(txn.Id, cancellationToken);
    }

    public async Task<Result<BankDocumentResultDto>> CreateDocumentAsync(Guid bankTransactionId, CreateBankDocumentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var txn = await _txnRepository.AsQueryable(true).FirstOrDefaultAsync(t => t.Id == bankTransactionId, cancellationToken);
        if (txn == null)
            return Fail<BankDocumentResultDto>("Bank transaction not found.", 404);
        if (txn.Status != BankTransactionStatus.Pending)
            return Fail<BankDocumentResultDto>("Only pending transactions can spawn a document.", 409);

        var amount = Math.Abs(txn.Amount);
        var isInbound = txn.Amount > 0;

        string docType;
        Guid docId;
        switch (input.DocType)
        {
            case BankFeedDocType.Expense:
            {
                if (input.CounterAccountId == null)
                    return Fail<BankDocumentResultDto>("An expense account is required.", 400);
                var expenseResult = await _expenseService.CreateDraftAsync(new CreateExpenseDto
                {
                    PaidFromAccountId = txn.AccountId,
                    DocDate = txn.TxnDate,
                    Currency = txn.Currency,
                    PaymentMethod = input.PaymentMethod,
                    Memo = txn.Description,
                    Lines = new List<CreateExpenseLineDto>
                    {
                        new() { AccountId = input.CounterAccountId.Value, Amount = amount, Description = txn.Description }
                    }
                }, cancellationToken);
                if (!expenseResult.Succeeded)
                    return Fail<BankDocumentResultDto>(expenseResult.Message!, expenseResult.Code ?? 400);
                docType = FinanceSourceTypes.Expense;
                docId = expenseResult.Data!.Id;
                break;
            }
            case BankFeedDocType.PaymentEntry:
            {
                if (input.PartyId == null)
                    return Fail<BankDocumentResultDto>("A party is required for a payment entry.", 400);
                var paymentResult = await _paymentEntryService.CreateDraftAsync(new CreatePaymentEntryDto
                {
                    Direction = isInbound ? PaymentDirection.Inbound : PaymentDirection.Outbound,
                    PartyType = isInbound ? FinancePartyType.Customer : FinancePartyType.Vendor,
                    PartyId = input.PartyId.Value,
                    DocDate = txn.TxnDate,
                    Currency = txn.Currency,
                    Amount = amount,
                    DepositToAccountId = txn.AccountId,
                    PaymentMethod = input.PaymentMethod,
                    Reference = txn.Reference,
                    Memo = txn.Description
                }, cancellationToken);
                if (!paymentResult.Succeeded)
                    return Fail<BankDocumentResultDto>(paymentResult.Message!, paymentResult.Code ?? 400);
                docType = FinanceSourceTypes.PaymentEntry;
                docId = paymentResult.Data!.Id;
                break;
            }
            case BankFeedDocType.Transfer:
            {
                if (input.CounterAccountId == null)
                    return Fail<BankDocumentResultDto>("The other transfer account is required.", 400);
                var transferResult = await _transferService.CreateDraftAsync(new CreateTransferDto
                {
                    FromAccountId = isInbound ? input.CounterAccountId.Value : txn.AccountId,
                    ToAccountId = isInbound ? txn.AccountId : input.CounterAccountId.Value,
                    TransferDate = txn.TxnDate,
                    Currency = txn.Currency,
                    Amount = amount,
                    Reference = txn.Reference,
                    Memo = txn.Description
                }, cancellationToken);
                if (!transferResult.Succeeded)
                    return Fail<BankDocumentResultDto>(transferResult.Message!, transferResult.Code ?? 400);
                docType = FinanceSourceTypes.Transfer;
                docId = transferResult.Data!.Id;
                break;
            }
            default:
                return Fail<BankDocumentResultDto>("Unsupported document type.", 400);
        }

        txn.CreatedDocType = docType;
        txn.CreatedDocId = docId;
        await _txnRepository.UpdateAsync(txn, cancellationToken);
        await _txnRepository.SaveChangesAsync(cancellationToken);

        return Ok(new BankDocumentResultDto { DocType = docType, DocId = docId });
    }

    public async Task<Result<IPagedList<BankImportBatchDto>>> GetBatchesAsync(BankImportBatchQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _batchRepository.AsNoTracking();
        if (query.AccountId.HasValue)
            queryable = queryable.Where(b => b.AccountId == query.AccountId.Value);

        var pagedList = await queryable
            .OrderByDescending(b => b.CreationTime)
            .ProjectTo<BankImportBatch, BankImportBatchDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        if (pagedList.Items.Count > 0)
        {
            var batchIds = pagedList.Items.Select(b => b.Id).ToList();
            var matchedCounts = await _txnRepository.AsNoTracking()
                .Where(t => batchIds.Contains(t.ImportBatchId) && t.Status == BankTransactionStatus.Matched)
                .GroupBy(t => t.ImportBatchId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

            var accountIds = pagedList.Items.Select(b => b.AccountId).Distinct().ToList();
            var names = await _accountRepository.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Code, a.Name })
                .ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Name}", cancellationToken);

            foreach (var dto in pagedList.Items)
            {
                dto.MatchedCount = matchedCounts.GetValueOrDefault(dto.Id);
                dto.AccountName = names.GetValueOrDefault(dto.AccountId);
            }
        }

        return Ok(pagedList);
    }

    public async Task<Result> DeleteBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await _batchRepository.AsQueryable(true).FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch == null)
            return Fail("Import batch not found.", 404);

        var hasMatched = await _txnRepository.AnyAsync(t => t.ImportBatchId == batchId && t.Status == BankTransactionStatus.Matched, cancellationToken);
        if (hasMatched)
            return Fail("The batch has matched transactions; unmatch them before deleting the batch.", 409);

        try
        {
            return await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                var txns = await _txnRepository.ToListAsync(t => t.ImportBatchId == batchId, ct);
                if (txns.Count > 0)
                    await _txnRepository.DeleteManyAsync(txns, ct);
                await _batchRepository.DeleteAsync(batch, ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("The batch was modified by another operation. Reload and retry.", 409);
        }
    }

    private async Task<Result<BankTransactionDto>> GetDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var txn = await _txnRepository.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (txn == null)
            return Fail<BankTransactionDto>("Bank transaction not found.", 404);
        return Ok(txn.MapTo<BankTransactionDto>());
    }

    /// <summary>
    /// 去重落库：加载账户既有 ExternalId、按去重键跳过已存/同文件重复，逐条插入
    /// （唯一索引兜底并发），返回 (导入数, 跳过数)。ImportStatement 与 PullFromProvider 共用
    /// 同一逻辑，避免两份易漂移的重复实现。
    /// </summary>
    private async Task<(int Imported, int Skipped)> PersistDeduplicatedTransactionsAsync(
        Guid accountId, Guid batchId, BankTransactionSource source,
        IReadOnlyList<ParsedBankTransaction> transactions, string defaultCurrency, CancellationToken cancellationToken)
    {
        var existing = (await _txnRepository.AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .Select(t => t.ExternalId)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenInFile = new HashSet<string>(StringComparer.Ordinal);
        var imported = 0;
        var skipped = 0;

        foreach (var p in transactions)
        {
            var externalId = ExternalIdFor(source, accountId, p, occurrences);
            if (existing.Contains(externalId) || !seenInFile.Add(externalId))
            {
                skipped++;
                continue;
            }

            var txn = new BankTransaction
            {
                AccountId = accountId,
                ImportBatchId = batchId,
                TxnDate = p.PostedDate.ToUtcDate(),
                Amount = _helper.Round(p.Amount),
                Currency = string.IsNullOrWhiteSpace(p.Currency) ? defaultCurrency : p.Currency.Trim().ToUpperInvariant(),
                Description = p.Description,
                Payee = p.Payee,
                Reference = p.Reference,
                ExternalId = externalId,
                Source = source,
                Status = BankTransactionStatus.Pending,
                BalanceAfter = p.BalanceAfter
            };

            try
            {
                await _txnRepository.InsertAsync(txn, cancellationToken);
                await _txnRepository.SaveChangesAsync(cancellationToken);
                imported++;
            }
            catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
            {
                // 并发导入已落同一 ExternalId：唯一索引兜底，跳过不失败整批
                skipped++;
            }
        }

        return (imported, skipped);
    }

    /// <summary>
    /// 计算去重键：OFX FITID / provider id 直接用；否则 CSV 规则
    /// <c>"csv:" + SHA256(accountId|date|amount|normalizedDescription|n)</c>（n = 同文件内相同元组序号）
    /// </summary>
    private static string ExternalIdFor(BankTransactionSource source, Guid accountId, ParsedBankTransaction p, Dictionary<string, int> occurrences)
    {
        if (!string.IsNullOrWhiteSpace(p.ExternalId))
            return p.ExternalId.Trim();

        var normalizedDesc = NormalizeDescription(p.Description);
        var tupleKey = $"{p.PostedDate:yyyyMMdd}|{p.Amount.ToString(CultureInfo.InvariantCulture)}|{normalizedDesc}";
        var n = occurrences.TryGetValue(tupleKey, out var count) ? count : 0;
        occurrences[tupleKey] = n + 1;

        var raw = $"{accountId:N}|{p.PostedDate:yyyyMMdd}|{p.Amount.ToString(CultureInfo.InvariantCulture)}|{normalizedDesc}|{n}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "csv:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;
        return Regex.Replace(description.Trim().ToLowerInvariant(), "\\s+", " ");
    }
}
