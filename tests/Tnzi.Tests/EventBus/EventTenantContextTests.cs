using Tnzi.Application;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

namespace Tnzi.Tests.EventBus;

/// <summary>
/// <c>EventBase.TenantId</c> 契约的两半（发布时捕获、处理前恢复）收口在 <see cref="EventTenantContext"/>，
/// 本地总线与两个分布式传输调的是同一段代码。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：契约此前只有 <c>LocalEventBus</c> 兑现。集成事件从 <c>PublishEventAsync</c>
/// 出发时绕过本地总线（Outbox / 分布式总线），线上 JSON 的 <c>TenantId</c> 恒为 null；
/// 消费侧只 <c>CreateScope()</c> 就跑处理器 —— 多租户开启时处理器一律跑在 null 租户作用域里，
/// 读到空结果集、写出 TenantId 为 null 的行，全程不抛异常。
/// </remarks>
public class EventTenantContextTests
{
    public class ProbeEvent : EventBase
    {
    }

    public class ProbeIntegrationEvent : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid? TenantId { get; init; }
        public bool IsAuthenticated => false;
        public Guid? Id => null;
        public string? UserName => null;
        public string[] Roles => [];
        public bool IsInRole(string roleName) => false;
    }

    private static ServiceProvider BuildProvider(Guid? ambientTenantId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = ambientTenantId });
        // 与生产一致：真实的 CurrentTenant，Scoped
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Capture_FillsTenantIdFromTheAmbientTenant()
    {
        var tenantId = Guid.NewGuid();
        using var provider = BuildProvider(tenantId);
        using var scope = provider.CreateScope();
        var @event = new ProbeEvent();

        EventTenantContext.Capture(@event, scope.ServiceProvider);

        Assert.Equal(tenantId, @event.TenantId);
    }

    [Fact]
    public void Capture_SeesATemporaryChangeInTheSameScope()
    {
        var overridden = Guid.NewGuid();
        using var provider = BuildProvider(Guid.NewGuid());
        using var scope = provider.CreateScope();
        var @event = new ProbeEvent();

        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(overridden))
        {
            EventTenantContext.Capture(@event, scope.ServiceProvider);
        }

        Assert.Equal(overridden, @event.TenantId);
    }

    [Fact]
    public void Capture_DoesNotOverwriteAnExplicitTenantId()
    {
        var explicitTenant = Guid.NewGuid();
        using var provider = BuildProvider(Guid.NewGuid());
        using var scope = provider.CreateScope();
        var @event = new ProbeEvent { TenantId = explicitTenant };

        EventTenantContext.Capture(@event, scope.ServiceProvider);

        Assert.Equal(explicitTenant, @event.TenantId);
    }

    [Fact]
    public void Capture_LeavesTenantIdNullWhenThereIsNoTenant()
    {
        using var provider = BuildProvider(null);
        using var scope = provider.CreateScope();
        var @event = new ProbeEvent();

        EventTenantContext.Capture(@event, scope.ServiceProvider);

        Assert.Null(@event.TenantId);
    }

    [Fact]
    public void CaptureInNewScope_FillsTenantIdFromARootProvider()
    {
        var tenantId = Guid.NewGuid();
        using var provider = BuildProvider(tenantId);
        var @event = new ProbeEvent();

        EventTenantContext.CaptureInNewScope(@event, provider);

        Assert.Equal(tenantId, @event.TenantId);
    }

    [Fact]
    public void Restore_ChangesTheCurrentTenantUntilDisposed()
    {
        var tenantId = Guid.NewGuid();
        using var provider = BuildProvider(null);
        using var scope = provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var @event = new ProbeEvent { TenantId = tenantId };

        var restored = EventTenantContext.Restore(@event, scope.ServiceProvider);

        Assert.NotNull(restored);
        Assert.Equal(tenantId, currentTenant.Id);

        restored.Dispose();
        Assert.Null(currentTenant.Id);
    }

    [Fact]
    public void Restore_DoesNothingForAnEventWithoutATenant()
    {
        var ambient = Guid.NewGuid();
        using var provider = BuildProvider(ambient);
        using var scope = provider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        var restored = EventTenantContext.Restore(new ProbeEvent(), scope.ServiceProvider);

        Assert.Null(restored);
        Assert.Equal(ambient, currentTenant.Id);
    }

    [Fact]
    public void Restore_ReturnsNullWhenNoCurrentTenantIsRegistered()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.Null(EventTenantContext.Restore(new ProbeEvent { TenantId = Guid.NewGuid() }, provider));
    }

    #region PublishEventAsync 路由收口

    private sealed class RecordingEventStore : IEventStore
    {
        public IEvent? Saved { get; private set; }

        public Task SaveEventAsync(IEvent @event, string eventType, CancellationToken cancellationToken = default)
        {
            Saved = @event;
            return Task.CompletedTask;
        }

        public Task<IEnumerable<StoredEvent>> GetUnprocessedEventsAsync(int count = 100, CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<StoredEvent>());

        public Task MarkAsProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task MarkAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<StoredEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default) => Task.FromResult<StoredEvent?>(null);

        public Task<IPagedList<StoredEvent>> GetEventsAsync(EventQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> DeleteExpiredEventsAsync(int days = 90, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class RecordingDistributedEventBus : IDistributedEventBus
    {
        public IEvent? Published { get; private set; }
        public bool IsLocal => false;

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent
        {
            Published = @event;
            return Task.CompletedTask;
        }

        public Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default) where TEvent : class, IEvent
            => PublishAsync(@event, cancellationToken);

        public void Subscribe<TEvent, THandler>() where TEvent : class, IEvent where THandler : class, IEventHandler<TEvent> { }
        public void Unsubscribe<TEvent, THandler>() where TEvent : class, IEvent where THandler : class, IEventHandler<TEvent> { }
        public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent { }
        public bool HasHandlers<TEvent>() where TEvent : class, IEvent => false;
        public int GetHandlerCount<TEvent>() where TEvent : class, IEvent => 0;
    }

    private sealed class ProbeService(IServiceProvider serviceProvider) : ApplicationService(serviceProvider)
    {
        public Task PublishAsync(IIntegrationEvent @event) => PublishEventAsync(@event);
    }

    private static ServiceProvider BuildRoutingProvider(Guid tenantId, Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 集成事件走 Outbox 时，落库前 TenantId 必须已经填好 —— 中继重发的是库里那份 JSON。
    /// </summary>
    [Fact]
    public async Task PublishEventAsync_StampsTenantIdBeforeTheOutboxTakesTheEvent()
    {
        var tenantId = Guid.NewGuid();
        var store = new RecordingEventStore();
        using var provider = BuildRoutingProvider(tenantId, s => s.AddSingleton<IEventStore>(store));
        using var scope = provider.CreateScope();
        var service = new ProbeService(scope.ServiceProvider);

        await service.PublishAsync(new ProbeIntegrationEvent());

        var saved = Assert.IsType<ProbeIntegrationEvent>(store.Saved);
        Assert.Equal(tenantId, saved.TenantId);
    }

    /// <summary>
    /// 集成事件直接交给分布式总线时同样如此 —— 总线原样序列化，不会替调用方补。
    /// </summary>
    [Fact]
    public async Task PublishEventAsync_StampsTenantIdBeforeTheDistributedBusTakesTheEvent()
    {
        var tenantId = Guid.NewGuid();
        var bus = new RecordingDistributedEventBus();
        using var provider = BuildRoutingProvider(tenantId, s => s.AddSingleton<IDistributedEventBus>(bus));
        using var scope = provider.CreateScope();
        var service = new ProbeService(scope.ServiceProvider);

        await service.PublishAsync(new ProbeIntegrationEvent());

        var published = Assert.IsType<ProbeIntegrationEvent>(bus.Published);
        Assert.Equal(tenantId, published.TenantId);
    }

    /// <summary>
    /// 调用方 <c>Change()</c> 过的租户也要被看见：捕获点必须用调用方自己的作用域。
    /// </summary>
    [Fact]
    public async Task PublishEventAsync_CapturesATemporarilyChangedTenant()
    {
        var overridden = Guid.NewGuid();
        var bus = new RecordingDistributedEventBus();
        using var provider = BuildRoutingProvider(Guid.NewGuid(), s => s.AddSingleton<IDistributedEventBus>(bus));
        using var scope = provider.CreateScope();
        var service = new ProbeService(scope.ServiceProvider);

        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(overridden))
        {
            await service.PublishAsync(new ProbeIntegrationEvent());
        }

        Assert.Equal(overridden, ((ProbeIntegrationEvent)bus.Published!).TenantId);
    }

    #endregion
}
