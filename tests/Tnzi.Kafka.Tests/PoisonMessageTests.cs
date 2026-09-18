using System.Text;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EventBus;
using Tnzi.Kafka.Options;

namespace Tnzi.Kafka.Tests;

/// <summary>
/// 反序列化不出来的消息是<b>毒消息</b>，不是连接故障。
/// </summary>
/// <remarks>
/// <para>
/// 此前 <c>JsonSerializer.Deserialize</c> 抛出的 <c>JsonException</c>（未知枚举值、类型不匹配、载荷截断）
/// 与 tombstone（<c>Value == null</c>）都不是 <c>ConsumeException</c>，会穿出内层循环落进外层的「重连」处理器：
/// 关闭并重建消费者（每轮触发一次消费组 rebalance）、从同一偏移量再读到同一条、再抛 —— 无限循环，
/// 毒消息永远进不了 DLQ，而 <c>/health/ready</c> 照报健康。<c>MaxReconnectAttempts = 0</c> 时第一条毒消息即永久停止消费。
/// </para>
/// <para>
/// 派发器在 Confluent 客户端里，这里经构造函数的消费者工厂缝换成 Moq 消费者：第一次 <c>Consume</c> 交出毒消息，之后返回 null。
/// </para>
/// </remarks>
public class PoisonMessageTests
{
    private static readonly string Topic = "Tnzi.Events." + typeof(ProbeEvent).FullName;

    public enum ProbeStatus { Known }

    public sealed class ProbeEvent : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProbeStatus Status { get; set; }
    }

    public sealed class ProbeBroadcastEvent : EventBase, IBroadcastIntegrationEvent
    {
        public string SourceService => "tests";
    }

    private sealed class Harness
    {
        public Mock<IProducer<string, string>> Producer { get; } = new();
        public Mock<IConsumer<string, string>> Consumer { get; } = new();
        public int ConsumerFactoryCalls;
        public KafkaEventBus Bus { get; }

        public Harness(KafkaOptions options, ConsumeResult<string, string> poison, ILogger<KafkaEventBus>? logger = null)
            : this(options, _ => null!, logger)
        {
            var delivered = 0;
            Consumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
                .Returns(() => Interlocked.Increment(ref delivered) == 1 ? poison : null!);
        }

        // script：第 n 次 Consume（从 1 数）交出哪条记录；返回 null 表示这一轮没有消息
        public Harness(KafkaOptions options, Func<int, ConsumeResult<string, string>?> script, ILogger<KafkaEventBus>? logger = null)
        {
            var delivered = 0;
            Consumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
                .Returns(() => script(Interlocked.Increment(ref delivered))!);

            Producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeliveryResult<string, string>());

            var services = new ServiceCollection();
            services.AddLogging();

            Bus = new KafkaEventBus(
                Producer.Object,
                logger ?? NullLogger<KafkaEventBus>.Instance,
                services.BuildServiceProvider(),
                options,
                "localhost:9092",
                new DistributedConsumerIdentity("tests", Guid.NewGuid()),
                consumerFactory: _ =>
                {
                    Interlocked.Increment(ref ConsumerFactoryCalls);
                    return Consumer.Object;
                });
        }
    }

    private static ConsumeResult<string, string> Record(string? value, string? topic = null, int partition = 0, long offset = 42) => new()
    {
        Topic = topic ?? Topic,
        Partition = new Partition(partition),
        Offset = new Offset(offset),
        Message = new Message<string, string> { Key = "k", Value = value! }
    };

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
    public async Task UnknownEnumValue_IsDeadLetteredAndCommitted_ConsumerIsNotRebuilt()
    {
        var poison = Record("{\"status\":\"NotAKnownValue\"}");
        var harness = new Harness(new KafkaOptions(), poison);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(ProbeEvent));
        await WaitUntilAsync(() => harness.Consumer.Invocations.Any(i => i.Method.Name == nameof(IConsumer<string, string>.Commit)), "the poison message to be committed");

        harness.Producer.Verify(p => p.ProduceAsync(
                Topic + ".dlq",
                It.Is<Message<string, string>>(m => HeaderOf(m, "x-dead-letter-reason") == "deserialization"),
                It.IsAny<CancellationToken>()),
            Times.Once, "毒消息必须进 DLQ，并标明是反序列化失败而不是处理器失败");
        harness.Consumer.Verify(c => c.Commit(poison), Times.Once);
        harness.ConsumerFactoryCalls.ShouldBe(1, "修复前它会落进外层重连处理器：关闭、重建、rebalance、再读同一条 —— 无限循环");
    }

    [Fact]
    public async Task TombstoneValue_IsDeadLetteredNotTreatedAsReconnect()
    {
        var poison = Record(null);
        var harness = new Harness(new KafkaOptions(), poison);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(ProbeEvent));
        await WaitUntilAsync(() => harness.Consumer.Invocations.Any(i => i.Method.Name == nameof(IConsumer<string, string>.Commit)), "the tombstone to be committed");

        harness.Producer.Verify(p => p.ProduceAsync(Topic + ".dlq", It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.ConsumerFactoryCalls.ShouldBe(1);
    }

    [Fact]
    public async Task WhenDeadLetterDisabled_PoisonMessageIsNotCommitted_AndPartitionIsSoughtBack()
    {
        var poison = Record("not json at all");
        var options = new KafkaOptions();
        options.Consumer.DeadLetterEnabled = false;
        options.Consumer.ConsumeErrorBackoffMs = 0;
        var harness = new Harness(options, poison);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(ProbeEvent));
        await WaitUntilAsync(() => harness.Consumer.Invocations.Any(i => i.Method.Name == nameof(IConsumer<string, string>.Seek)), "the partition to be sought back");

        // 「不提交」守不住偏移量：该分区下一条消息的 Commit 会把它一起跳过。Seek 回去让分区响亮地卡住，才是 Kafka 语境下的「绝不静默丢弃」
        harness.Consumer.Verify(c => c.Seek(poison.TopicPartitionOffset), Times.AtLeastOnce);
        harness.Consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
        harness.Producer.Verify(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.ConsumerFactoryCalls.ShouldBe(1);
    }

    [Fact]
    public async Task WithMaxReconnectAttemptsZero_PoisonMessageDoesNotStopTheConsumer()
    {
        var poison = Record("{\"status\":\"NotAKnownValue\"}");
        var options = new KafkaOptions();
        options.Consumer.MaxReconnectAttempts = 0;
        var harness = new Harness(options, poison);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(ProbeEvent));
        await WaitUntilAsync(() => harness.Consumer.Invocations.Any(i => i.Method.Name == nameof(IConsumer<string, string>.Commit)), "the poison message to be committed");

        // 消费循环还活着：处置完毒消息后继续拉取
        var consumesAfterCommit = harness.Consumer.Invocations.Count(i => i.Method.Name == nameof(IConsumer<string, string>.Consume));
        await WaitUntilAsync(
            () => harness.Consumer.Invocations.Count(i => i.Method.Name == nameof(IConsumer<string, string>.Consume)) > consumesAfterCommit,
            "the consumer loop to keep polling after the poison message");
    }

    /// <summary>
    /// 广播订阅的毒消息与广播的处理器失败同一处置：<b>丢弃</b>（提交、记 Error），既不进死信也不 Seek。
    /// </summary>
    /// <remarks>
    /// 死信：广播组按实例，N 个实例会各投一份进共享的 <c>{topic}.dlq</c>，而重放一条过期的广播本身就是有害的。
    /// Seek：广播组随实例退出而废弃，没有下一个消费者会来接手这个分区 —— 卡住的是本实例自己，
    /// 它从此再也看不到任何后续广播，直到重启。
    /// </remarks>
    [Fact]
    public async Task BroadcastPoisonMessage_WithDeadLetterEnabled_IsDroppedNotDeadLettered()
    {
        var broadcastTopic = "Tnzi.Events." + typeof(ProbeBroadcastEvent).FullName;
        var poison = Record("not json at all", broadcastTopic);
        var harness = new Harness(new KafkaOptions(), poison);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(ProbeBroadcastEvent));
        await WaitUntilAsync(() => harness.Consumer.Invocations.Any(i => i.Method.Name == nameof(IConsumer<string, string>.Commit)), "the broadcast poison message to be committed");

        harness.Consumer.Verify(c => c.Commit(poison), Times.Once);
        harness.Producer.Verify(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never, "广播的毒消息不进死信：每个实例都会投一份，而重放过期广播是有害的");
        harness.Consumer.Verify(c => c.Seek(It.IsAny<TopicPartitionOffset>()), Times.Never);
    }

    [Fact]
    public async Task BroadcastPoisonMessage_WithDeadLetterDisabled_IsCommittedNotSoughtBack()
    {
        var broadcastTopic = "Tnzi.Events." + typeof(ProbeBroadcastEvent).FullName;
        var poison = Record("not json at all", broadcastTopic);
        var options = new KafkaOptions();
        options.Consumer.DeadLetterEnabled = false;
        options.Consumer.ConsumeErrorBackoffMs = 0;
        var harness = new Harness(options, poison);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(ProbeBroadcastEvent));
        await WaitUntilAsync(() => harness.Consumer.Invocations.Any(i => i.Method.Name == nameof(IConsumer<string, string>.Commit)), "the broadcast poison message to be committed");

        // 工作队列在这里 Seek 回去让分区响亮地卡住；广播组没有下一个消费者会来接手，卡住的只是本实例自己
        harness.Consumer.Verify(c => c.Commit(poison), Times.Once);
        harness.Consumer.Verify(c => c.Seek(It.IsAny<TopicPartitionOffset>()), Times.Never);
        harness.Producer.Verify(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 死信关闭时毒消息 Seek 回原地、每个周期重读，Critical 只在第一个周期与之后每 60 个周期各一条。
    /// 这个节流按<b>分区</b>计：另一个分区的好记录穿插进来不能把计数清零。
    /// </summary>
    /// <remarks>
    /// 此前计数是一个变量，任何成功的反序列化都清掉它。多分区主题上只要别的分区有流量，
    /// 卡住的那条每个周期都从 0 重新算起 —— Critical 每秒一条，节流形同虚设。
    /// </remarks>
    [Fact]
    public async Task PoisonStallOnOnePartition_IsNotReLoggedAsCritical_WhenAnotherPartitionDeliversGoodRecords()
    {
        var poison = Record("not json at all", partition: 0, offset: 42);
        var good = Record("{\"status\":\"Known\"}", partition: 1, offset: 7);
        var options = new KafkaOptions();
        options.Consumer.DeadLetterEnabled = false;
        options.Consumer.ConsumeErrorBackoffMs = 0;
        var logger = new RecordingLogger();
        // 毒 / 好 / 毒 / 好 / 毒 …：Seek 回去之后分区 0 每轮都交出同一条，分区 1 之间穿插好记录
        var harness = new Harness(options, n => n > 12 ? null : n % 2 == 1 ? poison : good, logger);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(ProbeEvent));
        await WaitUntilAsync(() => harness.Consumer.Invocations.Count(i => i.Method.Name == nameof(IConsumer<string, string>.Seek)) >= 6, "the poison record to be sought back six times");

        logger.CriticalPoisonEntries.ShouldBe(1, "只有卡住的第一个周期该记 Critical；分区 1 的好记录不代表分区 0 的毒消息有任何变化");
    }

    private sealed class RecordingLogger : ILogger<KafkaEventBus>
    {
        private int _criticalPoisonEntries;

        public int CriticalPoisonEntries => Volatile.Read(ref _criticalPoisonEntries);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Critical && formatter(state, exception).StartsWith("Poison message", StringComparison.Ordinal))
                Interlocked.Increment(ref _criticalPoisonEntries);
        }
    }

    private static string? HeaderOf(Message<string, string> message, string key)
        => message.Headers != null && message.Headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}
