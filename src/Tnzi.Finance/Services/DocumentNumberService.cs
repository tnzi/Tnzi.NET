namespace Tnzi.Finance.Services;

/// <summary>
/// 单据连续编号服务
/// </summary>
/// <remarks>
/// 并发正确性依赖数据库行级锁：先对序列行执行原子 UPDATE（持有行锁直至事务结束），
/// 再在同一事务内读回已递增的值。并发分配方在 UPDATE 处串行等待，
/// 因此同一作用域的编号严格递增且无重复；事务回滚时号码随之回收（无缺口）。
/// 已存在活动事务时直接加入调用方事务；否则自建事务保证 UPDATE 与读回的原子性。
/// </remarks>
public class DocumentNumberService : ApplicationService, IDocumentNumberService
{
    private const int MaxInitAttempts = 3;

    private readonly IRepository<DocumentSequence, Guid> _repository;

    public DocumentNumberService(IServiceProvider serviceProvider, IRepository<DocumentSequence, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<long> NextAsync(string scope, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        // 已在事务内：直接分配并加入调用方事务（回滚时号码回收，保证无缺口）
        if (UnitOfWorkManager?.IsEnabledTransaction == true)
            return await AllocateAsync(scope, cancellationToken);

        // 无事务：自建事务，保证 UPDATE 行锁与读回处于同一事务
        return await ExecuteInUnitOfWorkAsync(ct => AllocateAsync(scope, ct), cancellationToken);
    }

    public async Task<string> NextFormattedAsync(string scope, string? prefix = null, int padding = 0, CancellationToken cancellationToken = default)
    {
        var value = await NextAsync(scope, cancellationToken);
        var number = padding > 0 ? value.ToString($"D{padding}") : value.ToString();
        return $"{prefix}{number}";
    }

    private async Task<long> AllocateAsync(string scope, CancellationToken cancellationToken)
    {
        // 确保物理事务已开启（框架事务为延迟开启：首次 UoW SaveChanges 才 BEGIN）。
        // 若不先开启，后续 ExecuteUpdate 会在自动提交模式下执行，
        // 行锁不持有到事务结束，读回值可能与本次递增不对应（重号）且回滚无法回收号码（缺口）。
        await _repository.EnsureTransactionStartedAsync(cancellationToken);

        for (var attempt = 1; attempt <= MaxInitAttempts; attempt++)
        {
            // 原子递增；行锁将并发分配串行化
            var updated = await _repository.AsQueryable(true)
                .Where(s => s.Scope == scope)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextValue, x => x.NextValue + 1), cancellationToken);

            if (updated > 0)
            {
                // 同一事务内读回自身写入（行锁保证读回值即本次分配结果）
                var next = await _repository.AsQueryable()
                    .Where(s => s.Scope == scope)
                    .Select(s => s.NextValue)
                    .FirstAsync(cancellationToken);
                return next - 1;
            }

            // 序列行不存在：初始化首号；并发首插由唯一索引兜底，冲突后重试走 UPDATE 路径
            var seed = new DocumentSequence { Scope = scope, NextValue = 2 };
            try
            {
                await _repository.InsertAsync(seed, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
                return 1;
            }
            catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
            {
                // 撤销失败的插入，避免残留 Added 实体在事务提交时再次触发冲突
                await _repository.DeleteAsync(seed, cancellationToken);
                Logger.LogDebug("Concurrent initialization detected for sequence scope '{Scope}', retrying (attempt {Attempt}).", scope, attempt);
            }
        }

        throw new ConflictException($"Failed to allocate a document number for scope '{scope}' after {MaxInitAttempts} attempts.");
    }
}
