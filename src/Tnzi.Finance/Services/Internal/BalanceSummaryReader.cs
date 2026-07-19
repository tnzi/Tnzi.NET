namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 报表余额聚合读路径（双路径封装：<see cref="Options.FinanceOptions.UseBalanceSummary"/> 门控）
/// </summary>
/// <remarks>
/// 关（默认）：原样从总账 <see cref="JournalLine"/> 做 DB 级 GroupBy/条件求和（与历史实现等价）。
/// 开：整月走 <see cref="AccountPeriodBalance"/> 汇总桶 + 头尾残月走明细的混合读。
///
/// 残月分解：给定 [lo, toExclusive)——firstFullMonthStart = lo 为月初 ? lo : 下月初；
/// lastFullMonthEndExclusive = toExclusive 为月初 ? toExclusive : 当月初。二者间存在整月时
/// summary 取 Period ∈ [P(firstFullMonthStart), P(toExclusive))、头明细 = [lo, firstFullMonthStart)
/// （lo 非月初）、尾明细 = [lastFullMonthEndExclusive, toExclusive)（toExclusive 非月初）；
/// **无整月（区间落在同月内、或跨部分月边界但无完整月）→ 纯明细单段 [lo, toExclusive) 防双计**。
/// lo = null（期初 &lt; from）退化为 summary（Period &lt; P(toExclusive)）+ 尾残月。
///
/// 切换矩阵：TB/BS/P&amp;L/CashFlow 聚合与 GL 头部/CSV 期初 ✅ 汇总；GL 行明细/跨页前缀和/CSV 行
/// ❌（行序依赖）、TaxSummary ❌（TaxRateId 不在键内）、Aging ❌（读单据表）——均留在明细路径。
/// </remarks>
public sealed class BalanceSummaryReader
{
    private readonly IReadOnlyRepository<JournalLine, Guid> _lineRepository;
    private readonly IReadOnlyRepository<AccountPeriodBalance, Guid> _bucketRepository;
    private readonly bool _useSummary;

    public BalanceSummaryReader(
        IReadOnlyRepository<JournalLine, Guid> lineRepository,
        IReadOnlyRepository<AccountPeriodBalance, Guid> bucketRepository,
        IOptionsSnapshot<FinanceOptions> options)
    {
        _lineRepository = Check.NotNull(lineRepository);
        _bucketRepository = Check.NotNull(bucketRepository);
        _useSummary = Check.NotNull(options).Value.UseBalanceSummary;
    }

    private IQueryable<JournalLine> PostedLines => _lineRepository.AsNoTracking().Where(l => l.IsPosted);
    private IQueryable<AccountPeriodBalance> Buckets => _bucketRepository.AsNoTracking();

    /// <summary>逐科目本位币借贷合计（BS：from = null 全期累计；P&amp;L：区间 [from, toExclusive)）</summary>
    public async Task<Dictionary<Guid, DebitCredit>> SumByAccountAsync(
        DateTime? from, DateTime toExclusive, CancellationToken cancellationToken = default)
    {
        if (!_useSummary)
        {
            var predicate = from is { } f
                ? (Expression<Func<JournalLine, bool>>)(l => l.PostingDate >= f && l.PostingDate < toExclusive)
                : (l => l.PostingDate < toExclusive);
            return await SumDetailGroupedAsync(predicate, cancellationToken);
        }

        return await SumRangeAsync(accountIds: null, from, toExclusive, cancellationToken);
    }

    /// <summary>逐科目期初（&lt; from）+ 期间（[from, toExclusive)）本位币借贷合计（TB / CashFlow）</summary>
    public async Task<Dictionary<Guid, PeriodSums>> SumOpeningAndPeriodByAccountAsync(
        DateTime from, DateTime toExclusive, CancellationToken cancellationToken = default)
    {
        if (!_useSummary)
        {
            // 期初 + 期间借贷四项条件求和合并为单次全账本扫描（TB 顺带两查询合一）
            var rows = await PostedLines
                .Where(l => l.PostingDate < toExclusive)
                .GroupBy(l => l.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    OpeningDebit = g.Sum(l => l.PostingDate < from ? l.Debit : 0m),
                    OpeningCredit = g.Sum(l => l.PostingDate < from ? l.Credit : 0m),
                    PeriodDebit = g.Sum(l => l.PostingDate >= from ? l.Debit : 0m),
                    PeriodCredit = g.Sum(l => l.PostingDate >= from ? l.Credit : 0m)
                })
                .ToListAsync(cancellationToken);
            return rows.ToDictionary(r => r.AccountId,
                r => new PeriodSums(r.OpeningDebit, r.OpeningCredit, r.PeriodDebit, r.PeriodCredit));
        }

        var opening = await SumRangeAsync(accountIds: null, null, from, cancellationToken);
        var period = await SumRangeAsync(accountIds: null, from, toExclusive, cancellationToken);
        return Merge(opening, period);
    }

    /// <summary>单科目期初（&lt; from）+ 期间（[from, toExclusive)）本位币借贷合计（GL 头部 / CSV 期初）</summary>
    public async Task<PeriodSums> SumOpeningAndPeriodForAccountAsync(
        Guid accountId, DateTime from, DateTime toExclusive, CancellationToken cancellationToken = default)
    {
        if (!_useSummary)
        {
            var sums = await PostedLines
                .Where(l => l.AccountId == accountId && l.PostingDate < toExclusive)
                .GroupBy(l => 1)
                .Select(g => new
                {
                    OpeningDebit = g.Sum(l => l.PostingDate < from ? l.Debit : 0m),
                    OpeningCredit = g.Sum(l => l.PostingDate < from ? l.Credit : 0m),
                    PeriodDebit = g.Sum(l => l.PostingDate >= from ? l.Debit : 0m),
                    PeriodCredit = g.Sum(l => l.PostingDate >= from ? l.Credit : 0m)
                })
                .FirstOrDefaultAsync(cancellationToken);
            return sums == null
                ? default
                : new PeriodSums(sums.OpeningDebit, sums.OpeningCredit, sums.PeriodDebit, sums.PeriodCredit);
        }

        var ids = new[] { accountId };
        var opening = await SumRangeAsync(ids, null, from, cancellationToken);
        var period = await SumRangeAsync(ids, from, toExclusive, cancellationToken);
        opening.TryGetValue(accountId, out var o);
        period.TryGetValue(accountId, out var p);
        return new PeriodSums(o.Debit, o.Credit, p.Debit, p.Credit);
    }

    /// <summary>
    /// 指定科目集的累计本位币借贷合计（截至 toExclusive，含全部历史；科目表余额面）
    /// </summary>
    /// <remarks>
    /// 与 <see cref="SumByAccountAsync"/> 的 <c>from = null</c> 同口径，只是把科目集下推到
    /// 查询里——科目表按页只要几十个科目的余额，不必为此扫全账本分组。
    /// 无分录的科目不出现在结果里（调用方按缺省 0 处理）。
    /// </remarks>
    public async Task<Dictionary<Guid, DebitCredit>> SumCumulativeByAccountsAsync(
        IReadOnlyCollection<Guid> accountIds, DateTime toExclusive, CancellationToken cancellationToken = default)
    {
        Check.NotNull(accountIds);
        if (accountIds.Count == 0)
            return new Dictionary<Guid, DebitCredit>();

        if (!_useSummary)
            return await SumDetailGroupedAsync(
                l => accountIds.Contains(l.AccountId) && l.PostingDate < toExclusive, cancellationToken);

        return await SumRangeAsync(accountIds, null, toExclusive, cancellationToken);
    }

    // ---- 汇总路径核心：残月分解 → summary 桶 + 头尾明细 ----

    private async Task<Dictionary<Guid, DebitCredit>> SumRangeAsync(
        IReadOnlyCollection<Guid>? accountIds, DateTime? lo, DateTime toExclusive, CancellationToken cancellationToken)
    {
        var d = Decompose(lo, toExclusive);
        var result = new Dictionary<Guid, DebitCredit>();

        if (d.Single is { } single)
        {
            AddInto(result, await SumDetailAsync(accountIds, single.Lo, single.HiExclusive, cancellationToken));
            return result;
        }

        AddInto(result, await SumSummaryAsync(accountIds, d.SummaryFromPeriod, d.SummaryToPeriodExclusive, cancellationToken));
        if (d.Head is { } head)
            AddInto(result, await SumDetailAsync(accountIds, head.Lo, head.HiExclusive, cancellationToken));
        if (d.Tail is { } tail)
            AddInto(result, await SumDetailAsync(accountIds, tail.Lo, tail.HiExclusive, cancellationToken));
        return result;
    }

    /// <summary>明细段 [lo, hiExclusive) 的逐科目本位币借贷合计（accountIds 非空则限定科目集）</summary>
    private async Task<Dictionary<Guid, DebitCredit>> SumDetailAsync(
        IReadOnlyCollection<Guid>? accountIds, DateTime lo, DateTime hiExclusive, CancellationToken cancellationToken)
    {
        var query = PostedLines.Where(l => l.PostingDate >= lo && l.PostingDate < hiExclusive);
        if (accountIds is { Count: > 0 })
            query = query.Where(l => accountIds.Contains(l.AccountId));

        var rows = await query
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.AccountId, r => new DebitCredit(r.Debit, r.Credit));
    }

    /// <summary>summary 桶 Period ∈ [fromPeriod, toPeriodExclusive) 的逐科目本位币借贷合计</summary>
    private async Task<Dictionary<Guid, DebitCredit>> SumSummaryAsync(
        IReadOnlyCollection<Guid>? accountIds, int? fromPeriod, int toPeriodExclusive, CancellationToken cancellationToken)
    {
        var query = Buckets.Where(b => b.Period < toPeriodExclusive);
        if (fromPeriod is { } fp)
            query = query.Where(b => b.Period >= fp);
        if (accountIds is { Count: > 0 })
            query = query.Where(b => accountIds.Contains(b.AccountId));

        var rows = await query
            .GroupBy(b => b.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(b => b.Debit), Credit = g.Sum(b => b.Credit) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.AccountId, r => new DebitCredit(r.Debit, r.Credit));
    }

    private async Task<Dictionary<Guid, DebitCredit>> SumDetailGroupedAsync(
        Expression<Func<JournalLine, bool>> predicate, CancellationToken cancellationToken)
    {
        var rows = await PostedLines
            .Where(predicate)
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.AccountId, r => new DebitCredit(r.Debit, r.Credit));
    }

    /// <summary>
    /// 把 [lo, toExclusive) 分解为 summary 期间范围 + 头/尾明细段，或（无整月时）纯明细单段。
    /// </summary>
    private static RangeDecomposition Decompose(DateTime? lo, DateTime toExclusive)
    {
        var firstFullMonthStart = lo == null
            ? DateTime.MinValue
            : (BalancePeriod.IsMonthStart(lo.Value) ? lo.Value : BalancePeriod.NextMonthStart(lo.Value));
        var lastFullMonthEndExclusive = BalancePeriod.IsMonthStart(toExclusive)
            ? toExclusive
            : BalancePeriod.MonthStart(toExclusive);

        // 无完整月：区间落在同一月内、或跨部分月边界但不含任何整月 → 纯明细单段防双计
        if (lo != null && firstFullMonthStart >= lastFullMonthEndExclusive)
            return new RangeDecomposition(null, 0, null, null, new Segment(lo.Value, toExclusive));

        // P(lastFullMonthEndExclusive) == P(toExclusive)（月初/月中同月），直接用 toExclusive 的期间键
        var summaryFrom = lo == null ? (int?)null : BalancePeriod.Of(firstFullMonthStart);
        var summaryToExclusive = BalancePeriod.Of(toExclusive);

        Segment? head = lo != null && !BalancePeriod.IsMonthStart(lo.Value)
            ? new Segment(lo.Value, firstFullMonthStart)
            : null;
        Segment? tail = !BalancePeriod.IsMonthStart(toExclusive)
            ? new Segment(lastFullMonthEndExclusive, toExclusive)
            : null;

        return new RangeDecomposition(summaryFrom, summaryToExclusive, head, tail, null);
    }

    private static void AddInto(Dictionary<Guid, DebitCredit> target, Dictionary<Guid, DebitCredit> source)
    {
        foreach (var (accountId, sum) in source)
        {
            target[accountId] = target.TryGetValue(accountId, out var existing)
                ? new DebitCredit(existing.Debit + sum.Debit, existing.Credit + sum.Credit)
                : sum;
        }
    }

    private static Dictionary<Guid, PeriodSums> Merge(
        Dictionary<Guid, DebitCredit> opening, Dictionary<Guid, DebitCredit> period)
    {
        var result = new Dictionary<Guid, PeriodSums>();
        foreach (var accountId in opening.Keys.Union(period.Keys))
        {
            opening.TryGetValue(accountId, out var o);
            period.TryGetValue(accountId, out var p);
            result[accountId] = new PeriodSums(o.Debit, o.Credit, p.Debit, p.Credit);
        }
        return result;
    }

    private readonly record struct Segment(DateTime Lo, DateTime HiExclusive);

    private readonly record struct RangeDecomposition(
        int? SummaryFromPeriod,
        int SummaryToPeriodExclusive,
        Segment? Head,
        Segment? Tail,
        Segment? Single);
}

/// <summary>逐科目本位币借贷合计（有符号余额 = Debit − Credit）</summary>
public readonly record struct DebitCredit(decimal Debit, decimal Credit);

/// <summary>逐科目期初 + 期间的本位币借贷合计</summary>
public readonly record struct PeriodSums(decimal OpeningDebit, decimal OpeningCredit, decimal PeriodDebit, decimal PeriodCredit);
