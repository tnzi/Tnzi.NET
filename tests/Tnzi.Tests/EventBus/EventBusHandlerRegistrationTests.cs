using Microsoft.Extensions.Configuration;

namespace Tnzi.Tests.EventBus;

/// <summary>
/// 自动注册与手动注册并存：同一个处理器每次事件只执行一次。
/// </summary>
/// <remarks>
/// 自动注册默认开启，且 <see cref="EventBusModule"/>（LoadOrder 10）先于应用模块执行 <c>ConfigureServicesAsync</c>，
/// 扫描那一刻看不见应用模块随后写的 <c>AddEventHandler</c>。两条描述符并存时同一个处理器每次事件执行两遍
/// （重复发信、重复扣减），没有任何告警。这里按真实的生命周期顺序驱动模块：Pre → Configure → 应用模块注册 → Post。
/// </remarks>
public class EventBusHandlerRegistrationTests
{
    public class RegistrationProbeEvent : EventBase
    {
        public int Calls;
    }

    public class RegistrationProbeHandler : IEventHandler<RegistrationProbeEvent>
    {
        public Task HandleAsync(RegistrationProbeEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref @event.Calls);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ManualAddEventHandlerAfterAutoScan_RunsOncePerEvent_AndKeepsTheManualLifetime()
    {
        var (module, context) = await ConfigureEventBusModuleAsync();

        // 应用模块按文档在 ConfigureServicesAsync 里手动注册（并显式指定生命周期）
        context.Services.AddEventHandler<RegistrationProbeEvent, RegistrationProbeHandler>(ServiceLifetime.Singleton);

        await module.PostConfigureServicesAsync(context);

        var descriptor = Assert.Single(HandlerDescriptors(context.Services));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(1, await PublishOnceAsync(context.Services));
    }

    [Fact]
    public async Task ManualPlainServiceRegistrationAfterAutoScan_RunsOncePerEvent()
    {
        var (module, context) = await ConfigureEventBusModuleAsync();

        // 不经 AddEventHandler、直接按服务注册的写法同样会和自动注册撞上
        context.Services.AddTransient<IEventHandler<RegistrationProbeEvent>, RegistrationProbeHandler>();

        await module.PostConfigureServicesAsync(context);

        var descriptor = Assert.Single(HandlerDescriptors(context.Services));
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
        Assert.Equal(1, await PublishOnceAsync(context.Services));
    }

    [Fact]
    public async Task AutoScanAlone_StillRegistersTheHandler()
    {
        var (module, context) = await ConfigureEventBusModuleAsync();

        await module.PostConfigureServicesAsync(context);

        Assert.Single(HandlerDescriptors(context.Services));
        Assert.Equal(1, await PublishOnceAsync(context.Services));
    }

    [Fact]
    public async Task RemovingAnAutoRegisteredHandlerInConfigureServices_StaysRemoved()
    {
        // 自动注册不能挪到 PostConfigure 再做：那会把应用模块在 ConfigureServices 里移除 / 替换掉的处理器重新加回来
        var (module, context) = await ConfigureEventBusModuleAsync();

        context.Services.RemoveEventHandler<RegistrationProbeEvent, RegistrationProbeHandler>();

        await module.PostConfigureServicesAsync(context);

        Assert.Empty(HandlerDescriptors(context.Services));
        Assert.Equal(0, await PublishOnceAsync(context.Services));
    }

    private static async Task<(EventBusModule Module, ServiceConfigurationContext Context)> ConfigureEventBusModuleAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EventBus:AutoRegisterHandlers"] = "true",
                ["EventBus:HandlerAssemblies:0"] = typeof(EventBusHandlerRegistrationTests).Assembly.GetName().Name,
            })
            .Build();

        var module = new EventBusModule();
        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        return (module, context);
    }

    private static List<ServiceDescriptor> HandlerDescriptors(IServiceCollection services) => services
        .Where(d => d.ServiceType == typeof(IEventHandler<RegistrationProbeEvent>))
        .ToList();

    private static async Task<int> PublishOnceAsync(IServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();
        var @event = new RegistrationProbeEvent();

        await provider.GetRequiredService<IEventBus>().PublishAsync(@event);

        return @event.Calls;
    }
}
