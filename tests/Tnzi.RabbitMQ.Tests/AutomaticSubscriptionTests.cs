using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Tnzi.EventBus;
using Tnzi.Modules;
using Tnzi.RabbitMQ.Options;

namespace Tnzi.RabbitMQ.Tests;

/// <summary>
/// 消费侧到底有没有被接上：注册了处理器的集成事件，启动后应当真的有队列绑定和消费者。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：<c>SubscribeEventAsync</c> 在整个 <c>src/</c> 里<b>一个调用方都没有</b>
/// （唯一调用点在测试里），模块也不注册任何 <c>IHostedService</c>。于是按文档配好 RabbitMQ、
/// 注册好 <c>IEventHandler&lt;T&gt;</c> 之后，交换机上没有任何队列绑定：发出去的消息被代理
/// <b>直接丢弃</b>，处理器永不执行，日志里照常一行 "Published…"。08-08 加固的那整套
/// 重试 / 死信 / 三态确认一行都不会执行 —— 机制建好了，没接上。
/// </para>
/// <para>
/// ★ 同一形态还有第二处：框架自己唯一的分布式订阅点（多实例配置变更广播）走的是
/// <c>IDistributedEventBus.Subscribe&lt;TEvent, THandler&gt;()</c>，而那个方法此前只打一行
/// "not supported" 就返回。调用方拿到成功返回，那条链路从来没工作过。
/// </para>
/// </remarks>
public class AutomaticSubscriptionTests : IDisposable
{
    private readonly Mock<IConnection> _mockConnection = new();
    private readonly Mock<IChannel> _mockChannel = new();

    /// <summary>被真正绑定到交换机上的队列（队列名 → 路由键）。空的就等于"消息会被丢弃"。</summary>
    private readonly List<(string Queue, string RoutingKey)> _bindings = [];

    /// <summary>真正开始消费的队列。</summary>
    private readonly List<string> _consumedQueues = [];

    private ServiceProvider? _serviceProvider;

    public AutomaticSubscriptionTests()
    {
        _mockChannel.Setup(c => c.IsOpen).Returns(true);

        _mockConnection
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockChannel.Object);

        _mockChannel.Setup(c => c.ExchangeDeclareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockChannel.Setup(c => c.QueueDeclareAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("q", 0, 0));

        _mockChannel.Setup(c => c.QueueBindAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IDictionary<string, object?>?, bool, CancellationToken>(
                (queue, _, routingKey, _, _, _) => _bindings.Add((queue, routingKey)))
            .Returns(Task.CompletedTask);

        _mockChannel.Setup(c => c.BasicQosAsync(
                It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockChannel.Setup(c => c.BasicConsumeAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>?, IAsyncBasicConsumer, CancellationToken>(
                (queue, _, _, _, _, _, _, _) => _consumedQueues.Add(queue))
            .ReturnsAsync("consumer-tag");
    }

    public void Dispose() => _serviceProvider?.Dispose();

    /// <summary>
    /// 注册了处理器的集成事件，启动后必须真的被绑定并开始消费。
    /// </summary>
    [Fact]
    public async Task AnIntegrationEventWithARegisteredHandler_IsBoundAndConsumedOnStartup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventHandler<ProbeIntegrationEvent, ProbeIntegrationEventHandler>();

        await StartInitializerAsync(services);

        var expectedQueue = $"Tnzi.Events.{typeof(ProbeIntegrationEvent).FullName}";

        _bindings.ShouldContain(
            b => b.Queue == expectedQueue && b.RoutingKey == typeof(ProbeIntegrationEvent).FullName,
            "没有队列绑定到交换机 = 发出去的消息被代理直接丢弃，且不会回 basic.return");

        _consumedQueues.ShouldContain(expectedQueue, "绑定了却不消费，消息只会堆在队列里");
    }

    /// <summary>
    /// 进程内领域事件不该在代理上建队列。
    /// </summary>
    /// <remarks>
    /// 它们只走本地总线，代理上永远不会出现这类消息。给每个本地处理器都建一条持久化队列，
    /// 只会在共享的代理上留下一堆永远收不到消息的队列，运维那边分辨不出哪些是真的坏了。
    /// </remarks>
    [Fact]
    public async Task ALocalDomainEventWithAHandler_IsNotSubscribed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventHandler<ProbeLocalEvent, ProbeLocalEventHandler>();

        await StartInitializerAsync(services);

        _consumedQueues.ShouldBeEmpty("本地领域事件不经过代理，不该占用队列");
    }

    /// <summary>
    /// 关掉自动订阅就什么都不订阅（纯生产者部署形态）。
    /// </summary>
    [Fact]
    public async Task WhenAutoSubscribeIsDisabled_NothingIsSubscribed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventHandler<ProbeIntegrationEvent, ProbeIntegrationEventHandler>();

        await StartInitializerAsync(services, enabled: false);

        _consumedQueues.ShouldBeEmpty();
    }

    /// <summary>
    /// 订阅面来自 DI 注册，而不是「程序集里存在这个处理器类型」。
    /// </summary>
    /// <remarks>
    /// 按类型扫描会把「写了但没注册」的处理器也算进来，结果是消息被拉下来、没人处理、
    /// 然后被确认 —— 又一次静默丢弃，而且比不消费更难发现。
    /// </remarks>
    [Fact]
    public async Task AnIntegrationEventWithoutARegisteredHandler_IsNotSubscribed()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        await StartInitializerAsync(services);

        _consumedQueues.ShouldBeEmpty();
    }

    /// <summary>
    /// 框架自己唯一的分布式订阅点：<c>Subscribe&lt;TEvent, THandler&gt;()</c> 必须真的开始消费。
    /// </summary>
    /// <remarks>
    /// 修复前它只打一行 "Runtime subscription is not supported" 就返回：
    /// 多实例配置变更广播因此从未工作过，而调用方看到的是一次成功返回。
    /// </remarks>
    [Fact]
    public async Task Subscribe_StartsConsumingRatherThanLoggingThatItIsUnsupported()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventHandler<ProbeIntegrationEvent, ProbeIntegrationEventHandler>();
        _serviceProvider = services.BuildServiceProvider();

        var bus = CreateBus(_serviceProvider);

        ((IDistributedEventBus)bus).Subscribe<ProbeIntegrationEvent, ProbeIntegrationEventHandler>();

        _consumedQueues.ShouldContain($"Tnzi.Events.{typeof(ProbeIntegrationEvent).FullName}");

        await Task.CompletedTask;
    }

    /// <summary>
    /// 同一个类型订阅两次不得建出第二个消费者。
    /// </summary>
    /// <remarks>
    /// 启动期自动订阅与应用自己调用 <c>Subscribe</c> 会同时到达同一个类型，这是常态不是异常。
    /// 建两个消费者意味着每条消息被处理两遍。
    /// </remarks>
    [Fact]
    public async Task SubscribingTheSameEventTwice_DoesNotCreateASecondConsumer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        var bus = CreateBus(_serviceProvider);

        await bus.SubscribeEventAsync(typeof(ProbeIntegrationEvent));
        await bus.SubscribeEventAsync(typeof(ProbeIntegrationEvent));

        _consumedQueues.Count.ShouldBe(1);
    }

    /// <summary>
    /// 模块必须把启动器接进 DI —— 否则上面所有断言都在测一个没人会创建的对象。
    /// </summary>
    /// <remarks>
    /// ★ 这条是「接线本身」的门禁：删掉模块里那行注册，本文件其它测试全都照绿。
    /// </remarks>
    [Fact]
    public async Task TheModule_RegistersTheSubscriptionInitializer()
    {
        var services = await ConfigureModuleAsync();

        // IConnection 换成 mock：真去连一个不存在的代理只会让这条测试变成一次超时
        services.RemoveAll<IConnection>();
        services.AddSingleton(_mockConnection.Object);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();

        _serviceProvider.GetServices<IHostedService>()
            .ShouldContain(s => s is DistributedEventSubscriptionInitializer,
                "没有这行注册，自动订阅永远不会发生，而一切看起来都正常");
    }

    /// <summary>
    /// 从模块配置到真正开始消费的整条线。
    /// </summary>
    [Fact]
    public async Task FromModuleConfiguration_ARegisteredHandlerEndsUpConsuming()
    {
        var services = await ConfigureModuleAsync();

        services.RemoveAll<IConnection>();
        services.AddSingleton(_mockConnection.Object);
        services.AddLogging();
        services.AddEventHandler<ProbeIntegrationEvent, ProbeIntegrationEventHandler>();

        _serviceProvider = services.BuildServiceProvider();

        foreach (var hostedService in _serviceProvider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        _consumedQueues.ShouldContain($"Tnzi.Events.{typeof(ProbeIntegrationEvent).FullName}");
    }

    /// <summary>
    /// 模块必须注册连通性探针，否则就绪探针对代理状态一无所知。
    /// </summary>
    /// <remarks>
    /// 健康检查此前注入 <c>IEventBus</c>，而总线分离后它永远是本地总线 —— 那个检查恒 Healthy。
    /// 现在检查改问探针，探针由传输模块自己注册；少了这行注册，检查只能报 Degraded，
    /// 代理宕机照样是 200。
    /// </remarks>
    [Fact]
    public async Task TheModule_RegistersTheConnectivityProbe()
    {
        var services = await ConfigureModuleAsync();

        services.ShouldContain(d => d.ServiceType == typeof(IDistributedEventBusHealthProbe));
    }

    #region Discovery

    /// <summary>
    /// 开放泛型注册没有具体事件类型可订阅，必须跳过而不是炸掉启动。
    /// </summary>
    [Fact]
    public void Discovery_SkipsOpenGenericHandlerRegistrations()
    {
        var services = new ServiceCollection();
        services.AddScoped(typeof(IEventHandler<>), typeof(OpenGenericHandler<>));

        var discovered = DistributedEventSubscriptionInitializer.DiscoverHandledIntegrationEventTypes(services);

        discovered.ShouldBeEmpty();
    }

    /// <summary>
    /// 同一个事件注册了多个处理器时只订阅一次。
    /// </summary>
    [Fact]
    public void Discovery_DeduplicatesEventTypesWithSeveralHandlers()
    {
        var services = new ServiceCollection();
        services.AddEventHandler<ProbeIntegrationEvent, ProbeIntegrationEventHandler>();
        services.AddEventHandler<ProbeIntegrationEvent, SecondProbeIntegrationEventHandler>();

        var discovered = DistributedEventSubscriptionInitializer.DiscoverHandledIntegrationEventTypes(services);

        discovered.ShouldBe([typeof(ProbeIntegrationEvent)]);
    }

    #endregion

    #region Harness

    private async Task<IServiceCollection> ConfigureModuleAsync()
    {
        var module = new RabbitMQEventBusModule();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EventBus:Type"] = "RabbitMQ",
                ["EventBus:RabbitMqConnectionString"] = "amqp://guest:guest@localhost:5672/",
            })
            .Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        return services;
    }

    private async Task StartInitializerAsync(IServiceCollection services, bool enabled = true)
    {
        _serviceProvider = services.BuildServiceProvider();

        var initializer = new DistributedEventSubscriptionInitializer(
            services,
            CreateBus(_serviceProvider),
            NullLogger<DistributedEventSubscriptionInitializer>.Instance,
            "RabbitMQ",
            enabled);

        await initializer.StartAsync(CancellationToken.None);
    }

    private RabbitMQEventBus CreateBus(IServiceProvider serviceProvider) => new(
        _mockConnection.Object,
        NullLogger<RabbitMQEventBus>.Instance,
        serviceProvider,
        new RabbitMQOptions());

    public class ProbeIntegrationEvent : EventBase, IIntegrationEvent
    {
        public string SourceService { get; set; } = "tests";
    }

    public class ProbeLocalEvent : EventBase;

    private sealed class ProbeIntegrationEventHandler : IEventHandler<ProbeIntegrationEvent>
    {
        public Task HandleAsync(ProbeIntegrationEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SecondProbeIntegrationEventHandler : IEventHandler<ProbeIntegrationEvent>
    {
        public Task HandleAsync(ProbeIntegrationEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ProbeLocalEventHandler : IEventHandler<ProbeLocalEvent>
    {
        public Task HandleAsync(ProbeLocalEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class OpenGenericHandler<TEvent> : IEventHandler<TEvent> where TEvent : class, IEvent
    {
        public Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    #endregion
}
