using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 把对账单变成台账上的流水行：文件解析 / 提供者拉取 + 去重落库。
/// </summary>
/// <remarks>
/// 与「匹配、对账、据此建单」是两件事——摄取的关键词是解析、账号与币种校验、
/// 去重；对账的关键词是候选、规则与过账。分开之后 <c>BankFeedService</c> 只剩后者。
///
/// public 因经 DI 注入 public 服务的构造函数（沿 <c>BankDocumentDrafter</c> 先例）。
/// </remarks>
public class BankStatementIngestor : ApplicationService
{
    private readonly IRepository<BankTransaction, Guid> _txnRepository;
    private readonly IRepository<BankImportBatch, Guid> _batchRepository;
    private readonly IRepository<BankAccount, Guid> _bankAccountRepository;
    private readonly FinanceDocumentHelper _helper;
    private readonly IEnumerable<IBankFeedProvider> _providers;
    private readonly FinanceOptions _options;

    public BankStatementIngestor(
        IServiceProvider serviceProvider,
        IRepository<BankTransaction, Guid> txnRepository,
        IRepository<BankImportBatch, Guid> batchRepository,
        IRepository<BankAccount, Guid> bankAccountRepository,
        FinanceDocumentHelper helper,
        IEnumerable<IBankFeedProvider> providers,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _txnRepository = Check.NotNull(txnRepository);
        _batchRepository = Check.NotNull(batchRepository);
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
        _helper = Check.NotNull(helper);
        _providers = Check.NotNull(providers);
        _options = Check.NotNull(options).Value;
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
            // 提供者由消费应用注册，其异常消息可能带内部细节（连接串 / 含令牌的 URL）：
            // 记服务端日志，对外只给通用消息（同 ReceiptCaptureService 对提取器的处理）。
            Logger.LogError(ex, "Bank feed provider '{ProviderKey}' failed for account {AccountId}.", provider.Key, input.AccountId);
            return Fail<BankImportResultDto>("The bank feed provider failed. See server logs for details.", 502);
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
