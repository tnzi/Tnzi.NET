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
    private readonly BalanceSummaryMaintainer _summaryMaintainer;
    private readonly ReversalGuard _reversalGuard;
    private readonly TimeProvider _timeProvider;
    private readonly FinanceOptions _options;
    private readonly ICurrentUser? _currentUser;

    public LedgerPostingEngine(
        IRepository<Account, Guid> accountRepository,
        IFiscalYearService fiscalYearService,
        IExchangeRateService exchangeRateService,
        IDocumentNumberService numberService,
        BalanceSummaryMaintainer summaryMaintainer,
        ReversalGuard reversalGuard,
        TimeProvider timeProvider,
        IOptionsSnapshot<FinanceOptions> options,
        ICurrentUser? currentUser = null)
    {
        _accountRepository = Check.NotNull(accountRepository);
        _fiscalYearService = Check.NotNull(fiscalYearService);
        _exchangeRateService = Check.NotNull(exchangeRateService);
        _numberService = Check.NotNull(numberService);
        _summaryMaintainer = Check.NotNull(summaryMaintainer);
        _reversalGuard = Check.NotNull(reversalGuard);
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
            // 币种限定科目接受「其币种 OR 本位币」的行：本位币行是价值调整（期末重估、
            // realized FX 控制科目残差），语义同 SettlementService.PostRealizedFxAsync 先例。
            // 口径铁律：限定科目的交易币余额 = Σ(TxnDebit − TxnCredit) WHERE l.Currency == account.Currency；
            // 本位币行只进本位币余额，不进交易币余额、不进外币对账候选。
            if (account.Currency != null && currency != account.Currency && currency != baseCurrency)
                return Result.Failure($"Line {i + 1}: account '{account.Code}' only accepts {account.Currency} or {baseCurrency} transactions.", 400);
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

        // 提交点内：把定稿行累加进月粒度余额桶（与凭证同事务，回滚一并撤销）
        await _summaryMaintainer.ApplyAsync(entry, cancellationToken);
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
    /// <remarks>
    /// <b>这里是全部单据的冲销漏斗</b>：每种单据的 <c>VoidAsync</c> 与
    /// <c>IJournalEntryService.ReverseAsync</c>（<c>ILedgerPostingService.ReverseAsync</c> 委托它）
    /// 最终都经由本方法构造冲销凭证，所以冲销的准入校验放这里一处即覆盖全部单据类型，
    /// 且不必按 DocType 分别去找它的总账凭证。
    /// <para>
    /// 校验经 <see cref="ReversalGuard"/>（期间封账 + 已完成对账 + 已匹配银行流水），
    /// 与只读的 <c>GetReversibilityAsync</c> 同源。守卫只读且位于凭证号分配与余额桶累加之前，
    /// 拒绝路径零写入。
    /// </para>
    /// </remarks>
    public async Task<Result<JournalEntry>> BuildReversalAsync(JournalEntry original, DateTime postingDate, string? memo, CancellationToken cancellationToken = default)
    {
        Check.NotNull(original);

        // ★ 只有已过账且未被冲销过的凭证可以冲销。这条**必须**在漏斗里，不能只由上游守。
        //
        // 七条上游（GL 冲销端点 + 六个单据 VoidAsync）里，此前只有 JournalEntryService.ReverseAsync
        // 与 TransferService 检查了原凭证状态，另外五个单据 Void 一律
        // `FirstOrDefaultAsync(e => e.Id == doc.JournalEntryId)` 直接取、不带 Status 谓词。
        // 后果是同一张凭证可被冲销两次：先经 GL 端点冲销（原凭证 Status 置 Reversed、
        // ReversedByEntryId 指向 R1），再走单据 void —— 单据自身仍是 Posted 故状态门全过，
        // 于是对同一 original 再造 R2，并把 ReversedByEntryId 覆写成 R2，R1 成为孤儿。
        //
        // ★ 破坏是**完全静默**的：每张凭证内部各自平衡，试算平衡恒为 0；余额汇总忠实累加，
        // VerifyAsync 报「一致」。唯一能暴露它的是人工把 AR/AP 控制科目余额与账龄合计对一遍。
        //
        // ReversalGuard 的注释曾写着「凭证状态不在这里判定 —— 上游各 VoidAsync 与 ReverseAsync
        // 已经在做」，而七个上游里五个从来没做过。注释被当断言用了。
        if (original.Status != JournalEntryStatus.Posted)
        {
            return Result<JournalEntry>.Failure(
                original.Status == JournalEntryStatus.Draft
                    ? "Draft entries cannot be reversed."
                    : "The journal entry has already been reversed.",
                409);
        }

        if (original.ReversedByEntryId.HasValue)
        {
            return Result<JournalEntry>.Failure("The journal entry has already been reversed.", 409);
        }

        var date = postingDate.ToUtcDate();
        var block = await _reversalGuard.EvaluateAsync(original, date, cancellationToken);
        if (block != null)
            return Result<JournalEntry>.Failure(block.Detail, block.Code);

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

        // 冲销行毛额累加进桶（净额天然归零）；调用方 MUST 同 UoW 持久化冲销凭证，
        // 任一后续失败经 UnitOfWorkAbortException/异常连同桶增量整体回滚
        await _summaryMaintainer.ApplyAsync(reversal, cancellationToken);

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
