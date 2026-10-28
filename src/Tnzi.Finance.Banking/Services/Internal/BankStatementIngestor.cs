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
        // 文件名是人给的（上传名），越界拒绝而不截断；在 SQL Server / PostgreSQL 上原样赋值是批次头插入 500。
        if (fileName != null && fileName.Length > BankTransactionFieldLimits.ImportFileNameMaxLength)
            return Fail<BankImportResultDto>($"The statement file name is {fileName.Length} characters; it cannot exceed {BankTransactionFieldLimits.ImportFileNameMaxLength}. Rename the file and import it again.", 400);

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
        var rowCurrencies = ValidateRowCurrencies(parsed.Transactions, defaultCurrency);
        if (!rowCurrencies.Succeeded)
            return Fail<BankImportResultDto>(rowCurrencies.Message!, rowCurrencies.Code ?? 400);

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

        // 无跟踪读取：档案在本方法里只被读，回写游标走下面那条定向更新（理由见那里）。
        var bankAccount = await _bankAccountRepository.AsNoTracking().FirstOrDefaultAsync(b => b.AccountId == input.AccountId, cancellationToken);
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
        var parsedList = pull.Transactions
            .Select(t => new ParsedBankTransaction(t.PostedDate, t.Amount, t.Currency, t.ExternalId, t.Description, t.Payee, t.Reference))
            .ToList();
        // 提供者是消费方可替换的扩展点，逐行币种在批次头落库之前比过账户币种：
        // 一行外币流水对着本位币 GL 行清算是错账；一个超宽的币种值则是第一行插入 500 而批次头已提交。
        var rowCurrencies = ValidateRowCurrencies(parsedList, defaultCurrency);
        if (!rowCurrencies.Succeeded)
            return Fail<BankImportResultDto>(rowCurrencies.Message!, rowCurrencies.Code ?? 400);

        var batch = new BankImportBatch
        {
            AccountId = input.AccountId,
            Source = BankTransactionSource.Provider,
            StatementEndBalance = pull.LedgerBalance
        };
        await _batchRepository.InsertAsync(batch, cancellationToken);
        await _batchRepository.SaveChangesAsync(cancellationToken);

        var (imported, skipped) = await PersistDeduplicatedTransactionsAsync(
            input.AccountId, batch.Id, BankTransactionSource.Provider, parsedList, defaultCurrency, cancellationToken);

        batch.ImportedCount = imported;
        batch.SkippedCount = skipped;
        await _batchRepository.UpdateAsync(batch, cancellationToken);
        await _batchRepository.SaveChangesAsync(cancellationToken);

        // 成功后回写游标 / 同步时间。★ 定向更新这两列，而不是把开头读到的档案整行写回：
        // 一次拉取可以跑上十几秒（提供者调用 + 逐行入库），期间打印的支票与生成的 EFT 文件已经把
        // NextCheckNumber / EftFileCreationNumber 往前推了，整行写回会把它们退回旧值，之后每一次打印
        // 都撞号 409、每一份文件都复用旧序号，直到有人手工改号。
        // 不换并发标记：游标是幂等的（重复拉取按 ExternalId 去重），不值得让一次后台拉取把操作员
        // 正在编辑的档案表单变成 409。
        var nextCursor = pull.NextCursor ?? bankAccount.FeedCursor;
        var syncedAt = DateTime.UtcNow;
        await _bankAccountRepository.AsQueryable(true)
            .Where(b => b.Id == bankAccount.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.FeedCursor, nextCursor)
                .SetProperty(x => x.LastFeedSyncTime, syncedAt), cancellationToken);

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
    /// 逐行币种必须与账户币种一致（空 = 沿用账户币种）。两条来源共用：文件路径此前只比对账单级
    /// 币种，提供者路径一行都没比。在批次头落库之前调用，这样拒绝是干净的（没有孤儿批次头）。
    /// </summary>
    private static Result ValidateRowCurrencies(IReadOnlyList<ParsedBankTransaction> rows, string accountCurrency)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var currency = rows[i].Currency;
            if (string.IsNullOrWhiteSpace(currency))
                continue;
            if (!string.Equals(currency.Trim(), accountCurrency, StringComparison.OrdinalIgnoreCase))
                return Result.Failure($"Row {i + 1} is in currency '{currency.Trim()}', which does not match the account currency '{accountCurrency}'. Import it into a matching-currency account.", 400);
        }
        return Result.Success();
    }

    /// <summary>查询既有去重键时每条 IN 列表的上限（远低于 SQL Server 单条语句 2100 个参数的上限）。</summary>
    private const int ExistingKeyQueryChunkSize = 1000;

    /// <summary>
    /// 去重落库：先算出本次全部去重键、只查这些键里账户已有的那些，按去重键跳过已存/同文件重复，
    /// 逐条插入（唯一索引兜底并发），返回 (导入数, 跳过数)。ImportStatement 与 PullFromProvider 共用
    /// 同一逻辑，避免两份易漂移的重复实现。
    /// </summary>
    /// <remarks>
    /// ★ 只查本次的键，不加载账户的全部历史键：后者随账户年龄线性增长（每次导入都把几年来的
    /// ExternalId 全量传输、全量哈希一遍），而一个文件最多 <c>BankImportMaxRows</c> 行。
    /// 键的计算顺序与逐行插入时相同（同一个 occurrences 字典），所以去重语义逐字不变；
    /// 软删的行照旧被全局过滤器排除，删掉批次后重导仍会重新导入。
    /// </remarks>
    private async Task<(int Imported, int Skipped)> PersistDeduplicatedTransactionsAsync(
        Guid accountId, Guid batchId, BankTransactionSource source,
        IReadOnlyList<ParsedBankTransaction> transactions, string defaultCurrency, CancellationToken cancellationToken)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = transactions.Select(p => ExternalIdFor(source, accountId, p, occurrences)).ToList();

        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in keys.Distinct(StringComparer.Ordinal).Chunk(ExistingKeyQueryChunkSize))
        {
            var found = await _txnRepository.AsNoTracking()
                .Where(t => t.AccountId == accountId && chunk.Contains(t.ExternalId))
                .Select(t => t.ExternalId)
                .ToListAsync(cancellationToken);
            existing.UnionWith(found);
        }

        var seenInFile = new HashSet<string>(StringComparer.Ordinal);
        var imported = 0;
        var skipped = 0;

        for (var i = 0; i < transactions.Count; i++)
        {
            var p = transactions[i];
            var externalId = keys[i];
            if (existing.Contains(externalId) || !seenInFile.Add(externalId))
            {
                skipped++;
                continue;
            }

            // ★ 按列宽归一化（BankTransactionFieldLimits）：这里是三条来源跨进持久化的唯一入口。
            //    原样赋值在 SQL Server / PostgreSQL 上会让第 k 行插入抛 500，而批次头与前 k-1 行
            //    已各自提交、计数停在 0/0 —— 重传时前 k-1 行按去重跳过、第 k 行再炸一次。
            var txn = new BankTransaction
            {
                AccountId = accountId,
                ImportBatchId = batchId,
                TxnDate = p.PostedDate.ToUtcDate(),
                Amount = _helper.Round(p.Amount),
                Currency = string.IsNullOrWhiteSpace(p.Currency) ? defaultCurrency : p.Currency.Trim().ToUpperInvariant(),
                Description = BankTransactionFieldLimits.ClampDescription(p.Description),
                Payee = BankTransactionFieldLimits.ClampPayee(p.Payee),
                Reference = BankTransactionFieldLimits.NormalizeReference(p.Reference),
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
                // 并发导入已落同一 ExternalId：唯一索引兜底，跳过不失败整批。
                // ★★ 必须 Discard：插入失败的实体仍是 Added 留在变更跟踪器里，
                //    下一行的 SaveChanges 会把它一起重新提交并再次撞索引 ——
                //    于是本批**剩下的每一行都被计成已跳过而一条都没真的导进去**，
                //    而返回值看起来完全正常（报告成功的静默数据丢失）。
                _txnRepository.Discard(txn);
                skipped++;
            }
        }

        return (imported, skipped);
    }

    /// <summary>
    /// 计算去重键：OFX FITID / provider id 直接用（超过列宽则折叠成哈希 —— 它是键，截断会让两个
    /// 不同的 id 合并、第二笔被当重复静默丢掉；<c>existing</c> 集合按最终值比对，所以折叠在这里收口）；
    /// 否则 CSV 规则 <c>"csv:" + SHA256(accountId|date|amount|normalizedDescription|n)</c>（n = 同文件内相同元组序号）
    /// </summary>
    private static string ExternalIdFor(BankTransactionSource source, Guid accountId, ParsedBankTransaction p, Dictionary<string, int> occurrences)
    {
        if (!string.IsNullOrWhiteSpace(p.ExternalId))
            return BankTransactionFieldLimits.FoldExternalId(p.ExternalId.Trim());

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
