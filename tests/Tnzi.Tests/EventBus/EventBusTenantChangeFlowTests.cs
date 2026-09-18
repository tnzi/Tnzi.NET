using Tnzi.MultiTenancy;

namespace Tnzi.Tests.EventBus;

/// <summary>
/// <see cref="ICurrentTenant.Change"/> 写下的覆盖必须跟着异步流走到事件处理器所在的新 scope。
/// </summary>
/// <remarks>
/// <c>CurrentTenant</c> 按 scoped 注册，而 <c>LocalEventBus</c> 给每次发布新开一个 scope 来解析处理器。
/// 覆盖若挂在实例字段上，新 scope 里的那个实例什么都不知道：匿名回调 / 后台扫描里
/// <c>Change(tenantId)</c> 之后发出去的事件，同步处理器仍在「无租户」下执行，
/// 多租户开启时它在 <c>TenantId IS NULL</c> 的过滤器下找不到要推进的那条记录，且没有任何报错。
/// </remarks>
public class EventBusTenantChangeFlowTests
{
    public class FlowEvent : EventBase
    {
    }

    /// <summary>记录处理器执行时所在 scope 解析到的当前租户。</summary>
    public class TenantObservingHandler : IEventHandler<FlowEvent>
    {
        private readonly ICurrentTenant _currentTenant;
        private readonly Observed _observed;

        public TenantObservingHandler(ICurrentTenant currentTenant, Observed observed)
        {
            _currentTenant = currentTenant;
            _observed = observed;
        }

        public Task HandleAsync(FlowEvent @event, CancellationToken cancellationToken = default)
        {
            _observed.TenantId = _currentTenant.Id;
            _observed.Handled = true;
            return Task.CompletedTask;
        }
    }

    public class Observed
    {
        public bool Handled { get; set; }
        public Guid? TenantId { get; set; }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<Observed>();
        services.AddScoped<IEventHandler<FlowEvent>, TenantObservingHandler>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Change_MadeByThePublisher_IsSeenByAHandlerResolvedInAFreshScope()
    {
        using var sp = BuildProvider();
        var bus = new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>());
        var observed = sp.GetRequiredService<Observed>();
        var tenantId = Guid.NewGuid();

        using (var publisherScope = sp.CreateScope())
        {
            var publisherTenant = publisherScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var @event = new FlowEvent();

            using (publisherTenant.Change(tenantId))
            {
                await bus.PublishAsync(@event);
            }

            Assert.True(observed.Handled);
            Assert.Equal(tenantId, observed.TenantId);
            // 总线在新 scope 里捕获的租户也必须是覆盖后的那个
            Assert.Equal(tenantId, @event.TenantId);
        }
    }

    [Fact]
    public async Task Change_IsRestoredWhenTheScopeEnds_AndDoesNotLeakIntoALaterPublish()
    {
        using var sp = BuildProvider();
        var bus = new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>());
        var observed = sp.GetRequiredService<Observed>();

        using var publisherScope = sp.CreateScope();
        var publisherTenant = publisherScope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        using (publisherTenant.Change(Guid.NewGuid()))
        {
            await bus.PublishAsync(new FlowEvent());
        }

        observed.TenantId = null;
        observed.Handled = false;

        await bus.PublishAsync(new FlowEvent());

        Assert.True(observed.Handled);
        Assert.Null(observed.TenantId);
        Assert.Null(publisherTenant.Id);
    }

    /// <summary>并行的按租户任务各走各的流，互不串味。</summary>
    [Fact]
    public async Task ParallelChanges_OnSeparateAsyncFlows_DoNotBleedIntoEachOther()
    {
        using var sp = BuildProvider();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        async Task<Guid?> ObserveAsync(Guid tenantId)
        {
            using var scope = sp.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using (tenant.Change(tenantId))
            {
                await Task.Delay(20);
                using var inner = sp.CreateScope();
                return inner.ServiceProvider.GetRequiredService<ICurrentTenant>().Id;
            }
        }

        var results = await Task.WhenAll(ObserveAsync(tenantA), ObserveAsync(tenantB));

        Assert.Equal(tenantA, results[0]);
        Assert.Equal(tenantB, results[1]);
        using var outer = sp.CreateScope();
        Assert.Null(outer.ServiceProvider.GetRequiredService<ICurrentTenant>().Id);
    }
}
