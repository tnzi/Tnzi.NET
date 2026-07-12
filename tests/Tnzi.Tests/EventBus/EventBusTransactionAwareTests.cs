namespace Tnzi.Tests.EventBus;

/// <summary>
/// 事务感知发布 + 总线分离注册 + 死信队列有界 + 关闭排水 回归测试
/// </summary>
public class EventBusTransactionAwareTests
{
    public class TxTestEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
    }

    public class TxTestEventHandler : IEventHandler<TxTestEvent>
    {
        public int CallCount;

        public Task HandleAsync(TxTestEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 假环境事务作用域:模拟 UnitOfWorkManager 的 IAmbientUnitOfWorkScope 行为
    /// </summary>
    private sealed class FakeAmbientScope : IAmbientUnitOfWorkScope
    {
        private readonly List<Func<CancellationToken, Task>> _postCommitActions = [];

        public bool IsTransactionActive { get; set; } = true;

        public int PendingCount => _postCommitActions.Count;

        public void EnqueuePostCommit(Func<CancellationToken, Task> action) => _postCommitActions.Add(action);

        /// <summary>模拟提交:清除活跃标志后执行队列(与 UnitOfWorkManager 语义一致)</summary>
        public async Task CommitAsync()
        {
            IsTransactionActive = false;
            AmbientUnitOfWork.Set(null);
            foreach (var action in _postCommitActions)
            {
                await action(CancellationToken.None);
            }
            _postCommitActions.Clear();
        }

        /// <summary>模拟回滚:丢弃全部延迟操作</summary>
        public void Rollback()
        {
            IsTransactionActive = false;
            AmbientUnitOfWork.Set(null);
            _postCommitActions.Clear();
        }
    }

    private static (LocalEventBus Bus, TxTestEventHandler Handler) CreateBus(EventBusOptions? options = null)
    {
        var services = new ServiceCollection();
        var handler = new TxTestEventHandler();
        services.AddScoped<IEventHandler<TxTestEvent>>(_ => handler);
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<LocalEventBus>>();
        var bus = new LocalEventBus(provider, logger, options ?? new EventBusOptions());
        return (bus, handler);
    }

    [Fact]
    public async Task PublishAsync_InsideActiveTransaction_DefersUntilCommit()
    {
        var (bus, handler) = CreateBus();
        var scope = new FakeAmbientScope();
        AmbientUnitOfWork.Set(scope);
        try
        {
            await bus.PublishAsync(new TxTestEvent { Message = "deferred" });

            // 事务中:处理器不应被调用,发布被排入提交后队列
            Assert.Equal(0, handler.CallCount);
            Assert.Equal(1, scope.PendingCount);

            await scope.CommitAsync();

            // 提交后:延迟的发布被执行,处理器被调用一次
            Assert.Equal(1, handler.CallCount);
        }
        finally
        {
            AmbientUnitOfWork.Set(null);
        }
    }

    [Fact]
    public async Task PublishAsync_AfterRollback_EventIsDiscarded()
    {
        var (bus, handler) = CreateBus();
        var scope = new FakeAmbientScope();
        AmbientUnitOfWork.Set(scope);
        try
        {
            await bus.PublishAsync(new TxTestEvent { Message = "discarded" });
            Assert.Equal(0, handler.CallCount);

            scope.Rollback();

            // 回滚后事件被丢弃,处理器永不执行
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            AmbientUnitOfWork.Set(null);
        }
    }

    [Fact]
    public async Task PublishAsync_WithoutTransaction_PublishesImmediately()
    {
        var (bus, handler) = CreateBus();
        AmbientUnitOfWork.Set(null);

        await bus.PublishAsync(new TxTestEvent { Message = "immediate" });

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task PublishAsync_TransactionAwareDisabled_PublishesImmediatelyInsideTransaction()
    {
        var (bus, handler) = CreateBus(new EventBusOptions { TransactionAwarePublish = false });
        var scope = new FakeAmbientScope();
        AmbientUnitOfWork.Set(scope);
        try
        {
            await bus.PublishAsync(new TxTestEvent { Message = "legacy" });

            // 关闭事务感知:恢复旧行为,事务中也立即派发
            Assert.Equal(1, handler.CallCount);
            Assert.Equal(0, scope.PendingCount);
        }
        finally
        {
            AmbientUnitOfWork.Set(null);
        }
    }

    [Fact]
    public async Task EventBusModule_Registers_IEventBus_And_ILocalEventBus_AsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var module = new EventBusModule();
        var context = new ServiceConfigurationContext(services, configuration);

        await module.ConfigureServicesAsync(context);

        using var provider = services.BuildServiceProvider();
        var eventBus = provider.GetRequiredService<IEventBus>();
        var localEventBus = provider.GetRequiredService<ILocalEventBus>();

        // IEventBus 永远指向本地总线,与 ILocalEventBus 同一实例
        Assert.Same(eventBus, localEventBus);
        Assert.True(eventBus.IsLocal);
        // 分布式总线未注册时不可解析
        Assert.Null(provider.GetService<IDistributedEventBus>());
    }

    [Fact]
    public async Task InMemoryDeadLetterQueue_EvictsOldestWhenOverCapacity()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EventBusOptions { DeadLetterQueueCapacity = 3 });
        var dlq = new InMemoryEventDeadLetterQueue(options);

        var events = new List<TxTestEvent>();
        for (var i = 0; i < 5; i++)
        {
            var evt = new TxTestEvent { Message = $"dead-{i}" };
            events.Add(evt);
            await dlq.AddAsync(evt, typeof(TxTestEventHandler), new InvalidOperationException($"failure {i}"));
            // FailedAt 用 UtcNow,加微小间隔保证驱逐顺序确定
            await Task.Delay(5);
        }

        var remaining = await dlq.GetAllAsync();

        Assert.Equal(3, remaining.Count);
        // 最旧的两条(0/1)被驱逐,最新三条保留
        var remainingIds = remaining.Select(e => e.EventId).ToHashSet();
        Assert.DoesNotContain(events[0].EventId, remainingIds);
        Assert.DoesNotContain(events[1].EventId, remainingIds);
        Assert.Contains(events[4].EventId, remainingIds);
    }

    public class SlowBackgroundEvent : EventBase
    {
    }

    [BackgroundEventHandler]
    public class SlowBackgroundHandler : IEventHandler<SlowBackgroundEvent>
    {
        public static readonly TaskCompletionSource<bool> Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(SlowBackgroundEvent @event, CancellationToken cancellationToken = default)
        {
            await Task.Delay(200, CancellationToken.None);
            Completed.TrySetResult(true);
        }
    }

    [Fact]
    public async Task DisposeAsync_DrainsInFlightBackgroundHandlers()
    {
        var services = new ServiceCollection();
        services.AddScoped<IEventHandler<SlowBackgroundEvent>, SlowBackgroundHandler>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<LocalEventBus>>();
        var bus = new LocalEventBus(provider, logger);

        await bus.PublishAsync(new SlowBackgroundEvent());

        // 后台处理器仍在飞行中,DisposeAsync 应等待其完成(排水)
        await bus.DisposeAsync();

        Assert.True(SlowBackgroundHandler.Completed.Task.IsCompletedSuccessfully,
            "Background handler should have completed before DisposeAsync returned");
    }
}
