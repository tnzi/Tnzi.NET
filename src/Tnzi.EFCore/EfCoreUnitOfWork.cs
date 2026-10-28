
namespace Tnzi.EFCore;

/// <summary>
/// EF Core 工作单元实现，支持嵌套事务和延迟事务开始
/// </summary>
/// <remarks>
/// <para>
/// ★ 同一个 DI 作用域里、同一个上下文上可能有<b>两个</b>工作单元实例：直接注入的作用域 <see cref="IUnitOfWork"/>，
/// 与 <see cref="IUnitOfWorkManager"/> 为这个上下文另建的那个。一个上下文同时只能有一个物理事务，
/// 所以两者都启用事务时必须只有一个开 BEGIN、另一个加入它，规则是「先进入事务的那个持有物理事务」：
/// </para>
/// <list type="bullet">
/// <item>管理器已经开着事务时，作用域实例再启用事务：这一层记成管理器的一个<b>嵌套层</b>（提交只 flush、
/// 回滚撤销整个环境事务，见 <c>docs/coding-standards/service.md</c>），物理事务、提交后队列都归管理器。</item>
/// <item>作用域实例先进入了自己的事务、管理器后开事务：管理器的工作单元不再 BEGIN，而是请作用域实例开启物理事务并加入它；
/// 管理器最外层的「提交」只 flush，提交后动作移交给作用域实例在真正提交之后执行。</item>
/// </list>
/// <para>此前两种顺序的第二次 BEGIN 都会让 EF 抛「The connection is already in a transaction」。</para>
/// </remarks>
public class EFCoreUnitOfWork<TDbContext> : IUnitOfWork, IAsyncDisposable, IEnclosedUnitOfWork
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly ILogger<EFCoreUnitOfWork<TDbContext>>? _logger;
    private readonly IPerformanceMonitorService? _monitor;
    private readonly IServiceProvider? _serviceProvider;
    private IDbContextTransaction? _transaction;
    private int _transactionDepth;
    private readonly SemaphoreSlim _transactionSemaphore = new(1, 1);
    private bool _hasCommitted;

    // 本实例在管理器已开着事务时启用的层数：这些层是管理器的嵌套层，提交 / 回滚转交管理器
    private int _forwardedLevels;
    private IUnitOfWorkManager? _forwardedManager;

    // 本实例（管理器的工作单元）加入的物理事务的持有者：先于管理器进入事务的作用域实例
    private EFCoreUnitOfWork<TDbContext>? _enclosingOwner;
    private bool _lastCommitDeferredToEnclosingTransaction;

    // 本实例持有的物理事务里，有一次管理器的提交被推迟到这里：真正提交后执行提交后队列，回滚则清掉它
    private bool _runPostCommitActionsAfterCommit;

    public EFCoreUnitOfWork(
        TDbContext dbContext,
        ILogger<EFCoreUnitOfWork<TDbContext>>? logger = null,
        IServiceProvider? serviceProvider = null)
    {
        _dbContext = Check.NotNull(dbContext);
        _logger = logger;
        _serviceProvider = serviceProvider;
        _monitor = serviceProvider?.GetService<IPerformanceMonitorService>();
    }

    /// <summary>
    /// 获取是否已启用事务（存在活跃事务或事务深度 > 0）
    /// </summary>
    public bool IsEnabledTransaction => HasOwnTransactionState || Volatile.Read(ref _forwardedLevels) > 0;

    /// <summary>
    /// 获取事务嵌套深度（含转交给管理器的那些层）
    /// </summary>
    public int TransactionDepth => Volatile.Read(ref _transactionDepth) + Volatile.Read(ref _forwardedLevels);

    /// <inheritdoc />
    bool IEnclosedUnitOfWork.LastCommitDeferredToEnclosingTransaction => _lastCommitDeferredToEnclosingTransaction;

    /// <summary>本实例自己开的层或自己持有的物理事务（不含转交给管理器的层）。</summary>
    private bool HasOwnTransactionState => _transaction != null || Volatile.Read(ref _transactionDepth) > 0;

    /// <summary>已经在一个物理事务里：自己持有的，或加入的外层作用域实例的那个（仍未结束）。</summary>
    private bool HasPhysicalTransaction => _transaction != null || _enclosingOwner?._transaction != null;

    /// <summary>
    /// 启用事务（标记需要事务，但不立即开始，支持嵌套）
    /// 这是延迟事务开始的关键：调用此方法后，事务会在第一次 SaveChanges 时才真正开始
    /// </summary>
    /// <remarks>
    /// 同作用域的管理器已经为同一个上下文开着事务、而本实例还没有自己的事务时，这一层是管理器的嵌套层：
    /// 交给管理器计深度，提交 / 回滚也交给它（见类型注释）。
    /// </remarks>
    public void EnableTransaction()
    {
        if (!HasOwnTransactionState && FindAmbientManager() is { } manager)
        {
            manager.EnableTransaction();
            _forwardedManager = manager;
            Interlocked.Increment(ref _forwardedLevels);
            return;
        }

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
            _lastCommitDeferredToEnclosingTransaction = false;
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 本实例没开自己的事务、而同作用域的管理器正为同一个上下文开着事务：交给管理器的那个工作单元保存，
        // 否则这次保存会在延迟事务 BEGIN 之前以自动提交落库（见 FindAmbientManagedUnitOfWork）
        if (FindAmbientManagedUnitOfWork() is { } managed)
        {
            return await managed.SaveChangesAsync(cancellationToken);
        }

        // 如果已启用事务但未开始，则延迟开始事务。只看本实例自己开的层：转交给管理器的层在上面已经交给了管理器，
        // 管理器的事务若已被别人回滚，这些层之后的写入按管理器的语义落在事务之外，而不是在这里另开一个没人收尾的事务
        if (Volatile.Read(ref _transactionDepth) > 0 && !HasPhysicalTransaction)
        {
            await BeginPhysicalTransactionAsync(cancellationToken);
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
        // 与 SaveChangesAsync 同理：管理器的事务要由管理器的工作单元开启，裸 SQL 才加入得了它
        if (FindAmbientManagedUnitOfWork() is { } managed)
        {
            await managed.EnsureTransactionStartedAsync(cancellationToken);
            return;
        }

        if (Volatile.Read(ref _transactionDepth) == 0 || HasPhysicalTransaction)
        {
            return;
        }

        await BeginPhysicalTransactionAsync(cancellationToken);
    }

    /// <remarks>
    /// 与 <see cref="EnableTransaction"/> 等价：深度 +1，物理事务延迟到首次保存（或 <see cref="EnsureTransactionStartedAsync"/>）才开启。
    /// 已有事务时同样只加一层，与它配对的 <see cref="CommitTransactionAsync"/> 只在最外层真正提交。
    /// ★ 此前它只开物理事务、从不计深度：仓储看到活动事务而缓冲写入，配对的提交却因深度为 0 抛异常并回滚，
    /// 「Begin → 写入 → Commit」一次都写不进库；嵌套的第二次调用抛「Transaction already started」。
    /// </remarks>
    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        EnableTransaction();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 开启物理数据库事务（幂等），不改变事务深度。
    /// </summary>
    /// <remarks>
    /// 作用域实例已经先于管理器进入了自己的事务时，本实例（管理器的工作单元）不另开 BEGIN：
    /// 请那个实例开启它持有的物理事务，本实例加入（同一个上下文同时只能有一个事务，第二次 BEGIN 必抛）。
    /// </remarks>
    private async Task BeginPhysicalTransactionAsync(CancellationToken cancellationToken)
    {
        // 使用 SemaphoreSlim 保护事务初始化，防止并发调用导致重复创建事务
        await _transactionSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 双重检查：并发调用时后进入者直接返回，不重复 BEGIN
            if (HasPhysicalTransaction)
            {
                return;
            }

            // 加入过的外层事务已经结束（外层先于本实例收尾）：不再算加入，按自己的状态处理
            _enclosingOwner = null;

            if (FindEnclosingOwner() is { } owner)
            {
                await owner.BeginOwnPhysicalTransactionAsync(cancellationToken);
                _enclosingOwner = owner;
                return;
            }

            _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            _hasCommitted = false;
        }
        finally
        {
            _transactionSemaphore.Release();
        }
    }

    /// <summary>
    /// 开启本实例持有的物理事务（幂等）。供加入它的管理器工作单元调用。
    /// </summary>
    private async Task BeginOwnPhysicalTransactionAsync(CancellationToken cancellationToken)
    {
        await _transactionSemaphore.WaitAsync(cancellationToken);
        try
        {
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

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        // 这一层是在管理器的事务里启用的：它是管理器的嵌套层，按管理器的嵌套语义收尾（flush、不物理提交）
        if (Volatile.Read(ref _transactionDepth) == 0 && TryTakeForwardedLevel() is { } manager)
        {
            try
            {
                await manager.CommitTransactionAsync(cancellationToken);
            }
            catch
            {
                // ★ 这一层已经取走，调用方随后的 RollbackTransactionAsync 不会再转交到管理器。
                // 与本类自己的提交同一语义（提交失败即回滚，见下方最外层分支）：flush 失败就撤销整个环境事务，
                // 而不是让外层带着这批失败的写入继续往下走、在它自己的提交里再撞一次
                await manager.RollbackTransactionAsync(cancellationToken);
                throw;
            }

            return;
        }

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

        // ★ 事务深度**不能**在这里递减。最终提交里的 SaveChangesAsync 往往就是这个事务的
        // 第一次保存 —— 事务内只做仓储写入、中途不 flush 是最常见的形状（仓储检测到事务后
        // 一律缓冲）。深度提前归零会让 IsEnabledTransaction 两侧皆假，那次保存于是不开物理
        // 事务而跑在自动提交模式下：主实体已经落库，随后同一次 SaveChanges 里的文件引用
        // 处理若失败，回滚时 _transaction 为 null 无事可回滚，
        // 「实体与文件引用要么全成功、要么全失败」的承诺当场落空。
        // 递减推迟到提交成功之后；失败路径由 RollbackTransactionAsync 把深度清零。
        var committed = false;
        _lastCommitDeferredToEnclosingTransaction = false;
        try
        {
            // 确保所有待处理的更改都已保存
            // 如果已启用事务但未实际开始，SaveChangesAsync 会触发事务开启（延迟开启机制）
            if (_dbContext.ChangeTracker.HasChanges())
            {
                await SaveChangesAsync(cancellationToken);
            }

            // 加入的是外层作用域实例的物理事务：本层只 flush，提交由那个实例在它的最外层做，
            // 管理器据此把提交后动作移交给它（见 IEnclosedUnitOfWork）
            if (_enclosingOwner is { } owner && owner._transaction != null)
            {
                owner._runPostCommitActionsAfterCommit = true;
                _lastCommitDeferredToEnclosingTransaction = true;
            }
            // 如果有活跃事务，则提交它
            // 注意：如果没有更改且没开启过物理事务，_transaction 可能为 null，此时直接结束
            else if (_transaction != null)
            {
                await _transaction.CommitAsync(cancellationToken);
                _hasCommitted = true;
                committed = true;
            }

            // 提交完成，退出最外层事务
            Interlocked.Decrement(ref _transactionDepth);
            _enclosingOwner = null;
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

        if (committed)
        {
            await RunDeferredPostCommitActionsAsync();
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        // 管理器的嵌套层回滚：按已记载的嵌套语义撤销整个环境事务（管理器负责记下被连带终结的外层）
        if (Volatile.Read(ref _transactionDepth) == 0 && _transaction == null && TryTakeForwardedLevel() is { } manager)
        {
            await manager.RollbackTransactionAsync(cancellationToken);
            return;
        }

        if (_hasCommitted)
        {
            return;
        }

        // 重置事务深度（回滚所有嵌套事务）
        var wasActive = Interlocked.Exchange(ref _transactionDepth, 0) > 0 || _transaction != null || _enclosingOwner != null;

        // 加入的是外层作用域实例的物理事务：内层回滚撤销整个事务（同管理器的嵌套语义），由持有者回滚它
        if (_enclosingOwner is { } owner)
        {
            _enclosingOwner = null;
            await owner.RollbackTransactionAsync(cancellationToken);
            ClearChangeTrackerIf(wasActive);
            return;
        }

        DropDeferredPostCommitActions();

        if (_transaction == null)
        {
            // 物理事务还没开（写入都还缓冲在跟踪器里）：回滚就是丢掉这些缓冲
            ClearChangeTrackerIf(wasActive);
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
            ClearChangeTrackerIf(wasActive);
        }
    }

    /// <summary>
    /// 回滚了一个活跃事务之后清空上下文的变更跟踪器：被回滚的写入（失败保存留下的 Added / Modified、
    /// flush 后停在 Unchanged 的条目）不得被同作用域下一次保存重放或持久化。缘由同
    /// <c>UnitOfWorkManager</c> 的回滚；回滚之后调用方持有的实体实例一律视为失效。
    /// </summary>
    private void ClearChangeTrackerIf(bool transactionWasActive)
    {
        if (transactionWasActive)
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>取走一层转交给管理器的层；没有时返回 null。</summary>
    private IUnitOfWorkManager? TryTakeForwardedLevel()
    {
        while (true)
        {
            var current = Volatile.Read(ref _forwardedLevels);
            if (current <= 0)
            {
                return null;
            }

            if (Interlocked.CompareExchange(ref _forwardedLevels, current - 1, current) == current)
            {
                var manager = _forwardedManager;
                if (current == 1)
                {
                    _forwardedManager = null;
                }

                return manager;
            }
        }
    }

    /// <summary>
    /// 本实例持有的物理事务刚刚提交：执行管理器推迟到这里的提交后动作（它的事务是加入本实例的那个物理事务）。
    /// </summary>
    /// <remarks>
    /// 不传调用方的令牌，理由同 <c>UnitOfWorkManager</c>：事务已经提交，客户端此刻断开不该让副作用被取消。
    /// 失败只记日志，提交本身已经成功。
    /// </remarks>
    private async Task RunDeferredPostCommitActionsAsync()
    {
        if (!_runPostCommitActionsAfterCommit)
        {
            return;
        }

        _runPostCommitActionsAfterCommit = false;
        var queue = _serviceProvider?.GetService<IPostCommitActionQueue>();
        if (queue == null || queue.Count == 0)
        {
            return;
        }

        var queuedCount = queue.Count;
        try
        {
            await queue.ExecuteAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error executing {TotalCount} post-commit actions after transaction commit", queuedCount);
        }
    }

    /// <summary>本实例持有的物理事务被回滚：管理器推迟到这里的提交后动作随之作废。</summary>
    private void DropDeferredPostCommitActions()
    {
        if (!_runPostCommitActionsAfterCommit)
        {
            return;
        }

        _runPostCommitActionsAfterCommit = false;
        _serviceProvider?.GetService<IPostCommitActionQueue>()?.Clear();
    }

    /// <summary>
    /// 同一 DI 作用域里的工作单元管理器正为本实例包着的上下文开着事务、且本实例不是它为这个上下文建的那个时，返回管理器。
    /// </summary>
    private IUnitOfWorkManager? FindAmbientManager()
    {
        if (_serviceProvider == null)
        {
            return null;
        }

        var manager = _serviceProvider.GetService<IUnitOfWorkManager>();
        if (manager is not { IsEnabledTransaction: true })
        {
            return null;
        }

        if (!ReferenceEquals(_serviceProvider.GetService<TDbContext>(), _dbContext))
        {
            return null;
        }

        return ReferenceEquals(manager.GetUnitOfWork(typeof(TDbContext)), this) ? null : manager;
    }

    /// <summary>
    /// 本实例没有自己的事务、而同一 DI 作用域里的工作单元管理器正为同一个上下文开着事务时，
    /// 返回管理器持有的那个工作单元；其余情况返回 null（按本实例自己的状态处理）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ DI 作用域里直接注入的 <see cref="IUnitOfWork"/> 与管理器为同一个上下文建的工作单元是<b>两个实例</b>。
    /// 管理器启用事务（全局工作单元过滤器 / <c>ExecuteInUnitOfWorkAsync</c>）时只启用它自己的那些，
    /// 作用域实例深度为 0。物理事务又是延迟到首次经工作单元保存才 BEGIN 的，所以作用域实例此时直接
    /// <c>DbContext.SaveChangesAsync</c> 会让 EF 以隐式事务<b>立即提交</b>上下文里全部挂起变更，
    /// 连同调用方缓冲在事务里的写入；之后调用方回滚，管理器没有物理事务可回滚，
    /// 而这次保存派发的领域事件被排进提交后队列、随回滚清掉：数据落了库，事件却丢了。
    /// 本实例在管理器事务里启用的层（转交给管理器的层）同样走这里：那些层没有自己的物理事务。
    /// </para>
    /// <para>
    /// 只在本实例包着的就是作用域里的那个上下文时转交（手工构造、包着别的上下文实例的不转交）；
    /// 取到的若就是本实例自己（管理器复用了它）也不转交，避免递归。
    /// 管理器按需解析而不在构造时注入：管理器自己会经同一个服务提供程序创建工作单元。
    /// </para>
    /// </remarks>
    private IUnitOfWork? FindAmbientManagedUnitOfWork()
    {
        if (HasOwnTransactionState || FindAmbientManager() is not { } manager)
        {
            return null;
        }

        var managed = manager.GetUnitOfWork(typeof(TDbContext));
        return ReferenceEquals(managed, this) ? null : managed;
    }

    /// <summary>
    /// 本实例是管理器为这个上下文建的工作单元、而作用域里直接注入的那个实例已经先进入了自己的事务时，返回它：
    /// 物理事务归它，本实例加入。
    /// </summary>
    private EFCoreUnitOfWork<TDbContext>? FindEnclosingOwner()
    {
        if (_serviceProvider?.GetService<IUnitOfWork>() is not EFCoreUnitOfWork<TDbContext> scoped
            || ReferenceEquals(scoped, this)
            || !ReferenceEquals(scoped._dbContext, _dbContext))
        {
            return null;
        }

        return scoped.HasOwnTransactionState ? scoped : null;
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