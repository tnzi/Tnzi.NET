
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
        Interlocked.Increment(ref _transactionDepth);
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