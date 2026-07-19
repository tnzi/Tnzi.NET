namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 科目期间余额汇总维护器（过账/冲销的同一事务内正向累加 <see cref="AccountPeriodBalance"/> 桶）
/// </summary>
/// <remarks>
/// 由 <see cref="LedgerPostingEngine"/> 在两处调用：<c>PostAsync</c> 的 Finalize 之后（提交点内）、
/// <c>BuildReversalAsync</c> 编号分配后 return 前（调用方 MUST 在同一 UoW 持久化冲销凭证，
/// 任一后续失败经 UnitOfWorkAbortException/异常连同桶增量一起回滚）。维护**无条件启用**——
/// 读路径才由 <see cref="Options.FinanceOptions.UseBalanceSummary"/> 门控。
///
/// 不变量：Posted 行永不改删（修正 = 冲销）→ 汇总只正向累加，永不需要负增量；冲销凭证的
/// 借贷互换行被当作普通正向增量累加，毛额累加而净额天然归零（与 TB 的 PeriodDebit/Credit 语义一致）。
///
/// 每桶：<see cref="IRepository{TEntity,TKey}.EnsureTransactionStartedAsync"/>（防御幂等，事务内裸 SQL 前置）
/// → <c>ExecuteUpdateAsync</c> 原子累加；命中 0 行则 <c>InsertAsync + SaveChanges</c>，唯一索引冲突
/// （并发首插）撤销 Added 实体 + 重试一次累加（照 <see cref="DocumentNumberService"/> 首插竞态兜底）。
///
/// 锁序无环：单据号 sequence（可选）→ JE sequence → 汇总桶；增量发生时编号锁已由引擎持有
/// （租户级串行化全部过账），桶行锁零新竞争。多实例同库由数据库行锁成立。
///
/// tenant = entry.TenantId ?? ambient——新凭证在 SaveChanges 前尚未 stamp TenantId，
/// 与 <c>AuditPropertyHelper</c> 同源解析 <c>currentTenant?.Id ?? currentUser?.TenantId</c>。
/// </remarks>
public sealed class BalanceSummaryMaintainer
{
    private readonly IRepository<AccountPeriodBalance, Guid> _bucketRepository;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ICurrentUser? _currentUser;

    public BalanceSummaryMaintainer(
        IRepository<AccountPeriodBalance, Guid> bucketRepository,
        ICurrentTenant? currentTenant = null,
        ICurrentUser? currentUser = null)
    {
        _bucketRepository = Check.NotNull(bucketRepository);
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    /// <summary>
    /// 将一张已定稿凭证的分录行累加进对应的月粒度余额桶（须在活动事务内调用）。
    /// </summary>
    public async Task ApplyAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entry);

        // 新凭证 SaveChanges 前未 stamp 租户，退回 ambient 解析（与审计填充同源）
        var tenant = entry.TenantId ?? _currentTenant?.Id ?? _currentUser?.TenantId;

        var groups = entry.Lines
            .GroupBy(l => new BucketKey(l.AccountId, BalancePeriod.Of(l.PostingDate), NormalizeCurrency(l.Currency)))
            .Select(g => new
            {
                g.Key,
                Debit = g.Sum(l => l.Debit),
                Credit = g.Sum(l => l.Credit),
                TxnDebit = g.Sum(l => l.TxnDebit),
                TxnCredit = g.Sum(l => l.TxnCredit),
                LineCount = g.Count()
            })
            .ToList();

        foreach (var g in groups)
        {
            // 事务内裸 SQL（ExecuteUpdate）前 MUST 强开延迟物理事务（幂等；编号分配通常已开启）
            await _bucketRepository.EnsureTransactionStartedAsync(cancellationToken);

            var affected = await IncrementAsync(g.Key, g.Debit, g.Credit, g.TxnDebit, g.TxnCredit, g.LineCount, cancellationToken);
            if (affected > 0)
                continue;

            var bucket = new AccountPeriodBalance
            {
                TenantId = tenant,
                AccountId = g.Key.AccountId,
                Period = g.Key.Period,
                Currency = g.Key.Currency,
                Debit = g.Debit,
                Credit = g.Credit,
                TxnDebit = g.TxnDebit,
                TxnCredit = g.TxnCredit,
                LineCount = g.LineCount
            };

            try
            {
                await _bucketRepository.InsertAsync(bucket, cancellationToken);
                await _bucketRepository.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
            {
                // 并发首插：另一事务已建同键桶。撤销失败的 Added 实体避免提交期重放，改走累加路径。
                await _bucketRepository.DeleteAsync(bucket, cancellationToken);
                var retried = await IncrementAsync(g.Key, g.Debit, g.Credit, g.TxnDebit, g.TxnCredit, g.LineCount, cancellationToken);
                if (retried == 0)
                    throw new ConflictException(
                        $"Failed to maintain the period-balance bucket for account '{g.Key.AccountId}' period {g.Key.Period}.");
            }
        }
    }

    // 租户维度由全局查询过滤器承担（多租户开 → 限当前租户；关 → TenantId 被 Ignore 未映射，
    // 不可出现在查询谓词中），谓词只按 (AccountId, Period, Currency) 命中——与 DocumentNumberService
    // 仅按 Scope 查询、靠过滤器隔离租户同源。
    private Task<int> IncrementAsync(BucketKey key,
        decimal debit, decimal credit, decimal txnDebit, decimal txnCredit, int lineCount, CancellationToken cancellationToken)
        => _bucketRepository.AsQueryable(true)
            .Where(b => b.AccountId == key.AccountId && b.Period == key.Period && b.Currency == key.Currency)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Debit, x => x.Debit + debit)
                .SetProperty(x => x.Credit, x => x.Credit + credit)
                .SetProperty(x => x.TxnDebit, x => x.TxnDebit + txnDebit)
                .SetProperty(x => x.TxnCredit, x => x.TxnCredit + txnCredit)
                .SetProperty(x => x.LineCount, x => x.LineCount + lineCount), cancellationToken);

    private static string NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? string.Empty : currency.Trim().ToUpperInvariant();

    private readonly record struct BucketKey(Guid AccountId, int Period, string Currency);
}
