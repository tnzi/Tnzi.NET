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

    /// <summary>声明过的队列及其形态（工作队列 vs 广播队列的差别全在这几个布尔上）。</summary>
    private readonly List<DeclaredQueue> _declaredQueues = [];

    private sealed record DeclaredQueue(string Name, bool Durable, bool Exclusive, bool AutoDelete, IDictionary<string, object?>? Arguments);

    private const string ConsumerGroup = "tests";

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
            .Callback<string, bool, bool, bool, IDictionary<string, object?>?, bool, bool, CancellationToken>(
                (queue, durable, exclusive, autoDelete, arguments, _, _, _) =>
                    _declaredQueues.Add(new DeclaredQueue(queue, durable, exclusive, autoDelete, arguments)))
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

        var expectedQueue = $"{ConsumerGroup}.{typeof(ProbeIntegrationEvent).FullName}";

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

        _consumedQueues.ShouldContain($"{ConsumerGroup}.{typeof(ProbeIntegrationEvent).FullName}");

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

        // 队列名以 EventBus:ConsumerGroup 开头：模块必须把配置里的组名交给总线
        _consumedQueues.ShouldContain($"tests-app.{typeof(ProbeIntegrationEvent).FullName}");
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

    #region 消费者身份

    /// <summary>
    /// 两个消费者组（两个服务）对同一个事件各得一条队列，都绑定到同一个路由键。
    /// </summary>
    /// <remarks>
    /// <b>被保护的缺陷</b>：队列名此前只由事件类型决定。订单服务与通知服务都处理
    /// <c>OrderCreated</c> 时共用一条持久队列，代理在两者之间<b>分发</b>而不是各投一份 ——
    /// 各自只收到约一半的事件，没有异常、没有 basic.return、没有任何症状。
    /// </remarks>
    [Fact]
    public async Task TwoConsumerGroups_DeclareDistinctQueuesForTheSameEvent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        var orders = CreateBus(_serviceProvider, new DistributedConsumerIdentity("orders"));
        var notifications = CreateBus(_serviceProvider, new DistributedConsumerIdentity("notifications"));

        await orders.SubscribeEventAsync(typeof(ProbeIntegrationEvent));
        await notifications.SubscribeEventAsync(typeof(ProbeIntegrationEvent));

        var routingKey = typeof(ProbeIntegrationEvent).FullName!;
        var boundQueues = _bindings.Where(b => b.RoutingKey == routingKey).Select(b => b.Queue).Distinct().ToList();

        boundQueues.ShouldBe([$"orders.{routingKey}", $"notifications.{routingKey}"], ignoreOrder: true);
        _consumedQueues.ShouldBe([$"orders.{routingKey}", $"notifications.{routingKey}"], ignoreOrder: true);
    }

    /// <summary>
    /// 同一个组的两个实例声明的是同一条工作队列（代理在实例间分发，这是工作队列该有的形状）。
    /// </summary>
    [Fact]
    public async Task TwoInstancesOfOneConsumerGroup_ShareTheWorkQueue()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        var instanceA = CreateBus(_serviceProvider, new DistributedConsumerIdentity("orders", Guid.NewGuid()));
        var instanceB = CreateBus(_serviceProvider, new DistributedConsumerIdentity("orders", Guid.NewGuid()));

        await instanceA.SubscribeEventAsync(typeof(ProbeIntegrationEvent));
        await instanceB.SubscribeEventAsync(typeof(ProbeIntegrationEvent));

        _consumedQueues.Distinct().ShouldHaveSingleItem().ShouldBe($"orders.{typeof(ProbeIntegrationEvent).FullName}");
    }

    /// <summary>
    /// 广播事件：每个实例一条独占、自动删除、不持久的队列，且不挂死信参数。
    /// </summary>
    /// <remarks>
    /// 多实例配置广播就是受害者：每个实例都要 reload 自己的缓存，而共用一条队列时代理只投给其中一个 ——
    /// 1/3 概率回到发布实例（处理器按 OriginInstanceId 直接 return，等于哪都没应用）。
    /// 不挂死信：一条过期的广播重放出来是有害的。
    /// </remarks>
    [Fact]
    public async Task BroadcastEvent_DeclaresAnExclusiveAutoDeletePerInstanceQueue()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        var instanceId = Guid.NewGuid();
        var bus = CreateBus(_serviceProvider, new DistributedConsumerIdentity("orders", instanceId));

        await bus.SubscribeEventAsync(typeof(ProbeBroadcastEvent));

        var routingKey = typeof(ProbeBroadcastEvent).FullName!;
        var expectedQueue = $"orders.{routingKey}.{instanceId:N}";

        var queue = _declaredQueues.ShouldHaveSingleItem("广播订阅只声明自己那条队列，没有死信队列");
        queue.Name.ShouldBe(expectedQueue);
        queue.Exclusive.ShouldBeTrue("独占：这条队列只属于本连接，连接断开即消失");
        queue.AutoDelete.ShouldBeTrue("自动删除：实例下线后不能留下一条永远没人消费的队列");
        queue.Durable.ShouldBeFalse("不持久：实例重启是一个新实例，旧队列里的广播没有意义");
        (queue.Arguments == null || !queue.Arguments.ContainsKey("x-dead-letter-exchange"))
            .ShouldBeTrue("广播不进死信：过期的广播重放出来是有害的");

        _bindings.ShouldContain(b => b.Queue == expectedQueue && b.RoutingKey == routingKey);
        _consumedQueues.ShouldBe([expectedQueue]);
    }

    /// <summary>
    /// 两个实例各自的广播队列名不同 —— 否则又回到了竞争消费。
    /// </summary>
    [Fact]
    public async Task BroadcastEvent_QueueNameDiffersPerInstance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        var instanceA = CreateBus(_serviceProvider, new DistributedConsumerIdentity("orders", Guid.NewGuid()));
        var instanceB = CreateBus(_serviceProvider, new DistributedConsumerIdentity("orders", Guid.NewGuid()));

        await instanceA.SubscribeEventAsync(typeof(ProbeBroadcastEvent));
        await instanceB.SubscribeEventAsync(typeof(ProbeBroadcastEvent));

        _consumedQueues.Count.ShouldBe(2);
        _consumedQueues.Distinct().Count().ShouldBe(2, "两个实例共用一条广播队列 = 只有一个实例收到");
    }

    /// <summary>
    /// 工作队列仍然持久、非独占，并挂着死信参数（at-least-once 那一套没有被广播改动波及）。
    /// </summary>
    [Fact]
    public async Task WorkQueue_StaysDurableWithDeadLettering()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        var bus = CreateBus(_serviceProvider, new DistributedConsumerIdentity("orders"));

        await bus.SubscribeEventAsync(typeof(ProbeIntegrationEvent));

        var routingKey = typeof(ProbeIntegrationEvent).FullName!;
        var main = _declaredQueues.Single(q => q.Name == $"orders.{routingKey}");
        main.Durable.ShouldBeTrue();
        main.Exclusive.ShouldBeFalse();
        main.AutoDelete.ShouldBeFalse();
        main.Arguments.ShouldNotBeNull().ShouldContainKey("x-dead-letter-exchange");

        _declaredQueues.ShouldContain(q => q.Name == $"orders.DeadLetter.{routingKey}");
    }

    /// <summary>
    /// 死信按<b>队列名</b>路由，死信队列也按队列名绑定：死信交换机是各组共享的 topic，
    /// 按事件名路由会让 A 组的死信同时落进 B 组的死信队列。
    /// </summary>
    /// <remarks>
    /// 09-12 把路由键从事件名改成了队列名，但没有测试守着它 —— 把两处改回 <c>eventTypeName</c> 套件照绿。
    /// </remarks>
    [Fact]
    public async Task WorkQueue_DeadLettersByQueueName_NotByEventName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        var bus = CreateBus(_serviceProvider, new DistributedConsumerIdentity("orders"));

        await bus.SubscribeEventAsync(typeof(ProbeIntegrationEvent));

        var eventName = typeof(ProbeIntegrationEvent).FullName!;
        var queueName = $"orders.{eventName}";
        var deadLetterQueueName = $"orders.DeadLetter.{eventName}";

        var main = _declaredQueues.Single(q => q.Name == queueName);
        main.Arguments.ShouldNotBeNull()["x-dead-letter-routing-key"].ShouldBe(queueName, "死信路由键必须是本队列名，不是事件名");

        _bindings.ShouldContain(b => b.Queue == deadLetterQueueName && b.RoutingKey == queueName,
            "死信队列按本队列名绑定到死信交换机");
        _bindings.ShouldNotContain(b => b.Queue == deadLetterQueueName && b.RoutingKey == eventName,
            "按事件名绑定会收下每一个组的死信");
    }

    #endregion

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
                ["EventBus:ConsumerGroup"] = "tests-app",
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

    private RabbitMQEventBus CreateBus(IServiceProvider serviceProvider, DistributedConsumerIdentity? identity = null) => new(
        _mockConnection.Object,
        NullLogger<RabbitMQEventBus>.Instance,
        serviceProvider,
        new RabbitMQOptions(),
        consumerIdentity: identity ?? new DistributedConsumerIdentity(ConsumerGroup));

    public class ProbeIntegrationEvent : EventBase, IIntegrationEvent
    {
        public string SourceService { get; set; } = "tests";
    }

    public class ProbeLocalEvent : EventBase;

    public class ProbeBroadcastEvent : EventBase, IBroadcastIntegrationEvent
    {
        public string SourceService { get; set; } = "tests";
    }

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
