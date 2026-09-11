using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EventBus;

using Tnzi.Modules;

namespace Tnzi.Kafka.Tests;

/// <summary>
/// 消费侧到底有没有被接上：注册了处理器的集成事件，启动后应当真的被订阅。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：<c>SubscribeEvent&lt;TEvent&gt;()</c> 在整个 <c>src/</c> 里<b>零调用方</b>，
/// 模块也不注册任何 <c>IHostedService</c>。Kafka 这边消息留在主题里不会丢，但<b>没有任何人消费</b>，
/// 而文档白纸黑字写着「消费端事件处理器无需修改」。
/// </para>
/// <para>
/// ★ 这里刻意不去真的建 Kafka 消费者（那会连一个不存在的代理并起后台线程）：
/// 「按发现的类型逐个订阅」这段逻辑与 RabbitMQ 侧是<b>同一个类</b>
/// （<see cref="DistributedEventSubscriptionInitializer"/>），真实传输路径由
/// <c>Tnzi.RabbitMQ.Tests.AutomaticSubscriptionTests</c> 用 mock 代理端到端覆盖。
/// 本文件守的是 Kafka 侧的<b>接线</b>与<b>契约</b>：两者缺一，那段共用逻辑就永远不会被执行到。
/// </para>
/// </remarks>
public class AutomaticSubscriptionTests
{
    /// <summary>
    /// 模块必须把启动器接进 DI。
    /// </summary>
    /// <remarks>
    /// ★ 删掉模块里那行注册，其它测试一条都不会红 —— 这正是缺陷此前活了那么久的原因。
    /// </remarks>
    [Fact]
    public async Task TheModule_RegistersTheSubscriptionInitializer()
    {
        using var provider = await BuildModuleProviderAsync();

        provider.GetServices<IHostedService>()
            .ShouldContain(s => s is DistributedEventSubscriptionInitializer,
                "没有这行注册，自动订阅永远不会发生，而一切看起来都正常");
    }

    /// <summary>
    /// 总线必须实现非泛型订阅面，否则上面那行注册会在解析时才炸。
    /// </summary>
    [Fact]
    public async Task TheEventBus_ImplementsTheSubscriberContract()
    {
        using var provider = await BuildModuleProviderAsync();

        provider.GetRequiredService<KafkaEventBus>().ShouldBeAssignableTo<IDistributedEventSubscriber>();
    }

    /// <summary>
    /// 只订阅注册了处理器的集成事件：本地领域事件不经过代理，不该占用主题与消费者组。
    /// </summary>
    [Fact]
    public async Task OnlyIntegrationEventsWithRegisteredHandlers_AreSubscribed()
    {
        var services = new ServiceCollection();
        services.AddEventHandler<ProbeIntegrationEvent, ProbeIntegrationEventHandler>();
        services.AddEventHandler<ProbeLocalEvent, ProbeLocalEventHandler>();

        var subscriber = new RecordingSubscriber();
        await CreateInitializer(services, subscriber).StartAsync(CancellationToken.None);

        subscriber.Subscribed.ShouldBe([typeof(ProbeIntegrationEvent)]);
    }

    /// <summary>
    /// 关掉自动订阅就什么都不订阅（纯生产者部署形态）。
    /// </summary>
    [Fact]
    public async Task WhenAutoSubscribeIsDisabled_NothingIsSubscribed()
    {
        var services = new ServiceCollection();
        services.AddEventHandler<ProbeIntegrationEvent, ProbeIntegrationEventHandler>();

        var subscriber = new RecordingSubscriber();
        await CreateInitializer(services, subscriber, enabled: false).StartAsync(CancellationToken.None);

        subscriber.Subscribed.ShouldBeEmpty();
    }

    /// <summary>
    /// <c>AutoSubscribe</c> 配置项要真的走到启动器，而不是只在选项类里躺着。
    /// </summary>
    /// <remarks>
    /// 用「关掉」这一侧验证接线：打开那一侧会去建真实的 Kafka 消费者，
    /// 在没有代理的测试环境里只会起一堆后台重连线程。
    /// </remarks>
    [Fact]
    public async Task TheAutoSubscribeSetting_ReachesTheInitializer()
    {
        using var provider = await BuildModuleProviderAsync(autoSubscribe: false);

        var initializer = provider.GetServices<IHostedService>()
            .OfType<DistributedEventSubscriptionInitializer>()
            .ShouldHaveSingleItem();

        // 关掉之后即使注册了处理器也不会去建消费者，因此这一步在没有代理的环境里是安全的
        await initializer.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// 模块必须注册连通性探针（与 RabbitMQ 模块同形）。
    /// </summary>
    [Fact]
    public async Task TheModule_RegistersTheConnectivityProbe()
    {
        using var provider = await BuildModuleProviderAsync();

        provider.GetServices<IDistributedEventBusHealthProbe>().ShouldContain(p => p is KafkaHealthProbe);
    }

    #region Harness

    private static async Task<ServiceProvider> BuildModuleProviderAsync(bool autoSubscribe = true)
    {
        var module = new KafkaEventBusModule();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventHandler<ProbeIntegrationEvent, ProbeIntegrationEventHandler>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EventBus:Type"] = "Kafka",
                ["EventBus:KafkaBootstrapServers"] = "localhost:9092",
                ["Kafka:AutoSubscribe"] = autoSubscribe ? "true" : "false",
            })
            .Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        // 生产者换成 mock：真建一个会去连不存在的代理并起后台线程
        services.RemoveAll<IProducer<string, string>>();
        services.AddSingleton(new Mock<IProducer<string, string>>().Object);

        return services.BuildServiceProvider();
    }

    private static DistributedEventSubscriptionInitializer CreateInitializer(
        IServiceCollection services,
        IDistributedEventSubscriber subscriber,
        bool enabled = true)
        => new(
            services,
            subscriber,
            NullLogger<DistributedEventSubscriptionInitializer>.Instance,
            "Kafka",
            enabled);

    private sealed class RecordingSubscriber : IDistributedEventSubscriber
    {
        public List<Type> Subscribed { get; } = [];

        public Task SubscribeEventAsync(Type eventType, CancellationToken cancellationToken = default)
        {
            Subscribed.Add(eventType);
            return Task.CompletedTask;
        }
    }

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

    private sealed class ProbeLocalEventHandler : IEventHandler<ProbeLocalEvent>
    {
        public Task HandleAsync(ProbeLocalEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    #endregion
}
