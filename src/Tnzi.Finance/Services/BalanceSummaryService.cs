using System.Diagnostics;

namespace Tnzi.Finance.Services;

/// <summary>
/// 科目期间余额汇总的重建与校验服务
/// </summary>
/// <remarks>
/// quiesce = 对当前租户的 JournalEntry 序列行自赋值 <c>ExecuteUpdate</c> 取 X 锁（持有到事务提交），
/// 把重建/校验对全部过账串行化——过账均经该行原子递增，锁序（单据号 → JE 序列 → 汇总桶）无环。
/// 聚合：已过账行 DB 侧按 (AccountId, Year, Month, Currency) GroupBy，Period = Year*100+Month 由结果集
/// 客户端计算（避免 SQL 侧算术翻译差异，DB 仍承担分组重活）。
/// </remarks>
public class BalanceSummaryService : ApplicationService, IBalanceSummaryService
{
    /// <summary>差异明细响应上限（诊断用，TotalDifferences 仍反映真实总数）</summary>
    private const int MaxDifferences = 100;

    private readonly IReadOnlyRepository<JournalLine, Guid> _lineRepository;
    private readonly IRepository<AccountPeriodBalance, Guid> _bucketRepository;
    private readonly IRepository<DocumentSequence, Guid> _sequenceRepository;
    private readonly ICurrentTenant? _currentTenant;

    /// <param name="serviceProvider">服务提供者（基类延迟解析用）</param>
    /// <param name="lineRepository">凭证行只读仓储</param>
    /// <param name="bucketRepository">科目期间余额桶仓储</param>
    /// <param name="sequenceRepository">文档序列仓储（重算游标）</param>
    /// <param name="currentTenant">
    /// 多租户未启用时可能未注册，故为可选构造注入（与 <see cref="BalanceSummaryMaintainer"/> 同源解析租户）。
    /// </param>
    public BalanceSummaryService(
        IServiceProvider serviceProvider,
        IReadOnlyRepository<JournalLine, Guid> lineRepository,
        IRepository<AccountPeriodBalance, Guid> bucketRepository,
        IRepository<DocumentSequence, Guid> sequenceRepository,
        ICurrentTenant? currentTenant = null)
        : base(serviceProvider)
    {
        _lineRepository = Check.NotNull(lineRepository);
        _bucketRepository = Check.NotNull(bucketRepository);
        _sequenceRepository = Check.NotNull(sequenceRepository);
        _currentTenant = currentTenant;
    }

    public async Task<Result<BalanceSummaryRebuildDto>> RebuildAsync(CancellationToken cancellationToken = default)
    {
        var tenant = ResolveTenant();
        var stopwatch = Stopwatch.StartNew();

        var dto = await ExecuteInUnitOfWorkAsync(async token =>
        {
            await QuiesceAsync(token);

            var expected = await AggregateExpectedAsync(token);

            // 幂等重建：先清当前租户全部桶（租户维度由全局查询过滤器承担），再整批插入
            await _bucketRepository.AsQueryable(true).ExecuteDeleteAsync(token);

            if (expected.Count > 0)
            {
                var buckets = expected.Select(e => new AccountPeriodBalance
                {
                    TenantId = tenant,
                    AccountId = e.AccountId,
                    Period = e.Period,
                    Currency = e.Currency,
                    Debit = e.Debit,
                    Credit = e.Credit,
                    TxnDebit = e.TxnDebit,
                    TxnCredit = e.TxnCredit,
                    LineCount = e.LineCount
                }).ToList();

                await _bucketRepository.InsertManyAsync(buckets, token);
                await _bucketRepository.SaveChangesAsync(token);
            }

            return new BalanceSummaryRebuildDto
            {
                Buckets = expected.Count,
                Lines = expected.Sum(e => (long)e.LineCount)
            };
        }, cancellationToken);

        stopwatch.Stop();
        dto.DurationMs = stopwatch.ElapsedMilliseconds;
        return Ok(dto);
    }

    public async Task<Result<BalanceSummaryVerifyDto>> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var dto = await ExecuteInUnitOfWorkAsync(async token =>
        {
            await QuiesceAsync(token);

            var expected = await AggregateExpectedAsync(token);
            var stored = await _bucketRepository.AsNoTracking().ToListAsync(token);

            var expectedMap = expected.ToDictionary(e => (e.AccountId, e.Period, e.Currency));
            var storedMap = stored.ToDictionary(b => (b.AccountId, b.Period, b.Currency));

            var differences = new List<BalanceSummaryDifferenceDto>();
            var total = 0;

            void Record(BalanceSummaryDifferenceKind kind, Guid accountId, int period, string currency,
                decimal expectedDebit, decimal expectedCredit, decimal storedDebit, decimal storedCredit)
            {
                total++;
                if (differences.Count < MaxDifferences)
                {
                    differences.Add(new BalanceSummaryDifferenceDto
                    {
                        AccountId = accountId,
                        Period = period,
                        Currency = currency,
                        Kind = kind,
                        ExpectedDebit = expectedDebit,
                        ExpectedCredit = expectedCredit,
                        StoredDebit = storedDebit,
                        StoredCredit = storedCredit
                    });
                }
            }

            foreach (var e in expected)
            {
                if (!storedMap.TryGetValue((e.AccountId, e.Period, e.Currency), out var b))
                {
                    Record(BalanceSummaryDifferenceKind.Missing, e.AccountId, e.Period, e.Currency,
                        e.Debit, e.Credit, 0m, 0m);
                }
                else if (b.Debit != e.Debit || b.Credit != e.Credit ||
                         b.TxnDebit != e.TxnDebit || b.TxnCredit != e.TxnCredit || b.LineCount != e.LineCount)
                {
                    Record(BalanceSummaryDifferenceKind.Mismatch, e.AccountId, e.Period, e.Currency,
                        e.Debit, e.Credit, b.Debit, b.Credit);
                }
            }

            foreach (var b in stored)
            {
                if (!expectedMap.ContainsKey((b.AccountId, b.Period, b.Currency)))
                {
                    Record(BalanceSummaryDifferenceKind.Extra, b.AccountId, b.Period, b.Currency,
                        0m, 0m, b.Debit, b.Credit);
                }
            }

            return new BalanceSummaryVerifyDto
            {
                IsConsistent = total == 0,
                CheckedBuckets = expected.Count,
                TotalDifferences = total,
                Differences = differences
            };
        }, cancellationToken);

        return Ok(dto);
    }

    /// <summary>
    /// 对当前租户的 JournalEntry 序列行自赋值取 X 锁，把重建/校验对全部过账串行化。
    /// 序列行不存在（尚无过账）跳过——与首笔过账竞态最坏 409 重试，良性。
    /// </summary>
    private async Task QuiesceAsync(CancellationToken cancellationToken)
    {
        await _sequenceRepository.EnsureTransactionStartedAsync(cancellationToken);
        await _sequenceRepository.AsQueryable(true)
            .Where(s => s.Scope == LedgerPostingEngine.JournalEntrySequenceScope)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextValue, x => x.NextValue), cancellationToken);
    }

    /// <summary>
    /// 当前租户全部已过账行按 (AccountId, yyyyMM, Currency) 聚合出期望桶。
    /// DB 侧按 Year/Month 分组，Period = Year*100+Month 客户端计算。
    /// </summary>
    private async Task<List<ExpectedBucket>> AggregateExpectedAsync(CancellationToken cancellationToken)
    {
        var raw = await _lineRepository.AsNoTracking()
            .Where(l => l.IsPosted)
            .GroupBy(l => new { l.AccountId, l.PostingDate.Year, l.PostingDate.Month, l.Currency })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.Year,
                g.Key.Month,
                g.Key.Currency,
                Debit = g.Sum(l => l.Debit),
                Credit = g.Sum(l => l.Credit),
                TxnDebit = g.Sum(l => l.TxnDebit),
                TxnCredit = g.Sum(l => l.TxnCredit),
                LineCount = g.Count()
            })
            .ToListAsync(cancellationToken);

        return raw.Select(r => new ExpectedBucket(
            r.AccountId, r.Year * 100 + r.Month, r.Currency,
            r.Debit, r.Credit, r.TxnDebit, r.TxnCredit, r.LineCount)).ToList();
    }

    private Guid? ResolveTenant()
        => _currentTenant?.Id ?? CurrentUser?.TenantId;

    private sealed record ExpectedBucket(
        Guid AccountId, int Period, string Currency,
        decimal Debit, decimal Credit, decimal TxnDebit, decimal TxnCredit, int LineCount);
}
