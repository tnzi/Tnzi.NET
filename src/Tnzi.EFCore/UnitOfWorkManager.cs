
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
    // 被内层回滚连带终结、但还没收到自己那次提交/回滚的外层层数（只用于诊断，见 RollbackTransactionAsync）
    private int _orphanedOuterLevels;
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

    /// <remarks>
    /// 与 <see cref="EnableTransaction"/> 等价：深度 +1，物理事务延迟到首次保存才开启。
    /// ★ 已有事务时同样要计入深度。此前嵌套调用不加深度，与它配对的 <see cref="CommitTransactionAsync"/>
    /// 看到深度 1 就走最外层分支：把外层到目前为止缓冲的写入一并物理提交、提前执行提交后队列、退出事务态，
    /// 外层随后的回滚撤不掉任何东西，后续写入也落在自动提交模式下。
    /// </remarks>
    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
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
            if (TryConsumeOrphanedOuterLevel())
            {
                // 调用方吞掉了内层失败、继续走到了外层提交：这次提交什么都提交不了，
                // 内层回滚之后的写入已经在事务之外直接落库。此前这里静默返回，看起来与成功提交无异。
                _logger?.LogWarning(
                    "CommitTransactionAsync was called for an outer unit of work whose transaction had already been rolled back by an inner unit of work: nothing is committed by this call, and writes made since that rollback were saved outside any transaction. Abort the outer operation when an inner unit of work fails, or run each item in its own DI scope.");
            }

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

                // 没提交成功的上下文，其被回滚的写入不得留在变更跟踪器里（见 ClearChangeTrackers）；
                // 已提交的那些条目已被接受、与库一致，不动
                ClearChangeTrackers(unitOfWorkEntries.Select(u => u.Key).Where(t => !committedDbContexts.Contains(t)));

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

        // 物理事务属于先于本管理器进入事务的作用域工作单元，这次「提交」只 flush 进了它的事务：
        // 提交后动作要等那个事务真正提交，由它执行；它回滚则随之作废（见 EFCoreUnitOfWork 的类型注释）
        if (unitOfWorkEntries.Any(e => e.Value is IEnclosedUnitOfWork { LastCommitDeferredToEnclosingTransaction: true }))
        {
            return;
        }

        // 全部提交成功，执行 post-commit actions（如延迟事件发布）。
        // ★ 不传调用方的令牌：它通常是请求的 RequestAborted。事务已经提交，客户端此刻断开（代理超时、移动端切后台）
        // 不该让缓存失效、通知这些副作用在事件总线的并发闸门处被取消：那条取消发生在重试与死信之外，
        // 结果是库已改而副作用一个都没发生，只留一条错误日志。
        var queue = _serviceProvider.GetService<IPostCommitActionQueue>();
        if (queue != null && queue.Count > 0)
        {
            var queuedCount = queue.Count;
            try
            {
                await queue.ExecuteAsync(CancellationToken.None);
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

    /// <remarks>
    /// 任何深度的回滚都撤销整个环境事务（没有保存点）：嵌套层失败时，外层到目前为止的写入一并撤销，
    /// 外层随后的提交/回滚找不到事务、直接返回。调用方因此必须在内层失败后中止外层操作
    /// （见 docs/coding-standards/service.md 的失败语义）。违反这条规则的「吞掉内层异常、继续写入」
    /// 不会被拦下，但外层那次什么都提交不了的提交会记一条警告，而不是静默。
    /// </remarks>
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabledTransaction)
        {
            // 外层在内层回滚之后中止：这正是规定的处理方式，只把记账消化掉
            TryConsumeOrphanedOuterLevel();
            return;
        }

        // 参与本事务的上下文要在清空 _unitOfWorks 之前认出来，回滚之后清它们的变更跟踪器（见 ClearChangeTrackers）
        var participants = CollectParticipatingContextTypes();

        // 重置事务深度（回滚所有嵌套事务）
        var depth = Interlocked.Exchange(ref _transactionDepth, 0);
        if (depth > 1)
        {
            // 记 Information 而不是 Warning：「内层失败、外层随即中止」是正常的失败路径（业务校验经异常中止就走这里）。
            // 真正的误用是外层吞掉失败后还去提交，那一处记 Warning（见 CommitTransactionAsync）
            Interlocked.Add(ref _orphanedOuterLevels, depth - 1);
            _logger?.LogInformation(
                "An inner unit of work rolled back at transaction depth {Depth}: the whole ambient transaction was rolled back, including the writes of the {OuterLevels} enclosing unit(s) of work, which no longer have a transaction to commit. The enclosing operation must abort; writes it makes from here on are saved outside any transaction.",
                depth, depth - 1);
        }

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
        ClearChangeTrackers(participants);

        if (exceptions.Count > 0)
        {
            _logger?.LogWarning("One or more transactions failed to rollback");
            // 回滚失败不应该抛出异常，只记录警告
        }
    }

    /// <summary>
    /// 参与当前事务的上下文类型：建过工作单元的（flush 过、或提交时创建的），以及仍有挂起变更的
    /// （事务里只做了缓冲写入、还没来得及建工作单元）。两者都没有的上下文没被这个事务碰过，不动它。
    /// </summary>
    private List<Type> CollectParticipatingContextTypes()
    {
        var candidates = new HashSet<Type>(_unitOfWorks.Keys);
        try
        {
            candidates.UnionWith(GetAllRegisteredDbContextTypes());
        }
        catch (Exception ex)
        {
            // 回滚发生在 catch 里，这里再抛会盖掉调用方的原始异常；只清建过工作单元的那些上下文
            _logger?.LogError(ex, "Could not enumerate DbContext types while rolling back; only contexts with a unit of work will have their change trackers cleared");
        }

        var participants = new List<Type>();
        foreach (var type in candidates)
        {
            if (_unitOfWorks.ContainsKey(type)
                || (_serviceProvider.GetService(type) is DbContext context && context.ChangeTracker.HasChanges()))
            {
                participants.Add(type);
            }
        }

        return participants;
    }

    /// <summary>
    /// 回滚之后清空参与上下文的变更跟踪器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 回滚只撤销数据库里的事务。EF 在 <c>SaveChanges</c> 失败时不接受变更（条目保持 <c>Added</c> / <c>Modified</c>），
    /// flush 之后被回滚的条目停在 <c>Unchanged</c>。不清的话，同一作用域里的下一次保存会把被回滚的写入重放
    /// （同一个异常再抛一遍，「每项一个工作单元、失败就继续」的循环此后每一项都失败），或者把提交之前就抛出的那一项的
    /// 半成品写入真正落库；按主键 <c>GetAsync</c> 还会从跟踪器里取到一个库里不存在的实例。
    /// </para>
    /// <para>
    /// 清空同时会让调用方此前读出、仍持有的实体变成脱离跟踪：回滚之后的实例一律视为失效，需要的话重新读取。
    /// 这是 EF 对「失败之后继续使用同一个上下文」的建议做法；逐条精确回退要给整个事务做快照，代价高且容易漏。
    /// </para>
    /// </remarks>
    private void ClearChangeTrackers(IEnumerable<Type> contextTypes)
    {
        foreach (var type in contextTypes)
        {
            if (_serviceProvider.GetService(type) is DbContext context)
            {
                context.ChangeTracker.Clear();
            }
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

    private bool TryConsumeOrphanedOuterLevel()
    {
        while (true)
        {
            var current = Volatile.Read(ref _orphanedOuterLevels);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _orphanedOuterLevels, current - 1, current) == current)
            {
                return true;
            }
        }
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