using Tnzi.MultiTenancy;

namespace Tnzi.Tests.EventBus;

/// <summary>
/// 本地总线派发语义的三条契约：按事件的运行时类型找处理器、同步处理器在同一作用域里逐个执行、
/// 同步处理器跑在事件携带的租户下（延迟到提交后的发布也一样）。
/// </summary>
/// <remarks>
/// 三条都是「机制完整、外观正常、从未生效」的形态：领域事件以 <see cref="IEvent"/> 静态类型发布时
/// 一个处理器都找不到，只留一条 Debug 日志；两个同步处理器并发跑在同一个 DbContext 上，
/// 后一个撞上「A second operation was started on this context」而只被记一条错误；
/// 事务内发布延迟到 Action 返回后才派发，此时 <c>Change()</c> 早已退出，处理器读到的是 null 租户。
/// </remarks>
public class LocalEventBusDispatchSemanticsTests
{
    public class ConcreteEvent : EventBase
    {
        public string Payload { get; set; } = string.Empty;
    }

    public class OtherConcreteEvent : EventBase
    {
    }

    public class Calls
    {
        public ConcurrentQueue<string> Log { get; } = new();
    }

    public class ConcreteEventHandler : IEventHandler<ConcreteEvent>
    {
        private readonly Calls _calls;

        public ConcreteEventHandler(Calls calls) => _calls = calls;

        public Task HandleAsync(ConcreteEvent @event, CancellationToken cancellationToken = default)
        {
            _calls.Log.Enqueue($"concrete:{@event.Payload}");
            return Task.CompletedTask;
        }
    }

    public class OtherConcreteEventHandler : IEventHandler<OtherConcreteEvent>
    {
        private readonly Calls _calls;

        public OtherConcreteEventHandler(Calls calls) => _calls = calls;

        public Task HandleAsync(OtherConcreteEvent @event, CancellationToken cancellationToken = default)
        {
            _calls.Log.Enqueue("other");
            return Task.CompletedTask;
        }
    }

    public class FailingConcreteEventHandler : IEventHandler<ConcreteEvent>
    {
        public Task HandleAsync(ConcreteEvent @event, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("handler failure");
    }

    /// <summary>
    /// 模拟 DbContext 的并发检查：同一作用域内上一次操作没结束就开始下一次，直接抛异常。
    /// </summary>
    public class SingleOperationResource
    {
        private int _inFlight;

        public async Task RunAsync(Func<Task> operation)
        {
            if (Interlocked.Increment(ref _inFlight) != 1)
            {
                Interlocked.Decrement(ref _inFlight);
                throw new InvalidOperationException("A second operation was started on this resource before a previous operation completed.");
            }

            try
            {
                await operation();
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    public abstract class ResourceUsingHandler : IEventHandler<ConcreteEvent>
    {
        private readonly SingleOperationResource _resource;
        private readonly Calls _calls;
        private readonly string _name;

        protected ResourceUsingHandler(SingleOperationResource resource, Calls calls, string name)
        {
            _resource = resource;
            _calls = calls;
            _name = name;
        }

        public Task HandleAsync(ConcreteEvent @event, CancellationToken cancellationToken = default)
            => _resource.RunAsync(async () =>
            {
                _calls.Log.Enqueue($"{_name}:start");
                // 真实 I/O 的形状：第一次 await 让出线程，下一个处理器若被并发启动就会在这里撞上
                await Task.Delay(30, cancellationToken);
                _calls.Log.Enqueue($"{_name}:end");
            });
    }

    public class FirstResourceHandler : ResourceUsingHandler
    {
        public FirstResourceHandler(SingleOperationResource resource, Calls calls) : base(resource, calls, "first") { }
    }

    public class SecondResourceHandler : ResourceUsingHandler
    {
        public SecondResourceHandler(SingleOperationResource resource, Calls calls) : base(resource, calls, "second") { }
    }

    [EventHandlerOrder(-5)]
    public class EarlyOrderedHandler : IEventHandler<ConcreteEvent>
    {
        private readonly Calls _calls;

        public EarlyOrderedHandler(Calls calls) => _calls = calls;

        public Task HandleAsync(ConcreteEvent @event, CancellationToken cancellationToken = default)
        {
            _calls.Log.Enqueue("early");
            return Task.CompletedTask;
        }
    }

    public class TenantObservingHandler : IEventHandler<ConcreteEvent>
    {
        private readonly ICurrentTenant _currentTenant;
        private readonly TenantObservation _observation;

        public TenantObservingHandler(ICurrentTenant currentTenant, TenantObservation observation)
        {
            _currentTenant = currentTenant;
            _observation = observation;
        }

        public Task HandleAsync(ConcreteEvent @event, CancellationToken cancellationToken = default)
        {
            _observation.Seen.Enqueue(_currentTenant.Id);
            return Task.CompletedTask;
        }
    }

    public class TenantObservation
    {
        public ConcurrentQueue<Guid?> Seen { get; } = new();
    }

    private sealed class FakeAmbientScope : IAmbientUnitOfWorkScope
    {
        private readonly List<Func<CancellationToken, Task>> _postCommitActions = [];

        public bool IsTransactionActive { get; private set; } = true;

        public void EnqueuePostCommit(Func<CancellationToken, Task> action) => _postCommitActions.Add(action);

        public async Task CommitAsync()
        {
            IsTransactionActive = false;
            AmbientUnitOfWork.Set(null);
            foreach (var action in _postCommitActions)
                await action(CancellationToken.None);
            _postCommitActions.Clear();
        }
    }

    private static (LocalEventBus Bus, ServiceProvider Provider) BuildBus(Action<IServiceCollection> configure, EventBusOptions? options = null, IEventDeadLetterQueue? deadLetterQueue = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Calls>();
        configure(services);
        var provider = services.BuildServiceProvider();
        var bus = new LocalEventBus(provider, provider.GetRequiredService<ILogger<LocalEventBus>>(), options, deadLetterQueue);
        return (bus, provider);
    }

    // ------------------------------------------------------------------
    // 按运行时类型派发
    // ------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_EventHeldAsIEvent_ReachesTheConcreteHandler()
    {
        var (bus, provider) = BuildBus(s => s.AddScoped<IEventHandler<ConcreteEvent>, ConcreteEventHandler>());
        using var _ = provider;

        IEvent @event = new ConcreteEvent { Payload = "as-interface" };
        await bus.PublishAsync(@event);

        Assert.Equal(["concrete:as-interface"], provider.GetRequiredService<Calls>().Log);
    }

    [Fact]
    public async Task PublishAsync_EventHeldAsBaseClass_ReachesOnlyTheMatchingConcreteHandler()
    {
        var (bus, provider) = BuildBus(s =>
        {
            s.AddScoped<IEventHandler<ConcreteEvent>, ConcreteEventHandler>();
            s.AddScoped<IEventHandler<OtherConcreteEvent>, OtherConcreteEventHandler>();
        });
        using var _ = provider;

        EventBase first = new ConcreteEvent { Payload = "base" };
        EventBase second = new OtherConcreteEvent();
        await bus.PublishAsync(first);
        await bus.PublishAsync(second);

        Assert.Equal(["concrete:base", "other"], provider.GetRequiredService<Calls>().Log);
    }

    [Fact]
    public async Task PublishAsync_EventHeldAsIEvent_InsideTransaction_ReachesTheConcreteHandlerAfterCommit()
    {
        var (bus, provider) = BuildBus(s => s.AddScoped<IEventHandler<ConcreteEvent>, ConcreteEventHandler>());
        using var _ = provider;
        var calls = provider.GetRequiredService<Calls>();
        var ambient = new FakeAmbientScope();
        AmbientUnitOfWork.Set(ambient);
        try
        {
            IEvent @event = new ConcreteEvent { Payload = "deferred" };
            await bus.PublishAsync(@event);
            Assert.Empty(calls.Log);

            await ambient.CommitAsync();

            Assert.Equal(["concrete:deferred"], calls.Log);
        }
        finally
        {
            AmbientUnitOfWork.Set(null);
        }
    }

    [Fact]
    public async Task PublishAsync_EventHeldAsIEvent_DeadLetterRecordsTheRuntimeType()
    {
        var deadLetters = new InMemoryEventDeadLetterQueue();
        var (bus, provider) = BuildBus(
            s => s.AddScoped<IEventHandler<ConcreteEvent>, FailingConcreteEventHandler>(),
            new EventBusOptions { EnableDeadLetterQueue = true },
            deadLetters);
        using var _ = provider;

        IEvent @event = new ConcreteEvent { Payload = "dlq" };
        await bus.PublishAsync(@event);

        var entry = Assert.Single(await deadLetters.GetAllAsync());
        Assert.Equal(typeof(ConcreteEvent).FullName, entry.EventTypeName);
        Assert.Contains("dlq", entry.EventData);
    }

    // ------------------------------------------------------------------
    // 同步处理器逐个执行
    // ------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_TwoSyncHandlersSharingAScopedResource_RunOneAfterTheOther()
    {
        var (bus, provider) = BuildBus(s =>
        {
            s.AddScoped<SingleOperationResource>();
            s.AddScoped<IEventHandler<ConcreteEvent>, FirstResourceHandler>();
            s.AddScoped<IEventHandler<ConcreteEvent>, SecondResourceHandler>();
        });
        using var _ = provider;

        await bus.PublishAsync(new ConcreteEvent());

        // 两个处理器都完整跑完（没有一个撞上并发检查），且按注册顺序首尾相接
        Assert.Equal(["first:start", "first:end", "second:start", "second:end"], provider.GetRequiredService<Calls>().Log);
    }

    [Fact]
    public async Task PublishAsync_FailingFirstHandler_DoesNotStopTheNextOne()
    {
        var (bus, provider) = BuildBus(s =>
        {
            s.AddScoped<IEventHandler<ConcreteEvent>, FailingConcreteEventHandler>();
            s.AddScoped<IEventHandler<ConcreteEvent>, ConcreteEventHandler>();
        });
        using var _ = provider;

        await bus.PublishAsync(new ConcreteEvent { Payload = "after-failure" });

        Assert.Equal(["concrete:after-failure"], provider.GetRequiredService<Calls>().Log);
    }

    [Fact]
    public async Task PublishAsync_EventHandlerOrder_RunsLowerOrderFirst_OtherwiseRegistrationOrder()
    {
        var (bus, provider) = BuildBus(s =>
        {
            s.AddScoped<IEventHandler<ConcreteEvent>, ConcreteEventHandler>();
            s.AddScoped<IEventHandler<ConcreteEvent>, EarlyOrderedHandler>();
            s.AddScoped<IEventHandler<ConcreteEvent>, OtherOrderZeroHandler>();
        });
        using var _ = provider;

        await bus.PublishAsync(new ConcreteEvent { Payload = "ordered" });

        Assert.Equal(["early", "concrete:ordered", "zero"], provider.GetRequiredService<Calls>().Log);
    }

    public class OtherOrderZeroHandler : IEventHandler<ConcreteEvent>
    {
        private readonly Calls _calls;

        public OtherOrderZeroHandler(Calls calls) => _calls = calls;

        public Task HandleAsync(ConcreteEvent @event, CancellationToken cancellationToken = default)
        {
            _calls.Log.Enqueue("zero");
            return Task.CompletedTask;
        }
    }

    // ------------------------------------------------------------------
    // 同步处理器跑在事件的租户下
    // ------------------------------------------------------------------

    private static (LocalEventBus Bus, ServiceProvider Provider) BuildTenantBus()
        => BuildBus(s =>
        {
            s.AddScoped<ICurrentTenant, CurrentTenant>();
            s.AddSingleton<TenantObservation>();
            s.AddScoped<IEventHandler<ConcreteEvent>, TenantObservingHandler>();
        });

    [Fact]
    public async Task DeferredPublish_ChangeScopeEndsBeforeCommit_SyncHandlerStillRunsUnderThePublishersTenant()
    {
        var (bus, provider) = BuildTenantBus();
        using var _ = provider;
        var tenantId = Guid.NewGuid();
        var @event = new ConcreteEvent();
        var ambient = new FakeAmbientScope();
        AmbientUnitOfWork.Set(ambient);
        try
        {
            using (var publisherScope = provider.CreateScope())
            {
                var publisherTenant = publisherScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
                using (publisherTenant.Change(tenantId))
                {
                    await bus.PublishAsync(@event);
                }
            }

            // 提交发生在 Change() 作用域之外（全局工作单元在 Action 返回后才提交）
            await ambient.CommitAsync();
        }
        finally
        {
            AmbientUnitOfWork.Set(null);
        }

        Assert.Equal(tenantId, @event.TenantId);
        Assert.Equal([tenantId], provider.GetRequiredService<TenantObservation>().Seen);
    }

    [Fact]
    public async Task Publish_EventWithExplicitTenant_SyncHandlerRunsUnderThatTenant_AndPublisherIsUntouched()
    {
        var (bus, provider) = BuildTenantBus();
        using var _ = provider;
        var tenantId = Guid.NewGuid();

        using var publisherScope = provider.CreateScope();
        var publisherTenant = publisherScope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        await bus.PublishAsync(new ConcreteEvent { TenantId = tenantId });

        Assert.Equal([tenantId], provider.GetRequiredService<TenantObservation>().Seen);
        Assert.Null(publisherTenant.Id);
    }

    /// <summary>构造时就读租户的处理器（把租户存进字段）也要拿到事件的租户：切换必须发生在解析处理器之前。</summary>
    public class ConstructorTenantHandler : IEventHandler<ConcreteEvent>
    {
        private readonly Guid? _tenantAtConstruction;
        private readonly TenantObservation _observation;

        public ConstructorTenantHandler(ICurrentTenant currentTenant, TenantObservation observation)
        {
            _tenantAtConstruction = currentTenant.Id;
            _observation = observation;
        }

        public Task HandleAsync(ConcreteEvent @event, CancellationToken cancellationToken = default)
        {
            _observation.Seen.Enqueue(_tenantAtConstruction);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_EventWithExplicitTenant_HandlerReadingTheTenantInItsConstructorSeesIt()
    {
        var (bus, provider) = BuildBus(s =>
        {
            s.AddScoped<ICurrentTenant, CurrentTenant>();
            s.AddSingleton<TenantObservation>();
            s.AddScoped<IEventHandler<ConcreteEvent>, ConstructorTenantHandler>();
        });
        using var _ = provider;
        var tenantId = Guid.NewGuid();

        await bus.PublishAsync(new ConcreteEvent { TenantId = tenantId });

        Assert.Equal([tenantId], provider.GetRequiredService<TenantObservation>().Seen);
    }

    [Fact]
    public async Task Publish_TenantlessEvent_LeavesTheHandlersAmbientTenantAlone()
    {
        var (bus, provider) = BuildTenantBus();
        using var _ = provider;

        await bus.PublishAsync(new ConcreteEvent());

        Assert.Equal([(Guid?)null], provider.GetRequiredService<TenantObservation>().Seen);
    }
}
