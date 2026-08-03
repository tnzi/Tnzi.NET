
namespace Tnzi.EventBus;

public class LocalEventBus : ILocalEventBus, IDisposable, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocalEventBus> _logger;
    private readonly EventBusOptions _options;
    private readonly IEventDeadLetterQueue? _deadLetterQueue;
    private readonly ConcurrentDictionary<Type, HashSet<Type>> _runtimeHandlers = new();
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly int _maxConcurrency;
    private readonly ResiliencePipeline? _retryPipeline;
    // 在飞后台处理器任务跟踪：关闭时排水,避免应用关闭丢失 fire-and-forget 事件处理
    private readonly ConcurrentDictionary<Task, byte> _backgroundTasks = new();
    private volatile bool _disposed;

    public LocalEventBus(
        IServiceProvider serviceProvider,
        ILogger<LocalEventBus> logger,
        EventBusOptions? options = null,
        IEventDeadLetterQueue? deadLetterQueue = null,
        int maxConcurrency = 10)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
        _options = options ?? new EventBusOptions();
        _deadLetterQueue = deadLetterQueue;
        _maxConcurrency = maxConcurrency > 0 ? maxConcurrency : 10;
        _concurrencySemaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);

        // 消费 ResilienceModule 注册的 "eventbus" 弹性管线(统一重试策略来源);
        // 未注册(Resilience 关闭)时回退到内置的手动重试逻辑
        if (_options.EnableRetry)
        {
            var pipelineProvider = serviceProvider.GetService<ResiliencePipelineProvider<string>>();
            if (pipelineProvider != null && pipelineProvider.TryGetPipeline(ResiliencePipelineNames.EventBus, out var pipeline))
            {
                _retryPipeline = pipeline;
            }
        }

        // 处理器现在直接从DI容器动态获取，支持运行时注册
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent
    {
        ThrowIfDisposed();
        Check.NotNull(@event);

        // 事务感知发布：处于活跃工作单元事务中时,延迟到提交后执行(回滚则丢弃),
        // 使直接注入 IEventBus 的发布也默认具备事务安全性(不产生幽灵事件)
        if (_options.TransactionAwarePublish)
        {
            var ambient = AmbientUnitOfWork.Current;
            if (ambient?.IsTransactionActive == true)
            {
                _logger.LogDebug("Event {EventType} (EventId: {EventId}) published inside an active unit-of-work transaction; deferred until after commit",
                    typeof(TEvent).Name, @event.EventId);
                ambient.EnqueuePostCommit(ct => PublishCoreAsync(@event, ct));
                return;
            }
        }

        await PublishCoreAsync(@event, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 实际发布逻辑(不做事务感知检测,由 PublishAsync 或提交后队列调用)
    /// </summary>
    private async Task PublishCoreAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);

        // 在整个处理过程中保持scope活动，确保Scoped生命周期的处理器有效
        using var scope = _serviceProvider.CreateScope();

        // 自动捕获当前租户上下文到事件（仅当事件未显式设置 TenantId 时）
        // 从当前 scope 解析 ICurrentTenant，避免 Singleton 持有 Scoped 引用
        if (@event is EventBase eventBase && eventBase.TenantId == null)
        {
            var currentTenant = scope.ServiceProvider.GetService<ICurrentTenant>();
            eventBase.TenantId = currentTenant?.Id;
        }

        // 获取所有处理器（包括直接匹配和基类匹配的，以及运行时注册的）
        var handlers = GetEventHandlers<TEvent>(eventType, scope.ServiceProvider).ToList();

        if (handlers.Count == 0)
        {
            _logger.LogDebug("No handlers found for event {EventType}", eventType.Name);
            return;
        }

        _logger.LogDebug("Found {Count} handler(s) for event {EventType}", handlers.Count, eventType.Name);

        // 将处理器分为同步组和后台组
        var syncTasks = new List<Task>();
        var backgroundHandlerTypes = new List<Type>();

        foreach (var handler in handlers)
        {
            if (handler == null) continue;

            var metadata = EventHandlerInvoker.GetMetadata(handler.GetType());
            if (metadata.IsBackground)
            {
                backgroundHandlerTypes.Add(handler.GetType());
            }
            else
            {
                syncTasks.Add(ExecuteHandlerWithConcurrencyControlAsync(handler, @event, eventType, cancellationToken));
            }
        }

        // 等待同步处理器完成（即使某些失败，其他处理器也会继续执行）
        // scope会在这里保持活动，直到同步任务完成
        if (syncTasks.Count > 0)
            await Task.WhenAll(syncTasks).ConfigureAwait(false);

        // Fire-and-forget 后台处理器（各自创建独立 Scope，不阻塞发布者）
        // 任务被跟踪,应用关闭时 DisposeAsync 会等待在飞任务排水(带超时),避免静默丢失
        foreach (var handlerType in backgroundHandlerTypes)
        {
            var capturedEvent = @event;
            var capturedEventType = eventType;
            // 捕获租户 ID，在新 scope 中恢复上下文
            var capturedTenantId = (@event as EventBase)?.TenantId;
            var backgroundTask = Task.Run(async () =>
            {
                // 后台处理器运行在独立作用域/独立事务中,必须隔离发布者的环境事务上下文,
                // 防止处理器内的再发布被挂到一个可能已提交/已释放的外部事务队列上
                AmbientUnitOfWork.Set(null);
                try
                {
                    using var bgScope = _serviceProvider.CreateScope();

                    // 恢复后台处理器的租户上下文
                    IDisposable? tenantScope = null;
                    if (capturedTenantId.HasValue)
                    {
                        var bgTenant = bgScope.ServiceProvider.GetService<ICurrentTenant>();
                        tenantScope = bgTenant?.Change(capturedTenantId.Value);
                    }

                    try
                    {
                        // Handlers are registered via `AddEventHandler<TEvent, THandler>()` as
                        // `services.AddScoped<IEventHandler<TEvent>, THandler>()` - i.e. only the
                        // interface descriptor exists, not a binding for the concrete type.
                        // Resolving by `handlerType` directly returns null and the background
                        // dispatch silently drops the event (regression introduced when sync
                        // handlers were converted to [BackgroundEventHandler]). Resolve via the
                        // interface and filter by concrete type to fix the dispatch.
                        var handlerInterface = typeof(IEventHandler<>).MakeGenericType(capturedEventType);
                        var allHandlers = bgScope.ServiceProvider.GetServices(handlerInterface);
                        var bgHandler = allHandlers.FirstOrDefault(h => h?.GetType() == handlerType);
                        if (bgHandler == null)
                        {
                            _logger.LogWarning(
                                "Background handler {HandlerType} for event {EventType} could not be resolved in a new scope. " +
                                "Ensure the handler is registered via AddEventHandler<TEvent, THandler>().",
                                handlerType.Name, capturedEventType.Name);
                            return;
                        }

                        await ExecuteHandlerAsync(bgHandler, capturedEvent, capturedEventType, CancellationToken.None);
                    }
                    finally
                    {
                        tenantScope?.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background handler {HandlerType} failed for event {EventType} (EventId: {EventId})",
                        handlerType.Name, capturedEventType.Name, capturedEvent.EventId);
                }
            });

            _backgroundTasks.TryAdd(backgroundTask, 0);
            _ = backgroundTask.ContinueWith(
                t => _backgroundTasks.TryRemove(t, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// 获取事件的所有处理器（支持事件继承和运行时订阅）
    /// </summary>
    private IEnumerable<object> GetEventHandlers<TEvent>(Type eventType, IServiceProvider serviceProvider) where TEvent : class, IEvent
    {
        var allHandlers = new List<object>();
        var handlerTypes = new HashSet<Type>();

        // 1. 从DI容器获取直接匹配的处理器
        var handlerInterface = typeof(IEventHandler<>).MakeGenericType(eventType);
        var directHandlers = serviceProvider.GetServices(handlerInterface);
        foreach (var handler in directHandlers)
        {
            if (handler != null)
            {
                allHandlers.Add(handler);
                handlerTypes.Add(handler.GetType());
            }
        }

        // 2. 获取基类事件的处理器（事件继承支持）
        var baseEventHandlers = GetBaseEventHandlers<TEvent>(eventType, serviceProvider);
        foreach (var handler in baseEventHandlers)
        {
            if (handler != null && !handlerTypes.Contains(handler.GetType()))
            {
                allHandlers.Add(handler);
                handlerTypes.Add(handler.GetType());
            }
        }

        // 3. 获取运行时注册的处理器
        // 注意：创建 HashSet 的副本以避免线程安全问题（HashSet 不是线程安全的）
        if (_runtimeHandlers.TryGetValue(eventType, out var runtimeHandlerTypes))
        {
            // 创建副本以避免在遍历时 HashSet 被其他线程修改
            var handlerTypesCopy = new HashSet<Type>(runtimeHandlerTypes);
            foreach (var handlerType in handlerTypesCopy)
            {
                if (!handlerTypes.Contains(handlerType))
                {
                    var runtimeHandler = serviceProvider.GetService(handlerType);
                    if (runtimeHandler != null)
                    {
                        allHandlers.Add(runtimeHandler);
                        handlerTypes.Add(handlerType);
                    }
                }
            }
        }

        return allHandlers;
    }

    /// <summary>
    /// 获取基类事件的处理器（支持事件继承）
    /// </summary>
    private IEnumerable<object> GetBaseEventHandlers<TEvent>(Type eventType, IServiceProvider serviceProvider) where TEvent : class, IEvent
    {
        var handlers = new List<object>();

        var baseInterfaces = EventHandlerInvoker.GetBaseHandlerInterfaces(eventType);
        foreach (var @interface in baseInterfaces)
        {
            var baseHandlers = serviceProvider.GetServices(@interface);
            foreach (var handler in baseHandlers)
            {
                if (handler != null)
                {
                    handlers.Add(handler);
                }
            }
        }

        return handlers;
    }

    /// <summary>
    /// 在并发控制下执行事件处理器（带错误隔离）
    /// </summary>
    private async Task ExecuteHandlerWithConcurrencyControlAsync<TEvent>(
        object handler,
        TEvent @event,
        Type eventType,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        // 等待信号量，限制并发数
        await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ExecuteHandlerAsync(handler, @event, eventType, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // 释放信号量
            _concurrencySemaphore.Release();
        }
    }

    /// <summary>
    /// 执行单个事件处理器（错误隔离、条件检查、重试和死信队列）
    /// 使用编译委托调用HandleAsync方法，支持基类事件处理器处理派生类事件
    /// </summary>
    private async Task ExecuteHandlerAsync<TEvent>(
        object handler,
        TEvent @event,
        Type eventType,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        var handlerType = handler.GetType();

        try
        {
            // 获取或创建处理器元数据（编译委托缓存）
            var metadata = EventHandlerInvoker.GetMetadata(handlerType);

            // 检查条件处理器
            if (metadata.CanHandleDelegate != null)
            {
                if (!metadata.CanHandleDelegate(handler, @event))
                {
                    _logger.LogDebug("Handler {HandlerType} cannot handle event {EventType} (EventId: {EventId})",
                        handlerType.Name, eventType.Name, @event.EventId);
                    return;
                }
            }

            // 执行 HandleAsync（带重试和计时）
            if (metadata.HandleDelegate != null)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await ExecuteWithRetryAsync(handler, @event, handlerType, eventType, metadata, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    stopwatch.Stop();
                    _logger.LogDebug("Handler {HandlerType} for event {EventType} (EventId: {EventId}) executed in {ElapsedMs}ms",
                        handlerType.Name, eventType.Name, @event.EventId, stopwatch.ElapsedMilliseconds);
                }
            }
            else
            {
                _logger.LogWarning("Handler {HandlerType} does not implement IEventHandler interface correctly",
                    handlerType.Name);
            }

            _logger.LogDebug("Handler {HandlerType} completed successfully for event {EventType}",
                handlerType.Name, eventType.Name);
        }
        catch (Exception ex)
        {
            // 错误隔离：记录错误但不影响其他处理器
            _logger.LogError(ex,
                "Error in handler {HandlerType} for event {EventType} (EventId: {EventId}). " +
                "This error is isolated and will not affect other handlers.",
                handlerType.Name, eventType.Name, @event.EventId);

            // 如果启用死信队列，将失败的事件添加到死信队列
            if (_options.EnableDeadLetterQueue && _deadLetterQueue != null)
            {
                try
                {
                    await _deadLetterQueue.AddAsync(@event, handlerType, ex, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Failed event {EventId} added to dead letter queue", @event.EventId);
                }
                catch (Exception dlqEx)
                {
                    _logger.LogError(dlqEx, "Failed to add event {EventId} to dead letter queue", @event.EventId);
                }
            }
        }
    }

    /// <summary>
    /// 带重试的执行处理器方法
    /// </summary>
    private async Task ExecuteWithRetryAsync<TEvent>(
        object handler,
        TEvent @event,
        Type handlerType,
        Type eventType,
        HandlerMetadata metadata,
        CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        if (!_options.EnableRetry || _options.RetryCount <= 0)
        {
            // 不启用重试，直接执行
            await metadata.HandleDelegate!(handler, @event, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 优先消费 ResilienceModule 的 "eventbus" 弹性管线(统一重试策略);
        // 管线未注册时回退到下方内置的指数退避重试
        if (_retryPipeline != null)
        {
            await _retryPipeline.ExecuteAsync(
                async ct => await metadata.HandleDelegate!(handler, @event, ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        Exception? lastException = null;
        for (int attempt = 0; attempt <= _options.RetryCount; attempt++)
        {
            try
            {
                await metadata.HandleDelegate!(handler, @event, cancellationToken).ConfigureAwait(false);
                // 成功，返回
                if (attempt > 0)
                {
                    _logger.LogInformation("Handler {HandlerType} succeeded after {Attempt} retry attempt(s) for event {EventType} (EventId: {EventId})",
                        handlerType.Name, attempt, eventType.Name, @event.EventId);
                }
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < _options.RetryCount)
                {
                    // 计算指数退避延迟：RetryInterval * (2 ^ attempt)
                    var delayMs = _options.RetryIntervalMs * (int)Math.Pow(2, attempt);
                    _logger.LogWarning(ex,
                        "Handler {HandlerType} failed for event {EventType} (EventId: {EventId}), attempt {Attempt}/{TotalAttempts}. Retrying after {DelayMs}ms...",
                        handlerType.Name, eventType.Name, @event.EventId, attempt + 1, _options.RetryCount + 1, delayMs);

                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // 最后一次尝试也失败
                    _logger.LogError(ex,
                        "Handler {HandlerType} failed for event {EventType} (EventId: {EventId}) after {TotalAttempts} attempt(s). No more retries.",
                        handlerType.Name, eventType.Name, @event.EventId, _options.RetryCount + 1);
                }
            }
        }

        // 所有重试都失败，抛出最后一个异常
        if (lastException != null)
        {
            throw lastException;
        }
    }

    /// <summary>
    /// 延迟发布事件(内联延迟实现)
    /// 注意语义：本实现通过 Task.Delay 在当前调用流内等待后发布,await 它会阻塞调用者整个延迟时长;
    /// 延迟期间应用关闭或调用方取消则事件不会发布(非持久化调度)。
    /// 适合短延迟的轻量场景;需要可靠的长延迟/持久化调度请使用后台任务系统(如 Hangfire)
    /// </summary>
    public async Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default) where TEvent : class, IEvent
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        await PublishAsync(@event, cancellationToken).ConfigureAwait(false);
    }

    public bool HasHandlers<TEvent>() where TEvent : class, IEvent
    {
        return GetHandlerCount<TEvent>() > 0;
    }

    public int GetHandlerCount<TEvent>() where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);
        var handlerTypes = new HashSet<Type>();

        using var scope = _serviceProvider.CreateScope();

        // 检查DI容器注册的处理器
        var handlerInterface = typeof(IEventHandler<>).MakeGenericType(eventType);
        var diHandlers = scope.ServiceProvider.GetServices(handlerInterface);
        foreach (var handler in diHandlers)
        {
            if (handler != null)
                handlerTypes.Add(handler.GetType());
        }

        // 检查基类事件的处理器（去重）
        var baseHandlers = GetBaseEventHandlers<TEvent>(eventType, scope.ServiceProvider);
        foreach (var handler in baseHandlers)
        {
            if (handler != null)
                handlerTypes.Add(handler.GetType());
        }

        // 检查运行时注册的处理器（去重）
        // 注意：创建 HashSet 的副本以避免线程安全问题（HashSet 不是线程安全的）
        if (_runtimeHandlers.TryGetValue(eventType, out var runtimeHandlerTypes))
        {
            // 创建副本以避免在遍历时 HashSet 被其他线程修改
            var handlerTypesCopy = new HashSet<Type>(runtimeHandlerTypes);
            foreach (var handlerType in handlerTypesCopy)
            {
                handlerTypes.Add(handlerType);
            }
        }

        return handlerTypes.Count;
    }

    public void Subscribe<TEvent, THandler>()
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        var eventType = typeof(TEvent);
        var handlerType = typeof(THandler);

        _runtimeHandlers.AddOrUpdate(
            eventType,
            new HashSet<Type> { handlerType },
            (key, existing) =>
            {
                // 创建新的HashSet以避免线程安全问题
                // HashSet不是线程安全的，直接修改可能导致竞态条件
                var newSet = new HashSet<Type>(existing);
                newSet.Add(handlerType);
                return newSet;
            });

        _logger.LogInformation("Subscribed handler {HandlerType} for event {EventType}", handlerType.Name, eventType.Name);
    }

    public void Unsubscribe<TEvent, THandler>()
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        var eventType = typeof(TEvent);
        var handlerType = typeof(THandler);

        // 使用循环和原子操作确保线程安全
        while (true)
        {
            if (!_runtimeHandlers.TryGetValue(eventType, out var existing))
            {
                // 不存在，直接返回
                return;
            }

            // 创建新的HashSet以避免线程安全问题
            var newSet = new HashSet<Type>(existing);
            if (!newSet.Remove(handlerType))
            {
                // 未找到要移除的处理器，直接返回
                return;
            }

            // 尝试原子更新：如果existing仍然是当前值，则用newSet替换
            if (_runtimeHandlers.TryUpdate(eventType, newSet, existing))
            {
                // 更新成功，检查是否为空并清理
                if (newSet.Count == 0)
                {
                    _runtimeHandlers.TryRemove(eventType, out _);
                }
                _logger.LogInformation("Unsubscribed handler {HandlerType} for event {EventType}", handlerType.Name, eventType.Name);
                return;
            }

            // 更新失败（并发修改），重试
        }
    }

    public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);
        if (_runtimeHandlers.TryRemove(eventType, out _))
        {
            _logger.LogInformation("Unsubscribed all runtime handlers for event {EventType}", eventType.Name);
        }
    }


    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 释放资源(同步路径,不等待在飞后台处理器;优雅关闭请走 DisposeAsync)
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _concurrencySemaphore?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 异步释放：先等待在飞后台处理器排水(最长 10 秒),再释放资源
    /// 容器关闭时(ServiceProvider.DisposeAsync)自动走此路径,避免应用关闭丢失后台事件处理
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        var inFlight = _backgroundTasks.Keys.ToArray();
        if (inFlight.Length > 0)
        {
            _logger.LogInformation("Draining {Count} in-flight background event handler task(s) before shutdown", inFlight.Length);
            var drainTask = Task.WhenAll(inFlight);
            var completed = await Task.WhenAny(drainTask, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            if (completed != drainTask)
            {
                _logger.LogWarning("Timed out waiting for {Count} background event handler task(s) to complete during shutdown",
                    _backgroundTasks.Count);
            }
        }

        _concurrencySemaphore?.Dispose();
        GC.SuppressFinalize(this);
    }
}