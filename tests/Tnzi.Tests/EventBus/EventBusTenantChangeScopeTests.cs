using Tnzi.MultiTenancy;

namespace Tnzi.Tests.EventBus;

/// <summary>
/// 用<b>真实</b> <see cref="CurrentTenant"/>（不 Mock）验证：发布者经 <see cref="ICurrentTenant.Change"/>
/// 建立的租户，对事件的 <c>TenantId</c>、同步处理器与后台处理器解析到的 <see cref="ICurrentTenant"/> 全部可见。
/// </summary>
/// <remarks>
/// <see cref="LocalEventBus"/> 在一个新建的根子作用域里解析 <see cref="ICurrentTenant"/>（Scoped ⇒ 新实例）取租户。
/// 覆盖若存在实例级 <c>AsyncLocal</c> 上，新实例什么都看不见：凡由 <c>TenantResolverMiddleware</c>、
/// 按租户轮转的后台服务、登录注册流程经 <c>Change()</c> 建立的租户，事件与处理器一律读空 ——
/// 同步处理器的仓储按 null 过滤、写入挂错租户。既有的 <c>EventBusTenantTests</c> 全用
/// <c>Mock&lt;ICurrentTenant&gt;</c>，无论哪个作用域都答同一值，恰好把这条盖住了。
/// </remarks>
public class EventBusTenantChangeScopeTests
{
    public class ScopedTenantEvent : EventBase;

    public class TenantRecordingHandler(ICurrentTenant currentTenant) : IEventHandler<ScopedTenantEvent>
    {
        public Guid? SeenTenantId { get; private set; }
        public bool Invoked { get; private set; }

        public Task HandleAsync(ScopedTenantEvent @event, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            SeenTenantId = currentTenant.Id;
            return Task.CompletedTask;
        }
    }

    public class BackgroundSignal
    {
        public TaskCompletionSource<Guid?> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [BackgroundEventHandler]
    public class BackgroundTenantRecordingHandler(ICurrentTenant currentTenant, BackgroundSignal signal) : IEventHandler<ScopedTenantEvent>
    {
        public Task HandleAsync(ScopedTenantEvent @event, CancellationToken cancellationToken = default)
        {
            signal.Tcs.TrySetResult(currentTenant.Id);
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PublishAsync_InsideChangeScope_RealCurrentTenant_CapturesOverriddenTenant()
    {
        var tenantId = Guid.NewGuid();
        var recorded = new List<TenantRecordingHandler>();
        using var provider = BuildProvider(svc => svc.AddScoped<IEventHandler<ScopedTenantEvent>>(sp =>
        {
            var h = new TenantRecordingHandler(sp.GetRequiredService<ICurrentTenant>());
            recorded.Add(h);
            return h;
        }));
        var bus = new LocalEventBus(provider, provider.GetRequiredService<ILogger<LocalEventBus>>());

        var @event = new ScopedTenantEvent();
        using (var callerScope = provider.CreateScope())
        {
            var callerTenant = callerScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using (callerTenant.Change(tenantId))
            {
                await bus.PublishAsync(@event);
            }
        }

        Assert.Equal(tenantId, @event.TenantId);
        var invoked = Assert.Single(recorded);
        Assert.True(invoked.Invoked);
        Assert.Equal(tenantId, invoked.SeenTenantId);
    }

    [Fact]
    public async Task BackgroundHandler_InsideChangeScope_RealCurrentTenant_SeesTenant()
    {
        var tenantId = Guid.NewGuid();
        var signal = new BackgroundSignal();
        using var provider = BuildProvider(svc =>
        {
            svc.AddSingleton(signal);
            svc.AddScoped<IEventHandler<ScopedTenantEvent>, BackgroundTenantRecordingHandler>();
        });
        var bus = new LocalEventBus(provider, provider.GetRequiredService<ILogger<LocalEventBus>>());

        using (var callerScope = provider.CreateScope())
        {
            var callerTenant = callerScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using (callerTenant.Change(tenantId))
            {
                await bus.PublishAsync(new ScopedTenantEvent());
            }
        }

        var seen = await signal.Tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(tenantId, seen);
    }

    [Fact]
    public async Task PublishAsync_AfterChangeScopeDisposed_TenantNotLeaked()
    {
        // Change 的覆盖只活在 using 里：作用域释放后再发布，事件不得带上已经退出的租户。
        var tenantId = Guid.NewGuid();
        var handler = new TenantRecordingHandler(new CurrentTenant());
        using var provider = BuildProvider(svc => svc.AddScoped<IEventHandler<ScopedTenantEvent>>(_ => handler));
        var bus = new LocalEventBus(provider, provider.GetRequiredService<ILogger<LocalEventBus>>());

        using var callerScope = provider.CreateScope();
        var callerTenant = callerScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (callerTenant.Change(tenantId))
        {
        }

        var @event = new ScopedTenantEvent();
        await bus.PublishAsync(@event);

        Assert.Null(@event.TenantId);
    }

    [Fact]
    public void Change_IsVisibleToAnotherInstanceInTheSameAsyncFlow_AndRestoresOnDispose()
    {
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();
        var a = new CurrentTenant();
        var b = new CurrentTenant();

        using (a.Change(outer))
        {
            Assert.Equal(outer, b.Id);
            using (b.Change(inner))
            {
                Assert.Equal(inner, a.Id);
            }
            Assert.Equal(outer, b.Id);
        }

        Assert.Null(a.Id);
        Assert.Null(b.Id);
    }

    [Fact]
    public async Task Change_DoesNotLeakAcrossIndependentAsyncFlows()
    {
        // AsyncLocal 只沿父→子流动，两个并行分支互不可见（多租户后台服务按租户轮转时的隔离前提）。
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var tenant = new CurrentTenant();

        var first = Task.Run(async () =>
        {
            using (tenant.Change(t1))
            {
                await Task.Delay(30);
                return tenant.Id;
            }
        });
        var second = Task.Run(async () =>
        {
            using (tenant.Change(t2))
            {
                await Task.Delay(10);
                return tenant.Id;
            }
        });

        Assert.Equal(t1, await first);
        Assert.Equal(t2, await second);
        Assert.Null(tenant.Id);
    }
}
