
namespace Tnzi.EFCore;

/// <summary>
/// EF Core 工作单元实现，支持嵌套事务和延迟事务开始
/// </summary>
public class EFCoreUnitOfWork<TDbContext> : IUnitOfWork, IAsyncDisposable
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly ILogger<EFCoreUnitOfWork<TDbContext>>? _logger;
    private readonly IPerformanceMonitorService? _monitor;
    private IDbContextTransaction? _transaction;
    private int _transactionDepth;
    private readonly SemaphoreSlim _transactionSemaphore = new(1, 1);
    private bool _hasCommitted;

    public EFCoreUnitOfWork(
        TDbContext dbContext,
        ILogger<EFCoreUnitOfWork<TDbContext>>? logger = null,
        IServiceProvider? serviceProvider = null)
    {
        _dbContext = Check.NotNull(dbContext);
        _logger = logger;
        _monitor = serviceProvider?.GetService<IPerformanceMonitorService>();
    }

    /// <summary>
    /// 获取是否已启用事务（存在活跃事务或事务深度 > 0）
    /// </summary>
    public bool IsEnabledTransaction => _transaction != null || Volatile.Read(ref _transactionDepth) > 0;

    /// <summary>
    /// 获取事务嵌套深度
    /// </summary>
    public int TransactionDepth => Volatile.Read(ref _transactionDepth);

    /// <summary>
    /// 启用事务（标记需要事务，但不立即开始，支持嵌套）
    /// 这是延迟事务开始的关键：调用此方法后，事务会在第一次 SaveChanges 时才真正开始
    /// </summary>
    public void EnableTransaction()
    {
        // 深度 0 → 1 = 开启一个**新的最外层事务**（不是嵌套）。此时必须清掉上一个
        // 事务留下的 _hasCommitted 标志：该标志的用途是「同一个事务不要提交两次」，
        // 而工作单元实例在整个 DI 作用域内被复用（UnitOfWorkManager 的 _unitOfWorks
        // 只在回滚/释放时清空，提交成功后不清）。不清的话，同一作用域内**顺序**发生
        // 的第二个 ExecuteInUnitOfWorkAsync 会在提交时命中 `_hasCommitted` 早退 ——
        // 既不 SaveChanges 也不提交，变更留在变更跟踪器里被静默丢弃，且不抛任何异常。
        //
        // 触发场景是一个服务顺序调用两个各自带事务的服务（如银行流水的
        // 「建单据 → 过账 → 确认匹配」），第二段的写入凭空消失。2026-06-11 修过该
        // 标志毒化的**嵌套**分支（GetUnitOfWork 深度同步），这里是它的顺序分支。
        if (Interlocked.Increment(ref _transactionDepth) == 1)
        {
            _hasCommitted = false;
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 如果已启用事务但未开始，则延迟开始事务
        if (IsEnabledTransaction && _transaction == null)
        {
            await BeginTransactionAsync(cancellationToken);
        }

        var sw = Stopwatch.StartNew();
        var result = await _dbContext.SaveChangesAsync(cancellationToken);
        sw.Stop();

        var isSlow = sw.ElapsedMilliseconds > 500;
        if (isSlow)
        {
            _logger?.LogWarning("Slow SaveChangesAsync detected: {Elapsed}ms", sw.ElapsedMilliseconds);
        }

        _monitor?.RecordDbQuery("SaveChangesAsync", sw.Elapsed, isSlow);

        return result;
    }

    /// <summary>
    /// 确保物理数据库事务已开启（幂等；供事务内的裸 SQL 操作加入事务）
    /// </summary>
    public async Task EnsureTransactionStartedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabledTransaction || _transaction != null)
        {
            return;
        }

        await _transactionSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 双重检查：并发调用时后进入者直接返回，不重复 BEGIN
            if (_transaction == null)
            {
                _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                _hasCommitted = false;
            }
        }
        finally
        {
            _transactionSemaphore.Release();
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // 使用 SemaphoreSlim 保护事务初始化，防止并发调用导致重复创建事务
        await _transactionSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 如果已启用事务，说明是嵌套调用，只增加深度（在 EnableTransaction 中已处理）
            // 这里只处理真正开始数据库事务的情况
            if (_transaction != null)
            {
                // 如果事务深度 > 1，说明是嵌套调用，不需要重新开始事务
                if (Volatile.Read(ref _transactionDepth) > 1)
                {
                    return;
                }

                throw new InvalidOperationException("Transaction already started");
            }

            // 只有在事务栈深度为 1 或未启用事务时才真正开始数据库事务
            _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            _hasCommitted = false;
        }
        finally
        {
            _transactionSemaphore.Release();
        }
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_hasCommitted)
        {
            return;
        }

        var currentDepth = Volatile.Read(ref _transactionDepth);
        if (currentDepth == 0)
        {
            throw new InvalidOperationException("Transaction is not enabled. Call EnableTransaction() before committing.");
        }

        // 如果事务深度 > 1，说明是嵌套调用，只减少深度，不真正提交
        if (currentDepth > 1)
        {
            Interlocked.Decrement(ref _transactionDepth);
            return;
        }

        // 减少深度到 0（最外层提交）
        Interlocked.Decrement(ref _transactionDepth);

        try
        {
            // 确保所有待处理的更改都已保存
            // 如果已启用事务但未实际开始，SaveChangesAsync 会触发事务开启（延迟开启机制）
            if (_dbContext.ChangeTracker.HasChanges())
            {
                await SaveChangesAsync(cancellationToken);
            }

            // 如果有活跃事务，则提交它
            // 注意：如果没有更改且没开启过物理事务，_transaction 可能为 null，此时直接结束
            if (_transaction != null)
            {
                await _transaction.CommitAsync(cancellationToken);
                _hasCommitted = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error committing transaction, rolling back");
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_hasCommitted)
        {
            return;
        }

        // 重置事务深度（回滚所有嵌套事务）
        Interlocked.Exchange(ref _transactionDepth, 0);

        if (_transaction == null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error rolling back transaction");
            throw;
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
            // 注意：回滚后不应该设置 _hasCommitted = true，因为事务已回滚，不是提交
            // 移除这行，让 _hasCommitted 保持 false，表示事务未成功提交
        }
    }

    #region IDisposable / IAsyncDisposable

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _transaction?.Dispose();
            _transaction = null;
            Interlocked.Exchange(ref _transactionDepth, 0);
            _transactionSemaphore.Dispose();
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed) return;

        if (_transaction != null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
        Interlocked.Exchange(ref _transactionDepth, 0);
        _transactionSemaphore.Dispose();
        _disposed = true;
    }

    #endregion
}