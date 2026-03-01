
namespace Tnzi.EFCore;

/// <summary>
/// 工作单元管理器实现
/// 用于管理多个 DbContext 的工作单元，支持统一的事务协调
/// </summary>
public class UnitOfWorkManager : IUnitOfWorkManager, IDisposable, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UnitOfWorkManager>? _logger;
    private readonly ConcurrentDictionary<Type, IUnitOfWork> _unitOfWorks = new();
    private int _transactionDepth;
    private volatile List<Type>? _cachedDbContextTypes;

    public UnitOfWorkManager(IServiceProvider serviceProvider, ILogger<UnitOfWorkManager>? logger = null)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = logger;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var totalChanges = 0;

        // 获取所有注册的 DbContext 类型并保存更改
        var dbContextTypes = GetAllRegisteredDbContextTypes();

        foreach (var dbContextType in dbContextTypes)
        {
            try
            {
                var dbContext = _serviceProvider.GetService(dbContextType) as DbContext;
                if (dbContext != null)
                {
                    var changes = await dbContext.SaveChangesAsync(cancellationToken);
                    totalChanges += changes;

                    if (changes > 0)
                    {
                        _logger?.LogDebug("Saved {Count} changes for DbContext {DbContextType}",
                            changes, dbContextType.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save changes for DbContext {DbContextType}",
                    dbContextType.Name);
                throw;
            }
        }

        return totalChanges;
    }

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // 如果已启用事务，说明是嵌套调用
        // 在嵌套场景中，我们不需要真正开始数据库事务，只需要标记事务已启用
        if (IsEnabledTransaction)
        {
            // 为新获取的 UnitOfWork 也启用事务
            var dbContextTypes = GetAllRegisteredDbContextTypes();
            foreach (var dbContextType in dbContextTypes)
            {
                var unitOfWork = GetUnitOfWork(dbContextType);
                if (unitOfWork != null)
                {
                    _unitOfWorks.TryAdd(dbContextType, unitOfWork);
                }
            }
            return Task.CompletedTask;
        }

        // 第一次调用，启用事务（延迟开始）
        EnableTransaction();
        return Task.CompletedTask;
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabledTransaction)
        {
            return;
        }

        // 如果事务深度 > 1，说明是嵌套调用，只减少深度，不真正提交
        if (_transactionDepth > 1)
        {
            Interlocked.Decrement(ref _transactionDepth);

            // 为所有 UnitOfWork 也执行嵌套提交
            foreach (var unitOfWork in _unitOfWorks.Values)
            {
                await unitOfWork.CommitTransactionAsync();
            }
            return;
        }

        // 策略：直接扫描所有已注册的 DbContext 服务，检查它们是否有更改
        // 不再依赖"发现"机制，而是直接检查服务容器中所有可能的 DbContext 实例
        var allDbContextTypes = GetAllRegisteredDbContextTypes();

        foreach (var dbContextType in allDbContextTypes)
        {
            var dbContext = _serviceProvider.GetService(dbContextType) as DbContext;
            if (dbContext == null)
            {
                continue;
            }

            if (dbContext.ChangeTracker.HasChanges())
            {
                // 如果有更改但 UnitOfWork 还未创建，创建它（GetUnitOfWork 会自动启用事务）
                if (!_unitOfWorks.ContainsKey(dbContextType))
                {
                    GetUnitOfWork(dbContextType);
                }
            }
        }

        // 最终提交阶段：减少事务深度到 0
        Interlocked.Decrement(ref _transactionDepth);

        // 使用 fail-fast 策略提交所有已创建的 UnitOfWork
        // 如果任何一个提交失败，立即停止并回滚所有尚未提交的 UnitOfWork
        var committedDbContexts = new List<Type>();
        var unitOfWorkEntries = _unitOfWorks.ToList();

        foreach (var kvp in unitOfWorkEntries)
        {
            try
            {
                await kvp.Value.CommitTransactionAsync(cancellationToken);
                committedDbContexts.Add(kvp.Key);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to commit transaction for DbContext {DbContextType}, rolling back remaining",
                    kvp.Key.Name);

                // 回滚所有尚未提交的 UnitOfWork（包括当前失败的）
                foreach (var remaining in unitOfWorkEntries.Where(u => !committedDbContexts.Contains(u.Key)))
                {
                    try
                    {
                        await remaining.Value.RollbackTransactionAsync(cancellationToken);
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger?.LogError(rollbackEx, "Failed to rollback transaction for DbContext {DbContextType}",
                            remaining.Key.Name);
                    }
                }

                // 清空 post-commit 队列（事务失败，丢弃所有待执行操作）
                var postCommitQueue = _serviceProvider.GetService<IPostCommitActionQueue>();
                postCommitQueue?.Clear();

                // 如果有已提交的 DbContext，记录严重警告（部分提交无法回滚，可能需要人工干预）
                if (committedDbContexts.Count > 0)
                {
                    _logger?.LogCritical(
                        "Partial commit detected! Committed: [{Committed}], Failed: {Failed}. Manual intervention may be required.",
                        string.Join(", ", committedDbContexts.Select(t => t.Name)),
                        kvp.Key.Name);
                }

                // 事务深度已在上方减少，无需再次操作

                // 直接抛出原始异常，保留完整的异常栈信息
                throw;
            }
        }

        // 全部提交成功，执行 post-commit actions（如延迟事件发布）
        var queue = _serviceProvider.GetService<IPostCommitActionQueue>();
        if (queue != null && queue.Count > 0)
        {
            try
            {
                await queue.ExecuteAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Post-commit actions 失败不影响已提交的事务，仅记录错误
                _logger?.LogError(ex, "Error executing post-commit actions after transaction commit");
            }
        }

        // 事务深度已在提交前减少
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabledTransaction)
        {
            return;
        }

        // 重置事务深度（回滚所有嵌套事务）
        Interlocked.Exchange(ref _transactionDepth, 0);

        // 清空 post-commit 队列（事务回滚，丢弃所有待执行操作）
        var postCommitQueue = _serviceProvider.GetService<IPostCommitActionQueue>();
        postCommitQueue?.Clear();

        var exceptions = new List<Exception>();

        foreach (var kvp in _unitOfWorks)
        {
            try
            {
                await kvp.Value.RollbackTransactionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to rollback transaction for DbContext {DbContextType}",
                    kvp.Key.Name);
                exceptions.Add(ex);
            }
        }

        _unitOfWorks.Clear();

        if (exceptions.Count > 0)
        {
            _logger?.LogWarning("One or more transactions failed to rollback");
            // 回滚失败不应该抛出异常，只记录警告
        }
    }

    public IUnitOfWork? GetUnitOfWork<T>()
    {
        return GetUnitOfWork(typeof(T));
    }

    public IUnitOfWork? GetUnitOfWork(Type dbContextType)
    {
        if (!typeof(DbContext).IsAssignableFrom(dbContextType))
        {
            throw new ArgumentException($"Type {dbContextType.Name} is not a DbContext", nameof(dbContextType));
        }

        // 如果已有缓存的 UnitOfWork，直接返回
        if (_unitOfWorks.TryGetValue(dbContextType, out var cachedUnitOfWork))
        {
            return cachedUnitOfWork;
        }

        // 尝试从服务容器获取 DbContext 实例
        var dbContext = _serviceProvider.GetService(dbContextType) as DbContext;
        if (dbContext == null)
        {
            _logger?.LogWarning("DbContext {DbContextType} not found in service container",
                dbContextType.Name);
            return null;
        }

        // 使用 ActivatorUtilities 自动解析依赖项（如 ILogger, IPerformanceMonitorService）并创建实例
        IUnitOfWork? unitOfWork = null;
        try
        {
            var unitOfWorkType = typeof(EFCoreUnitOfWork<>).MakeGenericType(dbContextType);
            unitOfWork = ActivatorUtilities.CreateInstance(_serviceProvider, unitOfWorkType, dbContext) as IUnitOfWork;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create UnitOfWork instance for DbContext {DbContextType} using ActivatorUtilities",
                dbContextType.Name);
            
            // 回退方案：手动尝试最基础的构造函数
            try
            {
                var unitOfWorkType = typeof(EFCoreUnitOfWork<>).MakeGenericType(dbContextType);
                unitOfWork = Activator.CreateInstance(unitOfWorkType, dbContext, null, null) as IUnitOfWork;
            }
            catch (Exception fallbackEx)
            {
                _logger?.LogCritical(fallbackEx, "Critical: Minimal fallback creation of UnitOfWork also failed for {DbContextType}", 
                    dbContextType.Name);
                return null;
            }
        }

        if (unitOfWork != null)
        {
            _unitOfWorks.TryAdd(dbContextType, unitOfWork);

            // 如果已启用事务，为新创建的 UnitOfWork 也启用事务
            if (IsEnabledTransaction)
            {
                unitOfWork.EnableTransaction();
            }
        }

        return unitOfWork;
    }

    /// <summary>
    /// 获取所有已注册的 DbContext 类型
    /// 综合多种方式：配置文件、EntityManager
    /// 使用 volatile 缓存避免重复扫描
    /// 注意：多 DbContext 场景不保证分布式事务，每个 DbContext 独立提交
    /// </summary>
    private List<Type> GetAllRegisteredDbContextTypes()
    {
        return _cachedDbContextTypes ??= DiscoverDbContextTypes();
    }

    private List<Type> DiscoverDbContextTypes()
    {
        var dbContextTypes = new HashSet<Type>();

        // 方式1：从配置文件读取 DatabaseOptions
        try
        {
            var configuration = _serviceProvider.GetService<IConfiguration>();
            if (configuration != null)
            {
                var databaseOptions = configuration.GetSection("Database").Get<DatabaseOptions>();
                if (databaseOptions?.DbContexts != null)
                {
                    foreach (var dbContextConfig in databaseOptions.DbContexts)
                    {
                        var dbContextType = dbContextConfig.GetDbContextType();
                        if (dbContextType != null && typeof(DbContext).IsAssignableFrom(dbContextType))
                        {
                            dbContextTypes.Add(dbContextType);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read DbContext types from config");
        }

        // 方式2：通过 EntityManager 获取
        try
        {
            var entityManager = _serviceProvider.GetService<IEntityManager>();
            if (entityManager != null)
            {
                entityManager.Initialize();
                var discoveredTypes = entityManager.GetAllDbContextTypes();
                foreach (var dbContextType in discoveredTypes)
                {
                    dbContextTypes.Add(dbContextType);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get DbContext types from EntityManager");
        }

        return dbContextTypes.ToList();
    }

    public void EnableTransaction()
    {
        Interlocked.Increment(ref _transactionDepth);

        // 为所有已获取的 UnitOfWork 启用事务
        foreach (var unitOfWork in _unitOfWorks.Values)
        {
            unitOfWork.EnableTransaction();
        }
    }

    public bool IsEnabledTransaction => Volatile.Read(ref _transactionDepth) > 0;

    public int TransactionDepth => Volatile.Read(ref _transactionDepth);

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
            // 清理所有管理的 UnitOfWork
            foreach (var unitOfWork in _unitOfWorks.Values)
            {
                if (unitOfWork is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Error disposing UnitOfWork");
                    }
                }
            }
            _unitOfWorks.Clear();
            Interlocked.Exchange(ref _transactionDepth, 0);
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

        // 异步清理所有管理的 UnitOfWork
        foreach (var unitOfWork in _unitOfWorks.Values)
        {
            if (unitOfWork is IAsyncDisposable asyncDisposable)
            {
                try
                {
                    await asyncDisposable.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error async disposing UnitOfWork");
                }
            }
            else if (unitOfWork is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error disposing UnitOfWork");
                }
            }
        }
        _unitOfWorks.Clear();
        Interlocked.Exchange(ref _transactionDepth, 0);
        _disposed = true;
    }

    #endregion
}