using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EventBus;
using Tnzi.Kafka.Options;

namespace Tnzi.Kafka.Tests;

/// <summary>
/// 消费循环耗尽重连预算而退出后，实例必须报成不就绪，且这个事件类型必须能被再次订阅救活。
/// </summary>
/// <remarks>
/// 此前退出后登记表里留着一条死掉的登记：任何一次 <c>SubscribeEvent</c> 都成为 no-op（「already subscribed」），
/// 而探针只问集群元数据，对「本进程某个事件类型已停止消费」一无所知 —— 一个不再消费的实例继续被编排器当作就绪。
/// <c>MaxReconnectAttempts = 0</c>（文档语义：禁用重连）让传输异常第一次出现就退出，用例据此免去退避等待。
/// </remarks>
public class StoppedConsumerTests
{
    private sealed class ProbeEvent : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";
    }

    private static (KafkaEventBus Bus, ServiceProvider Provider, Func<int> FactoryCalls) CreateBusWhoseConsumerDies()
    {
        // 第一个消费者一拉就死（不是 ConsumeException：模拟句柄被关掉这类传输级故障，走外层重连处理器）；
        // 之后建出来的消费者是健康的（拉不到消息），救活后不再倒下
        var dying = new Mock<IConsumer<string, string>>();
        dying.Setup(c => c.Consume(It.IsAny<TimeSpan>())).Throws(new ObjectDisposedException("handle"));
        var healthy = new Mock<IConsumer<string, string>>();
        healthy.Setup(c => c.Consume(It.IsAny<TimeSpan>())).Returns((ConsumeResult<string, string>)null!);

        var producer = new Mock<IProducer<string, string>>();
        var options = new KafkaOptions();
        options.Consumer.MaxReconnectAttempts = 0;

        var factoryCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(producer.Object);
        services.AddSingleton(sp => new KafkaEventBus(
            producer.Object,
            NullLogger<KafkaEventBus>.Instance,
            sp,
            options,
            "localhost:9092",
            new DistributedConsumerIdentity("tests", Guid.NewGuid()),
            consumerFactory: _ => Interlocked.Increment(ref factoryCalls) == 1 ? dying.Object : healthy.Object));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<KafkaEventBus>(), provider, () => Volatile.Read(ref factoryCalls));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        for (var waited = 0; waited < 5000; waited += 20)
        {
            if (condition()) return;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for {because}.");
    }

    [Fact]
    public async Task WhenTheLoopStops_TheProbeReportsNotReady()
    {
        var (bus, provider, _) = CreateBusWhoseConsumerDies();
        await using var _ = bus;
        await using var __ = provider;

        bus.SubscribeEvent(typeof(ProbeEvent));
        await WaitUntilAsync(() => bus.StoppedConsumers.Count > 0, "the consumer loop to give up");

        var health = await new KafkaHealthProbe(provider).CheckAsync();

        health.IsConnected.ShouldBeFalse("集群可达不等于本进程还在消费");
        health.Detail!.ShouldContain(typeof(ProbeEvent).FullName!);
    }

    [Fact]
    public async Task WhenTheLoopStops_ASecondSubscribeRevivesTheEventType()
    {
        var (bus, provider, factoryCalls) = CreateBusWhoseConsumerDies();
        await using var _ = bus;
        await using var __ = provider;

        bus.SubscribeEvent(typeof(ProbeEvent));
        await WaitUntilAsync(() => bus.StoppedConsumers.Count > 0, "the consumer loop to give up");

        bus.SubscribeEvent(typeof(ProbeEvent));

        // 修复前这里是「already subscribed」no-op：死掉的登记留在表里，这个事件类型再也没有人能救活
        factoryCalls().ShouldBe(2, "第二次订阅必须真的建出一个新消费者");
        bus.StoppedConsumers.ShouldBeEmpty();
    }
}

/// <summary>
/// <c>EnableAutoCommit = true</c> 会架空整套失败处置（重试期间不提交 / 保留偏移量等重投 / 毒消息卡住分区），
/// 而症状是静默丢消息：必须在启动期就拒绝。
/// </summary>
public class KafkaAutoCommitValidationTests
{
    [Fact]
    public void Validator_RejectsEnableAutoCommit()
    {
        var options = new KafkaOptions();
        options.Consumer.EnableAutoCommit = true;

        var result = new KafkaOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("EnableAutoCommit");
    }

    [Fact]
    public void Validator_AcceptsTheDefaults()
    {
        var result = new KafkaOptionsValidator().Validate(null, new KafkaOptions());

        result.Succeeded.ShouldBeTrue(result.FailureMessage);
    }
}
