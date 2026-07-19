namespace Tnzi.Finance.Services;

/// <summary>
/// 未实现汇兑损益期末重估服务
/// </summary>
/// <remarks>
/// 口径铁律：外币限定科目的交易币余额 = Σ(TxnDebit − TxnCredit) WHERE 行币种 == 科目币种；
/// 本位币行（历次重估的价值调整）只进账面本位币余额，不进交易币余额。
/// 单张汇总凭证（Currency = 本位币, rate = 1）：每科目一调整行 + 一净额行记 ExchangeGainLoss。
/// SourceType = "Revaluation"，SourceId = 基准日 yyyy-MM-dd。撤销 = 冲销（零新机制）。
/// </remarks>
public class RevaluationService : ApplicationService, IRevaluationService
{
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<JournalLine, Guid> _journalLineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly LedgerPostingEngine _engine;
    private readonly FinanceDocumentHelper _helper;
    private readonly PostingGuardRunner _guards;
    private readonly FinanceOptions _options;

    public RevaluationService(
        IServiceProvider serviceProvider,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<JournalLine, Guid> journalLineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IExchangeRateService exchangeRateService,
        LedgerPostingEngine engine,
        FinanceDocumentHelper helper,
        PostingGuardRunner guards,
        IOptionsSnapshot<FinanceOptions> options)
        : base(serviceProvider)
    {
        _accountRepository = Check.NotNull(accountRepository);
        _journalLineRepository = Check.NotNull(journalLineRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _exchangeRateService = Check.NotNull(exchangeRateService);
        _engine = Check.NotNull(engine);
        _helper = Check.NotNull(helper);
        _guards = Check.NotNull(guards);
        _options = Check.NotNull(options).Value;
    }

    public Task<Result<RevaluationPreviewDto>> PreviewAsync(RunRevaluationDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);
        return BuildPreviewAsync(input.AsOf, input.AccountIds, cancellationToken);
    }

    public async Task<Result<RevaluationPreviewDto>> RunAsync(RunRevaluationDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var previewResult = await BuildPreviewAsync(input.AsOf, input.AccountIds, cancellationToken);
        if (!previewResult.Succeeded)
            return previewResult;
        var data = previewResult.Data!;

        var postable = data.Rows.Where(r => r.SkipReason == null && r.Adjustment != 0m).ToList();
        if (postable.Count == 0)
            return Ok(data); // delta 全 0（同基准日同汇率重跑天然收敛）→ 幂等 no-op，不出凭证

        // 过账前钩子（任何写入之前）
        var guardResult = await _guards.CheckAsync(
            FinanceSourceTypes.Revaluation, data.AsOf.ToString("yyyy-MM-dd"), FinancePostingOperation.Post, input, cancellationToken);
        if (!guardResult.Succeeded)
            return Fail<RevaluationPreviewDto>(guardResult.Message ?? "Revaluation was rejected.", guardResult.Code ?? 403);

        var fxResult = await _helper.ResolveSystemAccountAsync(AccountSystemRole.ExchangeGainLoss, cancellationToken);
        if (!fxResult.Succeeded)
            return Fail<RevaluationPreviewDto>(fxResult.Message!, fxResult.Code ?? 400);
        var fx = fxResult.Data!;

        var netAdjustment = postable.Sum(r => r.Adjustment);
        var lineCount = postable.Count + (netAdjustment != 0m ? 1 : 0);
        if (lineCount > _options.MaxLinesPerEntry)
            return Fail<RevaluationPreviewDto>(
                $"The revaluation would post {lineCount} lines, exceeding the {_options.MaxLinesPerEntry}-line limit. Narrow the account selection.", 400);

        var baseCurrency = data.BaseCurrency;
        var memo = string.IsNullOrWhiteSpace(input.Memo)
            ? $"Unrealized FX revaluation as of {data.AsOf:yyyy-MM-dd}"
            : input.Memo;

        Guid entryId;
        try
        {
            var postResult = await ExecuteInUnitOfWorkAsync<Result<Guid>>(async ct =>
            {
                var entry = new JournalEntry
                {
                    Status = JournalEntryStatus.Draft,
                    PostingDate = data.AsOf,
                    Memo = memo,
                    Currency = baseCurrency,
                    ExchangeRate = 1m,
                    SourceType = FinanceSourceTypes.Revaluation,
                    SourceId = data.AsOf.ToString("yyyy-MM-dd")
                };

                var lineNumber = 1;
                foreach (var row in postable)
                {
                    var amount = Math.Abs(row.Adjustment);
                    var line = new JournalLine
                    {
                        LineNumber = lineNumber++,
                        AccountId = row.AccountId,
                        Currency = baseCurrency,
                        Memo = $"Revalue {row.Currency} {row.TxnBalance} @ {row.Rate} -> {row.TargetBase} (book {row.BookBase})"
                    };
                    // 调整 > 0 = 增记本位价值（借方）；< 0 = 减记（贷方）
                    if (row.Adjustment > 0m)
                        line.TxnDebit = amount;
                    else
                        line.TxnCredit = amount;
                    entry.Lines.Add(line);
                }

                // 净额记汇兑损益：科目侧净借（netAdjustment > 0）→ 贷 FX；净贷 → 借 FX。
                // netAdjustment == 0 时科目侧已自平，不加 FX 行（零金额行会被引擎拒绝）
                if (netAdjustment != 0m)
                {
                    var magnitude = Math.Abs(netAdjustment);
                    var fxLine = new JournalLine
                    {
                        LineNumber = lineNumber,
                        AccountId = fx.Id,
                        Currency = baseCurrency,
                        Memo = "Net unrealized exchange gain/loss"
                    };
                    if (netAdjustment > 0m)
                        fxLine.TxnCredit = magnitude;
                    else
                        fxLine.TxnDebit = magnitude;
                    entry.Lines.Add(fxLine);
                }

                var engineResult = await _engine.PostAsync(entry, ct);
                if (!engineResult.Succeeded)
                    throw new UnitOfWorkAbortException(engineResult);

                // 权威时序守卫（引擎已分配凭证号 = 串行化点；并发方等锁后见已提交同/后日重估）：
                // 基准日必须晚于最新未冲销原始重估凭证；违例整体回滚转 409。
                // 只看原始重估（ReversalOfEntryId == null）：冲销凭证由 BuildReversal 复制了
                // SourceType = "Revaluation"，但它是修正手段而非新重估，不应阻塞重跑
                var conflict = await _entryRepository.AnyAsync(
                    e => e.SourceType == FinanceSourceTypes.Revaluation
                         && e.ReversalOfEntryId == null
                         && e.Status != JournalEntryStatus.Reversed
                         && e.PostingDate >= data.AsOf, ct);
                if (conflict)
                    throw new UnitOfWorkAbortException(Result.Failure(
                        $"A revaluation dated on or after {data.AsOf:yyyy-MM-dd} already exists. Reverse it before revaluing an earlier or equal date.", 409));

                await _entryRepository.InsertAsync(entry, ct);
                return Result<Guid>.Success(entry.Id);
            }, cancellationToken);

            if (!postResult.Succeeded)
                return Fail<RevaluationPreviewDto>(postResult.Message ?? "Revaluation failed.", postResult.Code ?? 400);
            entryId = postResult.Data;
        }
        catch (UnitOfWorkAbortException ex)
        {
            return Fail<RevaluationPreviewDto>(ex.Result.Message ?? "Revaluation failed.", ex.Result.Code ?? 400);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<RevaluationPreviewDto>("The revaluation conflicted with a concurrent operation. Reload and retry.", 409);
        }

        data.JournalEntryId = entryId;
        return Ok(data);
    }

    /// <summary>
    /// 计算逐科目重估行（单次 DB 条件求和聚合）。范围：外币限定（Currency != null && != base）的
    /// Asset/Liability 叶子；inactive 带余额者给 SkipReason 不过账；缺汇率整单 400 列出币种。
    /// </summary>
    private async Task<Result<RevaluationPreviewDto>> BuildPreviewAsync(DateTime asOfInput, List<Guid>? accountIds, CancellationToken cancellationToken)
    {
        // 缺省 DateTime（0001-01-01）会得到空候选集静默 no-op；显式要求提供基准日
        if (asOfInput == default)
            return Fail<RevaluationPreviewDto>("AsOf date is required.", 400);

        var asOf = asOfInput.ToUtcDate();
        var baseCurrency = _helper.NormalizeCurrency(null);
        var accountFilter = accountIds != null && accountIds.Count > 0 ? accountIds.ToHashSet() : null;

        var candidates = await _accountRepository.AsNoTracking()
            .Where(a => a.Currency != null && a.Currency != baseCurrency && !a.IsGroup &&
                        (a.RootType == AccountRootType.Asset || a.RootType == AccountRootType.Liability) &&
                        (accountFilter == null || accountFilter.Contains(a.Id)))
            .Select(a => new { a.Id, a.Code, a.Name, a.Currency, a.IsActive })
            .ToListAsync(cancellationToken);

        var empty = new RevaluationPreviewDto { AsOf = asOf, BaseCurrency = baseCurrency, Rows = [], TotalAdjustment = 0m };
        if (candidates.Count == 0)
            return Ok(empty);

        var candidateIds = candidates.Select(c => c.Id).ToList();
        var aggregates = await _journalLineRepository.AsNoTracking()
            .Where(l => l.IsPosted && l.PostingDate <= asOf && candidateIds.Contains(l.AccountId))
            .Join(_accountRepository.AsNoTracking(), l => l.AccountId, a => a.Id, (l, a) => new { l, AccountCurrency = a.Currency })
            .GroupBy(x => x.l.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                BookBase = g.Sum(x => x.l.Debit) - g.Sum(x => x.l.Credit),
                // 交易币余额只计科目本币行；本位币价值调整行（重估/realized FX）天然排除
                TxnBalance = g.Sum(x => x.l.Currency == x.AccountCurrency ? x.l.TxnDebit - x.l.TxnCredit : 0m)
            })
            .ToDictionaryAsync(x => x.AccountId, cancellationToken);

        // 需要汇率的科目：启用且有余额
        var neededCurrencies = candidates
            .Where(c => c.IsActive && aggregates.TryGetValue(c.Id, out var a) && (a.TxnBalance != 0m || a.BookBase != 0m))
            .Select(c => c.Currency!)
            .Distinct()
            .ToList();

        var rates = new Dictionary<string, decimal>();
        var missing = new List<string>();
        foreach (var currency in neededCurrencies)
        {
            var rate = await _exchangeRateService.ResolveRateAsync(currency, baseCurrency, asOf, cancellationToken);
            if (rate.HasValue)
                rates[currency] = rate.Value;
            else
                missing.Add(currency);
        }
        if (missing.Count > 0)
            return Fail<RevaluationPreviewDto>(
                $"No exchange rate available for {string.Join(", ", missing)} -> {baseCurrency} on {asOf:yyyy-MM-dd}. Add the missing rates and retry.", 400);

        var rows = new List<RevaluationRowDto>();
        var totalAdjustment = 0m;
        foreach (var c in candidates.OrderBy(c => c.Code, StringComparer.Ordinal))
        {
            aggregates.TryGetValue(c.Id, out var agg);
            var txnBalance = agg?.TxnBalance ?? 0m;
            var bookBase = agg?.BookBase ?? 0m;
            if (txnBalance == 0m && bookBase == 0m)
                continue; // 无余额科目不进结果

            if (!c.IsActive)
            {
                rows.Add(new RevaluationRowDto
                {
                    AccountId = c.Id,
                    Code = c.Code,
                    Name = c.Name,
                    Currency = c.Currency!,
                    TxnBalance = txnBalance,
                    Rate = 0m,
                    TargetBase = 0m,
                    BookBase = bookBase,
                    Adjustment = 0m,
                    SkipReason = "Account is inactive; reactivate it to include it in a revaluation."
                });
                continue;
            }

            var rate = rates[c.Currency!];
            var targetBase = _helper.Round(txnBalance * rate);
            var adjustment = targetBase - bookBase;
            rows.Add(new RevaluationRowDto
            {
                AccountId = c.Id,
                Code = c.Code,
                Name = c.Name,
                Currency = c.Currency!,
                TxnBalance = txnBalance,
                Rate = rate,
                TargetBase = targetBase,
                BookBase = bookBase,
                Adjustment = adjustment
            });
            totalAdjustment += adjustment;
        }

        return Ok(new RevaluationPreviewDto
        {
            AsOf = asOf,
            BaseCurrency = baseCurrency,
            Rows = rows,
            TotalAdjustment = totalAdjustment
        });
    }
}
