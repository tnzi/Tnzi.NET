
namespace Tnzi.EFCore;

/// <summary>
/// 工作单元管理器实现
/// 用于管理多个 DbContext 的工作单元，支持统一的事务协调
/// 同时实现 IAmbientUnitOfWorkScope：事务启用期间把自身发布到 AmbientUnitOfWork(AsyncLocal),
/// 供事件总线等基础设施感知活跃事务并把副作用延迟到提交后
/// </summary>
public class UnitOfWorkManager : IUnitOfWorkManager, IAmbientUnitOfWorkScope, IDisposable, IAsyncDisposable
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
        var transactionEnabled = IsEnabledTransaction;

        foreach (var dbContextType in dbContextTypes)
        {
            try
            {
                if (_serviceProvider.GetService(dbContextType) is not DbContext dbContext)
                {
                    continue;
                }

                int changes;
                if (transactionEnabled && dbContext.ChangeTracker.HasChanges())
                {
                    // Route through the UnitOfWork so the deferred physical transaction is
                    // started (via the UoW's EnsureTransactionStartedAsync) BEFORE saving.
                    // A bare dbContext.SaveChangesAsync as the first save inside an enabled-
                    // but-not-yet-started transaction runs in autocommit mode, escaping the
                    // transaction (a later rollback can't undo it), the same hazard already
                    // fixed for EfCoreRepository.SaveChangesAsync (2026-07-02). This is the
                    // transaction-safe path behind ApplicationService.FlushAsync.
                    var unitOfWork = GetUnitOfWork(dbContextType);
                    changes = unitOfWork != null
                        ? await unitOfWork.SaveChangesAsync(cancellationToken)
                        : await dbContext.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    // No active transaction (autocommit is correct), or nothing to save.
                    changes = await dbContext.SaveChangesAsync(cancellationToken);
                }

                totalChanges += changes;

                if (changes > 0)
                {
                    _logger?.LogDebug("Saved {Count} changes for DbContext {DbContextType}",
                        changes, dbContextType.Name);
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

    /// <remarks>
    /// ⚠️ 多 DbContext 注意：当单个工作单元涉及 2 个及以上 DbContext 时，各上下文按顺序逐个提交，
    /// 这<b>不是</b>跨上下文的分布式/原子事务。若前面的上下文已提交、后面的上下文提交失败，
    /// 已提交的部分<b>无法</b>回滚——此时框架会回滚尚未提交的上下文、清空 post-commit 队列，
    /// 并记录 <c>LogCritical("Partial commit detected ...")</c> 后重新抛出，可能需要人工干预或补偿。
    /// 跨上下文的强一致性应通过单一 DbContext、Outbox 模式（<c>EfCoreEventStore</c>）或幂等/补偿设计实现。
    /// </remarks>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabledTransaction)
        {
            return;
        }

        // 如果事务深度 > 1，说明是嵌套调用：flush 待保存的变更（审计字段/ID 在此填充，
        // 数据在物理事务内对后续读取可见），但不提交物理事务——提交/回滚由最外层决定。
        // 若不 flush，内层 ExecuteInUnitOfWorkAsync "提交" 后的代码（DTO 映射、事务内查询）
        // 会观察到未持久化的实体（审计字段为 default、数据不可查）
        if (_transactionDepth > 1)
        {
            // 为有变更的 DbContext 确保 UnitOfWork 存在（GetUnitOfWork 会同步事务深度），
            // 必须在 Decrement 之前创建，使新 UnitOfWork 继承当前嵌套深度
            var changedTypes = new HashSet<Type>();
            foreach (var dbContextType in GetAllRegisteredDbContextTypes())
            {
                if (_serviceProvider.GetService(dbContextType) is not DbContext dbContext)
                {
                    continue;
                }

                if (dbContext.ChangeTracker.HasChanges())
                {
                    changedTypes.Add(dbContextType);
                    if (!_unitOfWorks.ContainsKey(dbContextType))
                    {
                        GetUnitOfWork(dbContextType);
                    }
                }
            }

            Interlocked.Decrement(ref _transactionDepth);

            foreach (var kvp in _unitOfWorks)
            {
                if (changedTypes.Contains(kvp.Key))
                {
                    // flush 进物理事务（延迟开启），物理事务保持打开
                    await kvp.Value.SaveChangesAsync(cancellationToken);
                }

                // 嵌套提交：仅递减 UnitOfWork 的事务深度
                await kvp.Value.CommitTransactionAsync(cancellationToken);
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

        // ★ 事务深度与环境事务作用域都**不能**在提交循环之前清掉。最终提交里的
        // SaveChangesAsync 往往就是本事务的第一次保存（事务内只做仓储写入、中途不 flush
        // 是最常见的形状），提前离开事务态会让那次保存跑在自动提交模式下，
        // 也会让它收集到的领域事件被判定为"不在事务中"而就地发布 ——
        // 提交随后失败时，那些事件已经发出去了（幽灵事件），且主实体已经落库无从回滚。
        // 两者一律推迟到提交循环之后。

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

                // 事务已终结（部分提交的情况下也无法再继续），离开事务态
                Interlocked.Exchange(ref _transactionDepth, 0);
                AmbientUnitOfWork.Set(null);

                // 直接抛出原始异常，保留完整的异常栈信息
                throw;
            }
        }

        // 提交全部成功：先离开事务态，再执行 post-commit 队列。
        // 顺序是必须的 —— 队列里的事件发布若仍被判定为"事务中"会重新入队，形成自引用死循环。
        // AsyncLocal 边界说明：本方法是 async,此处 Set(null) 只影响本方法内部执行流
        // (post-commit 队列恰在其中);调用者流会残留一个 IsTransactionActive=false 的引用,
        // 消费方(如 LocalEventBus)判定的是 IsTransactionActive 而非 null,语义等价于已清除
        Interlocked.Decrement(ref _transactionDepth);
        AmbientUnitOfWork.Set(null);

        // 全部提交成功，执行 post-commit actions（如延迟事件发布）
        var queue = _serviceProvider.GetService<IPostCommitActionQueue>();
        if (queue != null && queue.Count > 0)
        {
            var queuedCount = queue.Count;
            try
            {
                await queue.ExecuteAsync(cancellationToken);
            }
            catch (AggregateException ex)
            {
                // Post-commit actions 失败不影响已提交的事务，仅记录错误。
                // 队列逐个隔离失败并跑完全部动作，故这里的计数能区分「个别失败」与「全体失败」
                _logger?.LogError(ex,
                    "{FailedCount} of {TotalCount} post-commit actions failed after transaction commit",
                    ex.InnerExceptions.Count, queuedCount);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Error executing {TotalCount} post-commit actions after transaction commit", queuedCount);
            }
        }

        // 事务深度与环境事务作用域已在 post-commit 队列执行前清理
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabledTransaction)
        {
            return;
        }

        // 重置事务深度（回滚所有嵌套事务）
        Interlocked.Exchange(ref _transactionDepth, 0);

        // 清除环境事务作用域(事务已结束)
        AmbientUnitOfWork.Set(null);

        // 清空 post-commit 队列（事务回滚，丢弃所有待执行操作,包括事务感知发布延迟的事件）
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

            // 与管理器的事务深度同步：新创建的 UnitOfWork 必须继承当前嵌套深度。
            // 若只启用一层（深度 1），嵌套提交会把它当成最外层而提前提交物理事务，
            // 破坏外层的回滚能力，并因 _hasCommitted 标记导致后续变更被静默丢弃
            var depth = TransactionDepth;
            for (var i = 0; i < depth; i++)
            {
                unitOfWork.EnableTransaction();
            }
        }

        return unitOfWork;
    }

    /// <summary>
    /// 获取所有已注册的 DbContext 类型
    /// 综合多种方式：AddTnziDbContext 的登记、配置文件、EntityManager
    /// 使用 volatile 缓存避免重复扫描
    /// 注意：多 DbContext 场景不保证分布式事务，每个 DbContext 独立提交
    /// </summary>
    private List<Type> GetAllRegisteredDbContextTypes()
    {
        return _cachedDbContextTypes ??= DiscoverDbContextTypes();
    }

    /// <summary>
    /// 发现上下文类型。★ 这份列表决定提交循环里有没有东西可提交：取不到主上下文时，
    /// 事务内缓冲的写入一个 UoW 都不建、零次 SaveChanges，随作用域释放静默消失而接口 200。
    /// 三条途径（AddTnziDbContext 的登记 / 配置 / EntityManager）收口在 <see cref="DbContextTypeDiscovery"/>，
    /// 与 <c>EfCoreDbMigrator</c> 共用 —— 此前迁移器自己另抄了一份只含 EntityManager 那条，补登记时被漏掉。
    /// </summary>
    private List<Type> DiscoverDbContextTypes()
    {
        var dbContextTypes = DbContextTypeDiscovery.Discover(_serviceProvider, _logger);

        if (dbContextTypes.Count == 0)
        {
            // 失败方向：说出来。一个都发现不了时，每次提交都是空转 —— 事务内的写入不会报错，只会消失。
            _logger?.LogWarning(
                "No DbContext types are known to UnitOfWorkManager: nothing registered through AddTnziDbContext, " +
                "no resolvable DbContextType in 'Database:DbContexts', and no entity registers bound to a DbContext. " +
                "Changes made inside a unit of work will not be saved. Register the DbContext with AddTnziDbContext " +
                "(or configure it under 'Database:DbContexts') so the unit of work can commit it.");
        }

        return dbContextTypes;
    }

    public void EnableTransaction()
    {
        var depth = Interlocked.Increment(ref _transactionDepth);

        // 首层进入事务时,把自身发布为环境事务作用域(AsyncLocal 随执行流传播),
        // 使事件总线等基础设施能检测到活跃事务并延迟副作用到提交后
        if (depth == 1)
        {
            AmbientUnitOfWork.Set(this);
        }

        // 为所有已获取的 UnitOfWork 启用事务
        foreach (var unitOfWork in _unitOfWorks.Values)
        {
            unitOfWork.EnableTransaction();
        }
    }

    public bool IsEnabledTransaction => Volatile.Read(ref _transactionDepth) > 0;

    public int TransactionDepth => Volatile.Read(ref _transactionDepth);

    #region IAmbientUnitOfWorkScope

    /// <inheritdoc />
    bool IAmbientUnitOfWorkScope.IsTransactionActive => IsEnabledTransaction;

    /// <inheritdoc />
    void IAmbientUnitOfWorkScope.EnqueuePostCommit(Func<CancellationToken, Task> action)
    {
        Check.NotNull(action);

        var queue = _serviceProvider.GetService<IPostCommitActionQueue>();
        if (queue == null)
        {
            // 队列不可用(极端配置)时不能静默丢弃,记录后立即触发,保持事件不丢失
            _logger?.LogWarning("IPostCommitActionQueue is not registered; post-commit action executes immediately instead of after commit");
            _ = Task.Run(() => action(CancellationToken.None));
            return;
        }

        queue.Enqueue(action);
    }

    #endregion

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
            // 防御性清除环境事务作用域,避免自身引用在异常路径下泄漏到后续执行流
            if (ReferenceEquals(AmbientUnitOfWork.Current, this))
            {
                AmbientUnitOfWork.Set(null);
            }
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
        // 防御性清除环境事务作用域,避免自身引用在异常路径下泄漏到后续执行流
        if (ReferenceEquals(AmbientUnitOfWork.Current, this))
        {
            AmbientUnitOfWork.Set(null);
        }
        _disposed = true;
    }

    #endregion
}