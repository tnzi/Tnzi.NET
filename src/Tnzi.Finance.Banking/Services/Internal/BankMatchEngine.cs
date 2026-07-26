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
public sealed class BankMatchEngine
{
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
    public async Task<List<BankMatchCandidate>> GetCandidatesAsync(Guid accountId, decimal amount, CancellationToken cancellationToken)
    {
        var reconLines = _reconLineRepository.AsNoTracking();
        var occupied = _bankTxnRepository.AsNoTracking().Where(bt => bt.MatchedJournalLineId != null);

        var candidates = await _journalLineRepository.AsNoTracking()
            .Where(l => l.AccountId == accountId && l.IsPosted &&
                        (l.Debit - l.Credit) == amount &&
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

        return candidates;
    }

    /// <summary>
    /// 对单条流水计算建议匹配（无命中返回 null）。
    /// </summary>
    public async Task<BankMatchSuggestion?> SuggestAsync(BankTransaction txn, CancellationToken cancellationToken)
    {
        Check.NotNull(txn);

        var candidates = await GetCandidatesAsync(txn.AccountId, txn.Amount, cancellationToken);
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
