namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 银行流水匹配引擎（候选筛选 + 两级规则；首版限本位币科目）
/// </summary>
/// <remarks>
/// 候选 = 该科目已过账、未 cleared（反连接 <see cref="ReconciliationLine"/>）、未被其它流水占用的
/// <see cref="JournalLine"/>；方向 = 流水金额与行净额（Debit − Credit，本位币）同号且绝对值精确相等。
/// 规则序：①exact-ref（1.0）金额等 + 日期差 ≤ 1 天 + 参考号命中（凭证号相等或备注包含）且唯一；
/// ②amount-date（0.8）金额等 + 日期窗 <c>BankMatchDateWindowDays</c> + 候选唯一；否则不建议。
/// </remarks>
public class BankMatchEngine
{
    /// <summary>一次 IN 查询里最多带多少个金额。</summary>
    /// <remarks>
    /// 上界不是性能调优而是闸门：待匹配流水条数没有上限（一次对账单导入几千行是常态），
    /// 把它们的金额一次全塞进 IN 会撞上各家提供程序对参数个数的硬限制，
    /// 而症状是整个「建议匹配」在库那一侧报错，与流水条数相关、与业务无关。
    /// </remarks>
    private const int AmountBatchSize = 500;

    private readonly IReadOnlyRepository<JournalLine, Guid> _journalLineRepository;
    private readonly IReadOnlyRepository<ReconciliationLine, Guid> _reconLineRepository;
    private readonly IReadOnlyRepository<BankTransaction, Guid> _bankTxnRepository;
    private readonly FinanceOptions _options;

    public BankMatchEngine(
        IReadOnlyRepository<JournalLine, Guid> journalLineRepository,
        IReadOnlyRepository<ReconciliationLine, Guid> reconLineRepository,
        IReadOnlyRepository<BankTransaction, Guid> bankTxnRepository,
        IOptionsSnapshot<FinanceOptions> options)
    {
        _journalLineRepository = Check.NotNull(journalLineRepository);
        _reconLineRepository = Check.NotNull(reconLineRepository);
        _bankTxnRepository = Check.NotNull(bankTxnRepository);
        _options = Check.NotNull(options).Value;
    }

    /// <summary>
    /// 加载与某金额精确匹配的候选行（uncleared + unoccupied + posted，本位币净额同号等值）。
    /// </summary>
    public virtual async Task<List<BankMatchCandidate>> GetCandidatesAsync(Guid accountId, decimal amount, CancellationToken cancellationToken)
    {
        var byAmount = await GetCandidatesByAmountAsync(accountId, new[] { amount }, cancellationToken);
        return byAmount.TryGetValue(amount, out var candidates) ? candidates : new List<BankMatchCandidate>();
    }

    /// <summary>
    /// 一次取回多笔金额的候选行，按金额分组。
    /// </summary>
    /// <remarks>
    /// ★ 存在的理由是「建议匹配」那条路径：逐条流水调 <see cref="GetCandidatesAsync"/> 会把
    /// N 条待匹配流水变成 N 次三层反连接查询，而那 N 次原本还串在同一个物理事务里、
    /// 全程持有写锁 —— 一次导入几千行的对账单点一下「建议匹配」就是几千次往返。
    /// 候选集只取决于科目与金额，与是哪一条流水无关，所以一次取回、内存里按金额分派即可。
    /// <para>
    /// 每组内的顺序与单笔查询逐字相同（同一个 ORDER BY），因为 exact-ref / amount-date
    /// 两条规则都以「候选唯一」为判据，顺序变了结论不变但差异难查。
    /// </para>
    /// </remarks>
    public virtual async Task<Dictionary<decimal, List<BankMatchCandidate>>> GetCandidatesByAmountAsync(
        Guid accountId, IReadOnlyCollection<decimal> amounts, CancellationToken cancellationToken)
    {
        Check.NotNull(amounts);

        var result = new Dictionary<decimal, List<BankMatchCandidate>>();
        if (amounts.Count == 0)
            return result;

        var reconLines = _reconLineRepository.AsNoTracking();
        var occupied = _bankTxnRepository.AsNoTracking().Where(bt => bt.MatchedJournalLineId != null);

        foreach (var chunk in amounts.Distinct().Chunk(AmountBatchSize))
        {
            var slice = chunk.ToList();
            var rows = await _journalLineRepository.AsNoTracking()
                .Where(l => l.AccountId == accountId && l.IsPosted &&
                            slice.Contains(l.Debit - l.Credit) &&
                            !reconLines.Any(rl => rl.JournalLineId == l.Id) &&
                            !occupied.Any(bt => bt.MatchedJournalLineId == l.Id))
                .OrderBy(l => l.PostingDate)
                .ThenBy(l => l.JournalEntry!.Number)
                .ThenBy(l => l.LineNumber)
                .Select(l => new BankMatchCandidate(
                    l.Id,
                    l.JournalEntryId,
                    l.JournalEntry!.Number,
                    l.PostingDate,
                    l.Memo ?? l.JournalEntry.Memo,
                    l.Debit - l.Credit))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.NetAmount, out var list))
                    result[row.NetAmount] = list = new List<BankMatchCandidate>();
                list.Add(row);
            }
        }

        return result;
    }

    /// <summary>
    /// 对单条流水计算建议匹配（无命中返回 null）。
    /// </summary>
    public virtual async Task<BankMatchSuggestion?> SuggestAsync(BankTransaction txn, CancellationToken cancellationToken)
    {
        Check.NotNull(txn);
        var candidates = await GetCandidatesAsync(txn.AccountId, txn.Amount, cancellationToken);
        return Suggest(txn, candidates);
    }

    /// <summary>
    /// 在已取回的候选集上计算建议匹配（纯函数，供批量路径复用）。
    /// </summary>
    public BankMatchSuggestion? Suggest(BankTransaction txn, IReadOnlyList<BankMatchCandidate> candidates)
    {
        Check.NotNull(txn);
        Check.NotNull(candidates);

        if (candidates.Count == 0)
            return null;

        // 规则 1：exact-ref —— 参考号命中 + 日期差 ≤ 1 天，且唯一
        if (!string.IsNullOrWhiteSpace(txn.Reference))
        {
            var reference = txn.Reference.Trim();
            var refHits = candidates
                .Where(c => Math.Abs((c.PostingDate.Date - txn.TxnDate.Date).TotalDays) <= 1 && ReferenceMatches(c, reference))
                .ToList();
            if (refHits.Count == 1)
                return new BankMatchSuggestion(refHits[0].JournalLineId, _options.ExactMatchConfidence, "exact-ref");
        }

        // 规则 2：amount-date —— 金额等 + 日期窗口内 + 候选唯一
        var window = _options.BankMatchDateWindowDays;
        var withinWindow = candidates
            .Where(c => Math.Abs((c.PostingDate.Date - txn.TxnDate.Date).TotalDays) <= window)
            .ToList();
        if (withinWindow.Count == 1)
            return new BankMatchSuggestion(withinWindow[0].JournalLineId, _options.AmountDateMatchConfidence, "amount-date");

        return null;
    }

    private static bool ReferenceMatches(BankMatchCandidate candidate, string reference)
    {
        if (!string.IsNullOrWhiteSpace(candidate.EntryNumber) &&
            string.Equals(candidate.EntryNumber.Trim(), reference, StringComparison.OrdinalIgnoreCase))
            return true;
        return candidate.Memo != null &&
               candidate.Memo.Contains(reference, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>匹配候选行（本位币净额）</summary>
public sealed record BankMatchCandidate(
    Guid JournalLineId,
    Guid JournalEntryId,
    string? EntryNumber,
    DateTime PostingDate,
    string? Memo,
    decimal NetAmount);

/// <summary>匹配建议</summary>
public sealed record BankMatchSuggestion(Guid JournalLineId, decimal Confidence, string Rule);
