using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EventBus;
using Tnzi.Kafka.Options;
using Tnzi.Modules;

namespace Tnzi.Kafka.Tests;

/// <summary>
/// 消费者组必须带上「哪个服务」「哪个实例」，否则同一代理上的所有 Tnzi 进程都在同一个组里竞争消费。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：组名此前是 <c>{GroupIdPrefix}.{事件全名}</c>，不含任何应用或实例成分。
/// 主题由代理自动创建、通常只有一个分区，于是同一个组里永远只有一个成员收到消息：
/// 两个服务各自只收到一部分事件；框架自己的多实例配置广播（每个实例都得 reload）
/// 固定只到达一个成员 —— 那个成员若是发布实例，广播在任何实例上都不生效。
/// </para>
/// <para>
/// 这里不建真实消费者（那会连一个不存在的代理），只断言交给 Confluent 的 <see cref="ConsumerConfig"/>：
/// 组名与偏移重置策略正是这条缺陷的全部形态。命名逻辑与 RabbitMQ 侧共用核心的
/// <see cref="DistributedConsumerIdentity"/>。
/// </para>
/// </remarks>
public class ConsumerIdentityTests
{
    private static readonly KafkaOptions Options = new();

    [Fact]
    public void WorkGroup_IsNamedByPrefixConsumerGroupAndEvent()
    {
        var bus = CreateBus(new DistributedConsumerIdentity("orders"));

        var config = bus.BuildConsumerConfig(bus.ConsumerIdentity.Describe(typeof(ProbeIntegrationEvent)));

        config.GroupId.ShouldBe($"{Options.GroupIdPrefix}.orders.{typeof(ProbeIntegrationEvent).FullName}");
        config.AutoOffsetReset.ShouldBe(Options.Consumer.AutoOffsetReset, "工作组沿用配置的偏移重置策略");
    }

    [Fact]
    public void TwoConsumerGroups_GetDistinctGroupIdsForTheSameEvent()
    {
        var orders = CreateBus(new DistributedConsumerIdentity("orders"));
        var notifications = CreateBus(new DistributedConsumerIdentity("notifications"));

        var a = orders.BuildConsumerConfig(orders.ConsumerIdentity.Describe(typeof(ProbeIntegrationEvent))).GroupId;
        var b = notifications.BuildConsumerConfig(notifications.ConsumerIdentity.Describe(typeof(ProbeIntegrationEvent))).GroupId;

        a.ShouldNotBe(b, "同组 = 竞争消费，两个服务各自只收到一部分事件");
    }

    [Fact]
    public void TwoInstancesOfOneConsumerGroup_ShareTheWorkGroup()
    {
        var instanceA = CreateBus(new DistributedConsumerIdentity("orders", Guid.NewGuid()));
        var instanceB = CreateBus(new DistributedConsumerIdentity("orders", Guid.NewGuid()));

        var a = instanceA.BuildConsumerConfig(instanceA.ConsumerIdentity.Describe(typeof(ProbeIntegrationEvent))).GroupId;
        var b = instanceB.BuildConsumerConfig(instanceB.ConsumerIdentity.Describe(typeof(ProbeIntegrationEvent))).GroupId;

        a.ShouldBe(b, "工作队列语义：同一服务的实例共用一个组，代理在实例间分发");
    }

    /// <summary>
    /// 广播事件：每个实例自成一组（组名带实例 ID），且从 <c>Latest</c> 开始 —— 新组用 <c>Earliest</c>
    /// 会把主题里保留的全部历史广播重放一遍，而一条过期的广播重放出来是有害的。
    /// </summary>
    [Fact]
    public void BroadcastEvent_GroupIdIncludesTheInstanceId_AndStartsFromLatest()
    {
        var instanceId = Guid.NewGuid();
        var bus = CreateBus(new DistributedConsumerIdentity("orders", instanceId));

        var config = bus.BuildConsumerConfig(bus.ConsumerIdentity.Describe(typeof(ProbeBroadcastEvent)));

        config.GroupId.ShouldBe($"{Options.GroupIdPrefix}.orders.{typeof(ProbeBroadcastEvent).FullName}.{instanceId:N}");
        config.AutoOffsetReset.ShouldBe(AutoOffsetReset.Latest);
    }

    [Fact]
    public void BroadcastEvent_GroupIdDiffersPerInstance()
    {
        var instanceA = CreateBus(new DistributedConsumerIdentity("orders", Guid.NewGuid()));
        var instanceB = CreateBus(new DistributedConsumerIdentity("orders", Guid.NewGuid()));

        var a = instanceA.BuildConsumerConfig(instanceA.ConsumerIdentity.Describe(typeof(ProbeBroadcastEvent))).GroupId;
        var b = instanceB.BuildConsumerConfig(instanceB.ConsumerIdentity.Describe(typeof(ProbeBroadcastEvent))).GroupId;

        a.ShouldNotBe(b, "两个实例同组 = 只有一个实例收到广播");
    }

    /// <summary>
    /// 模块必须把 <c>EventBus:ConsumerGroup</c> 交给总线，否则上面的命名逻辑永远拿的是默认值。
    /// </summary>
    [Fact]
    public async Task TheModule_HandsTheConfiguredConsumerGroupToTheBus()
    {
        var module = new KafkaEventBusModule();
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EventBus:Type"] = "Kafka",
                ["EventBus:KafkaBootstrapServers"] = "localhost:9092",
                ["EventBus:ConsumerGroup"] = "tests-app",
            })
            .Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        services.RemoveAll<IProducer<string, string>>();
        services.AddSingleton(new Mock<IProducer<string, string>>().Object);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<KafkaEventBus>().ConsumerIdentity.ConsumerGroup.ShouldBe("tests-app");
    }

    private static KafkaEventBus CreateBus(DistributedConsumerIdentity identity) => new(
        new Mock<IProducer<string, string>>().Object,
        NullLogger<KafkaEventBus>.Instance,
        new ServiceCollection().BuildServiceProvider(),
        Options,
        "localhost:9092",
        identity);

    public class ProbeIntegrationEvent : EventBase, IIntegrationEvent
    {
        public string SourceService { get; set; } = "tests";
    }

    public class ProbeBroadcastEvent : EventBase, IBroadcastIntegrationEvent
    {
        public string SourceService { get; set; } = "tests";
    }
}
