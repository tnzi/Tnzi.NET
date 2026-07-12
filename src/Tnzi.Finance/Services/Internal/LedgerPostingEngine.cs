namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 总账过账引擎（内部共享）：校验、换算、配平、编号、定稿
/// </summary>
/// <remarks>
/// 供 <see cref="JournalEntryService"/>（草稿过账）与 <see cref="LedgerPostingService"/>（直接过账）复用；
/// 位于 Internal 命名空间，不属于模块公共 API，消费方请使用 <see cref="ILedgerPostingService"/>。
/// 必须在工作单元事务内调用：凭证号分配依赖事务回滚回收保证连续无缺口。
/// 步骤顺序约束：凭证号分配必须位于所有可失败校验之后（分配后不再有业务性失败路径），
/// 否则校验失败会烧号产生缺口。
/// </remarks>
public sealed class LedgerPostingEngine
{
    /// <summary>凭证号序列作用域</summary>
    public const string JournalEntrySequenceScope = "JournalEntry";

    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IFiscalYearService _fiscalYearService;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly IDocumentNumberService _numberService;
    private readonly TimeProvider _timeProvider;
    private readonly FinanceOptions _options;
    private readonly ICurrentUser? _currentUser;

    public LedgerPostingEngine(
        IRepository<Account, Guid> accountRepository,
        IFiscalYearService fiscalYearService,
        IExchangeRateService exchangeRateService,
        IDocumentNumberService numberService,
        TimeProvider timeProvider,
        IOptionsSnapshot<FinanceOptions> options,
        ICurrentUser? currentUser = null)
    {
        _accountRepository = Check.NotNull(accountRepository);
        _fiscalYearService = Check.NotNull(fiscalYearService);
        _exchangeRateService = Check.NotNull(exchangeRateService);
        _numberService = Check.NotNull(numberService);
        _timeProvider = Check.NotNull(timeProvider);
        _options = Check.NotNull(options).Value;
        _currentUser = currentUser;
    }

    /// <summary>
    /// 校验并定稿一张凭证（entry.Lines 携带交易币金额 TxnDebit/TxnCredit）。
    /// 成功后凭证处于 Posted 状态且分配了连续凭证号；调用方负责持久化。
    /// </summary>
    /// <remarks>
    /// 失败原子性：entry 可能是被跟踪实体（草稿过账路径），而调用方以 Result（非异常）
    /// 传递失败后仍会正常提交工作单元 —— 变更跟踪会把任何已发生的实体改动一并落库。
    /// 因此所有计算先写入本地缓冲，任何失败路径 MUST 在触碰实体前返回；
    /// 只有全部校验通过后才进入"提交点"统一写回实体。
    /// </remarks>
    public async Task<Result> PostAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entry);

        var postingDate = entry.PostingDate.ToUtcDate();
        var lines = entry.Lines.OrderBy(l => l.LineNumber).ToList();

        // 行数与金额形态
        if (lines.Count < 2)
            return Result.Failure("A journal entry must contain at least two lines.", 400);
        if (lines.Count > _options.MaxLinesPerEntry)
            return Result.Failure($"A journal entry cannot contain more than {_options.MaxLinesPerEntry} lines.", 400);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.TxnDebit < 0 || line.TxnCredit < 0)
                return Result.Failure($"Line {i + 1}: amounts cannot be negative.", 400);
            if (line.TxnDebit > 0 == line.TxnCredit > 0)
                return Result.Failure($"Line {i + 1}: exactly one of debit or credit must be greater than zero.", 400);
        }

        // 交易币种平衡（必须精确相等）
        var txnDebitTotal = lines.Sum(l => l.TxnDebit);
        var txnCreditTotal = lines.Sum(l => l.TxnCredit);
        if (txnDebitTotal != txnCreditTotal)
            return Result.Failure($"The entry is not balanced: total debit {txnDebitTotal} does not equal total credit {txnCreditTotal}.", 400);

        // 币种与汇率解析（本地变量，提交点前不写实体）
        var baseCurrency = _options.BaseCurrency.Trim().ToUpperInvariant();
        var currency = string.IsNullOrWhiteSpace(entry.Currency)
            ? baseCurrency
            : entry.Currency.Trim().ToUpperInvariant();

        decimal rate;
        if (currency == baseCurrency)
        {
            rate = 1m;
        }
        else if (entry.ExchangeRate > 0)
        {
            rate = entry.ExchangeRate;
        }
        else
        {
            var resolved = await _exchangeRateService.ResolveRateAsync(currency, baseCurrency, postingDate, cancellationToken);
            if (!resolved.HasValue)
                return Result.Failure($"No exchange rate available for {currency} -> {baseCurrency} on {postingDate:yyyy-MM-dd}.", 400);
            rate = resolved.Value;
        }

        // 期间锁定
        var dateResult = await _fiscalYearService.ValidatePostingDateAsync(postingDate, cancellationToken);
        if (!dateResult.Succeeded)
            return dateResult;

        // 科目校验
        var accountIds = lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = (await _accountRepository.ToListAsync(a => accountIds.Contains(a.Id), cancellationToken))
            .ToDictionary(a => a.Id);

        for (var i = 0; i < lines.Count; i++)
        {
            if (!accounts.TryGetValue(lines[i].AccountId, out var account))
                return Result.Failure($"Line {i + 1}: account not found.", 400);
            if (account.IsGroup)
                return Result.Failure($"Line {i + 1}: cannot post to group account '{account.Code}'.", 400);
            if (!account.IsActive)
                return Result.Failure($"Line {i + 1}: account '{account.Code}' is inactive.", 400);
            if (account.Currency != null && account.Currency != currency)
                return Result.Failure($"Line {i + 1}: account '{account.Code}' only accepts {account.Currency} transactions.", 400);
        }

        // 本位币金额换算（写入本地缓冲）
        var decimals = _options.BaseCurrencyDecimals;
        var baseAmounts = new (decimal Debit, decimal Credit)[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            baseAmounts[i] = (
                Math.Round(lines[i].TxnDebit * rate, decimals, MidpointRounding.AwayFromZero),
                Math.Round(lines[i].TxnCredit * rate, decimals, MidpointRounding.AwayFromZero));
        }

        // 换算尾差校验：每行合法舍入误差上界为 0.5 个最小货币单位，
        // 容差按行数缩放，否则多行外币凭证的正常累积尾差会被误拒
        var residual = baseAmounts.Sum(a => a.Debit) - baseAmounts.Sum(a => a.Credit);
        Account? roundingAccount = null;
        if (residual != 0)
        {
            var effectiveTolerance = Math.Max(_options.RoundingTolerance, lines.Count * GetHalfUnit(decimals));
            if (Math.Abs(residual) > effectiveTolerance)
                return Result.Failure($"Converted base-currency amounts do not balance (difference {residual}). Check the exchange rate.", 400);

            roundingAccount = await _accountRepository.FirstOrDefaultAsync(
                a => a.SystemRole == AccountSystemRole.RoundingDifference && a.IsActive && !a.IsGroup, cancellationToken);
            if (roundingAccount == null)
                return Result.Failure("Posting requires an active account with the RoundingDifference system role to absorb rounding differences.", 400);
        }

        // —— 提交点：全部业务校验已通过，此后不再有业务性失败路径 ——
        // 连续凭证号分配放在提交点最前（分配后无失败路径，异常则整体回滚回收号码）
        entry.Number = await _numberService.NextFormattedAsync(
            JournalEntrySequenceScope, _options.JournalNumberPrefix, _options.JournalNumberPadding, cancellationToken);

        entry.PostingDate = postingDate;
        entry.Currency = currency;
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].Debit = baseAmounts[i].Debit;
            lines[i].Credit = baseAmounts[i].Credit;
        }

        if (roundingAccount != null)
        {
            var roundingLine = new JournalLine
            {
                AccountId = roundingAccount.Id,
                Debit = residual < 0 ? -residual : 0,
                Credit = residual > 0 ? residual : 0,
                TxnDebit = 0,
                TxnCredit = 0,
                Memo = "Automatic rounding difference"
            };

            entry.Lines.Add(roundingLine);
            lines.Add(roundingLine);
        }

        Finalize(entry, lines, rate);
        return Result.Success();
    }

    /// <summary>
    /// 半个最小货币单位（如 2 位小数 → 0.005），即单行换算的最大合法舍入误差
    /// </summary>
    private static decimal GetHalfUnit(int decimals)
    {
        var unit = 1m;
        for (var i = 0; i < decimals; i++)
            unit /= 10m;
        return unit / 2m;
    }

    /// <summary>
    /// 构建一张冲销凭证（借贷互换，金额精确取自原凭证，不重新换算）
    /// </summary>
    public async Task<Result<JournalEntry>> BuildReversalAsync(JournalEntry original, DateTime postingDate, string? memo, CancellationToken cancellationToken = default)
    {
        Check.NotNull(original);

        var date = postingDate.ToUtcDate();
        var dateResult = await _fiscalYearService.ValidatePostingDateAsync(date, cancellationToken);
        if (!dateResult.Succeeded)
            return Result<JournalEntry>.Failure(dateResult.Message ?? "Posting date is not allowed.", dateResult.Code ?? 400);

        var reversal = new JournalEntry
        {
            Id = SequentialGuid.NewGuid(),
            Status = JournalEntryStatus.Posted,
            PostingDate = date,
            Memo = memo ?? $"Reversal of {original.Number}",
            Currency = original.Currency,
            ExchangeRate = original.ExchangeRate,
            SourceType = original.SourceType,
            SourceId = original.SourceId,
            ReversalOfEntryId = original.Id,
            TotalDebit = original.TotalCredit,
            TotalCredit = original.TotalDebit,
            PostedTime = _timeProvider.GetUtcNow().UtcDateTime,
            PostedById = _currentUser?.Id
        };

        var lineNumber = 1;
        foreach (var line in original.Lines.OrderBy(l => l.LineNumber))
        {
            reversal.Lines.Add(new JournalLine
            {
                LineNumber = lineNumber++,
                AccountId = line.AccountId,
                Debit = line.Credit,
                Credit = line.Debit,
                TxnDebit = line.TxnCredit,
                TxnCredit = line.TxnDebit,
                Currency = line.Currency,
                ExchangeRate = line.ExchangeRate,
                Memo = line.Memo,
                PartyType = line.PartyType,
                PartyId = line.PartyId,
                Dimensions = line.Dimensions,
                TaxRateId = line.TaxRateId,
                IsPosted = true,
                PostingDate = date
            });
        }

        reversal.Number = await _numberService.NextFormattedAsync(
            JournalEntrySequenceScope, _options.JournalNumberPrefix, _options.JournalNumberPadding, cancellationToken);

        return Result<JournalEntry>.Success(reversal);
    }

    private void Finalize(JournalEntry entry, List<JournalLine> lines, decimal rate)
    {
        entry.Status = JournalEntryStatus.Posted;
        entry.ExchangeRate = rate;
        entry.PostedTime = _timeProvider.GetUtcNow().UtcDateTime;
        entry.PostedById = _currentUser?.Id;
        entry.TotalDebit = lines.Sum(l => l.Debit);
        entry.TotalCredit = lines.Sum(l => l.Credit);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            line.LineNumber = i + 1;
            line.Currency = entry.Currency;
            line.ExchangeRate = rate;
            line.IsPosted = true;
            line.PostingDate = entry.PostingDate;
        }
    }
}
