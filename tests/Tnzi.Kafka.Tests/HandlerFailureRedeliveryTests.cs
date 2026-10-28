using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EventBus;
using Tnzi.Kafka.Options;

namespace Tnzi.Kafka.Tests;

/// <summary>
/// 处理器耗尽重试、而消息又没能进死信（死信关闭，或死信投递失败）时，分区必须卡在这条消息上，
/// 不能被同分区下一条消息的提交越过。
/// </summary>
/// <remarks>
/// <para>
/// Kafka 提交的是分区的<b>位置</b>，不是单条消息：「不提交」守不住偏移量。此前这两条路径只记一条
/// 「offset NOT committed; message will be redelivered」就继续拉下一条，同分区下一条处理成功、
/// <c>Commit(N+1)</c> 提交位置 N+2，失败的 N 就被永久越过：重启、再均衡都不会再投，日志却说会重投。
/// 修复与毒消息路径同形：Seek 回失败消息的偏移量，让分区响亮地卡住，直到处理器恢复或死信可用。
/// </para>
/// <para>
/// 消费者是一个按偏移量真正记位置的替身：<c>Consume</c> 依次交出分区日志里的记录，
/// <c>Seek</c> 把位置挪回指定偏移量。不这样做的话 Seek 只是一次没有后果的调用，
/// 断言「调用了 Seek」证明不了失败的消息真的被重读、而不是被越过。
/// </para>
/// </remarks>
public class HandlerFailureRedeliveryTests
{
    private static readonly string Topic = "Tnzi.Events." + typeof(OrderPlaced).FullName;
    private static readonly string BroadcastTopic = "Tnzi.Events." + typeof(CachePurged).FullName;

    public sealed class OrderPlaced : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";

        public string Marker { get; set; } = string.Empty;
    }

    public sealed class CachePurged : EventBase, IBroadcastIntegrationEvent
    {
        public string SourceService => "tests";

        public string Marker { get; set; } = string.Empty;
    }

    /// <summary>对标记为 A 的消息先失败指定次数，之后成功；其余消息一律成功。</summary>
    private sealed class FlakyHandler(int failuresOfA) : IEventHandler<OrderPlaced>, IEventHandler<CachePurged>
    {
        private int _callsOfA;

        public int CallsOfA => Volatile.Read(ref _callsOfA);

        public Task HandleAsync(OrderPlaced @event, CancellationToken cancellationToken = default) => Handle(@event.Marker);

        public Task HandleAsync(CachePurged @event, CancellationToken cancellationToken = default) => Handle(@event.Marker);

        private Task Handle(string marker)
        {
            if (marker == "A" && Interlocked.Increment(ref _callsOfA) <= failuresOfA)
            {
                throw new InvalidOperationException("downstream unavailable");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>单分区日志 + 按偏移量记位置的消费者替身。</summary>
    private sealed class Harness
    {
        private readonly object _gate = new();
        private readonly List<ConsumeResult<string, string>> _log;
        private int _position;

        public Mock<IProducer<string, string>> Producer { get; } = new();
        public Mock<IConsumer<string, string>> Consumer { get; } = new();
        public List<long> CommittedOffsets { get; } = [];
        public List<long> SoughtOffsets { get; } = [];
        public KafkaEventBus Bus { get; }

        // maxSeeks：超过这么多次 Seek 之后分区「读完」（之后 Consume 只返回 null），让一直卡住的场景有确定的周期数
        public Harness(
            KafkaOptions options,
            FlakyHandler handler,
            IReadOnlyList<ConsumeResult<string, string>> log,
            ILogger<KafkaEventBus>? logger = null,
            int maxSeeks = int.MaxValue)
        {
            _log = [.. log];

            Consumer.Setup(c => c.Consume(It.IsAny<TimeSpan>()))
                .Returns(() =>
                {
                    lock (_gate)
                    {
                        return _position < _log.Count ? _log[_position++] : null!;
                    }
                });
            Consumer.Setup(c => c.Seek(It.IsAny<TopicPartitionOffset>()))
                .Callback<TopicPartitionOffset>(target =>
                {
                    lock (_gate)
                    {
                        SoughtOffsets.Add(target.Offset.Value);
                        _position = SoughtOffsets.Count > maxSeeks
                            ? _log.Count
                            : _log.FindIndex(r => r.TopicPartitionOffset.Equals(target));
                    }
                });
            Consumer.Setup(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()))
                .Callback<ConsumeResult<string, string>>(result =>
                {
                    lock (_gate)
                    {
                        CommittedOffsets.Add(result.Offset.Value);
                    }
                });

            Producer.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeliveryResult<string, string>());

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IEventHandler<OrderPlaced>>(handler);
            services.AddSingleton<IEventHandler<CachePurged>>(handler);

            Bus = new KafkaEventBus(
                Producer.Object,
                logger ?? NullLogger<KafkaEventBus>.Instance,
                services.BuildServiceProvider(),
                options,
                "localhost:9092",
                new DistributedConsumerIdentity("tests", Guid.NewGuid()),
                consumerFactory: _ => Consumer.Object);
        }

        public long[] Commits()
        {
            lock (_gate)
            {
                return [.. CommittedOffsets];
            }
        }

        public long[] Seeks()
        {
            lock (_gate)
            {
                return [.. SoughtOffsets];
            }
        }
    }

    private static ConsumeResult<string, string> Record(string topic, string marker, long offset) => new()
    {
        Topic = topic,
        Partition = new Partition(0),
        Offset = new Offset(offset),
        Message = new Message<string, string> { Key = "k", Value = $"{{\"marker\":\"{marker}\"}}" }
    };

    private static KafkaOptions FastOptions(bool deadLetterEnabled)
    {
        var options = new KafkaOptions();
        options.Consumer.DeadLetterEnabled = deadLetterEnabled;
        options.Consumer.ConsumeRetryBackoffMs = 0;
        options.Consumer.ConsumeErrorBackoffMs = 0;
        return options;
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

    /// <summary>
    /// ★★★ 死信关闭：A 耗尽重试后分区卡在 A，A 在处理器恢复后被重读并提交，然后才轮到 B。
    /// </summary>
    [Fact]
    public async Task DeadLetterDisabled_FailedMessageIsReReadNotSkippedByTheNextCommit()
    {
        // MaxConsumeRetries=3 ⇒ 一个周期 4 次尝试；A 第一个周期全败，第二个周期成功
        var handler = new FlakyHandler(failuresOfA: 4);
        var a = Record(Topic, "A", offset: 42);
        var b = Record(Topic, "B", offset: 43);
        var harness = new Harness(FastOptions(deadLetterEnabled: false), handler, [a, b]);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(OrderPlaced));
        await WaitUntilAsync(() => harness.Commits().Length >= 2, "both records to be committed");

        harness.Commits().ShouldBe([42L, 43L], "A 必须先被提交：越过它直接提交 B 等于把 A 静默丢掉");
        harness.Seeks().ShouldBe([42L]);
        handler.CallsOfA.ShouldBe(5);
    }

    /// <summary>
    /// ★★★ 死信开启但投递失败（死信主题不存在 / 无 ACL）：同样 Seek 回去，不能被下一条的提交越过。
    /// </summary>
    [Fact]
    public async Task DeadLetterProduceFails_FailedMessageIsReReadNotSkippedByTheNextCommit()
    {
        var handler = new FlakyHandler(failuresOfA: 4);
        var a = Record(Topic, "A", offset: 42);
        var b = Record(Topic, "B", offset: 43);
        var harness = new Harness(FastOptions(deadLetterEnabled: true), handler, [a, b]);
        harness.Producer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, string>(
                new Error(ErrorCode.UnknownTopicOrPart), new DeliveryResult<string, string>()));
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(OrderPlaced));
        await WaitUntilAsync(() => harness.Commits().Length >= 2, "both records to be committed");

        harness.Commits().ShouldBe([42L, 43L], "死信投递失败时 A 既没进死信也没被处理，越过它就是丢失");
        harness.Seeks().ShouldBe([42L]);
        harness.Producer.Verify(
            p => p.ProduceAsync(Topic + ".dlq", It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>对照组：死信投递成功时 A 进死信并提交，不 Seek。</summary>
    [Fact]
    public async Task DeadLetterProduceSucceeds_FailedMessageIsCommittedWithoutSeeking()
    {
        var handler = new FlakyHandler(failuresOfA: int.MaxValue);
        var a = Record(Topic, "A", offset: 42);
        var b = Record(Topic, "B", offset: 43);
        var harness = new Harness(FastOptions(deadLetterEnabled: true), handler, [a, b]);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(OrderPlaced));
        await WaitUntilAsync(() => harness.Commits().Length >= 2, "both records to be committed");

        harness.Commits().ShouldBe([42L, 43L]);
        harness.Seeks().ShouldBeEmpty();
    }

    /// <summary>
    /// 广播不变：重投一次后丢弃并提交，不 Seek（广播组按实例，卡住的只会是本实例自己）。
    /// </summary>
    [Fact]
    public async Task BroadcastHandlerFailure_IsStillDroppedNotSoughtBack()
    {
        var handler = new FlakyHandler(failuresOfA: int.MaxValue);
        var a = Record(BroadcastTopic, "A", offset: 42);
        var b = Record(BroadcastTopic, "B", offset: 43);
        var harness = new Harness(FastOptions(deadLetterEnabled: false), handler, [a, b]);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(CachePurged));
        await WaitUntilAsync(() => harness.Commits().Length >= 2, "both broadcast records to be committed");

        harness.Commits().ShouldBe([42L, 43L]);
        harness.Seeks().ShouldBeEmpty();
        handler.CallsOfA.ShouldBe(2);
    }

    /// <summary>
    /// 处理器一直失败时分区一直卡着：每个周期都 Seek 回去，但 Critical 只在卡住的第一个周期记一条，
    /// 之后按周期节流，不能每个周期刷一条。
    /// </summary>
    [Fact]
    public async Task PersistentHandlerFailure_KeepsThePartitionBlocked_AndThrottlesTheCriticalLog()
    {
        var handler = new FlakyHandler(failuresOfA: int.MaxValue);
        var a = Record(Topic, "A", offset: 42);
        var b = Record(Topic, "B", offset: 43);
        var logger = new RecordingLogger();
        var harness = new Harness(FastOptions(deadLetterEnabled: false), handler, [a, b], logger, maxSeeks: 10);
        await using var bus = harness.Bus;

        bus.SubscribeEvent(typeof(OrderPlaced));
        await WaitUntilAsync(() => harness.Seeks().Length > 10, "the partition to be held for eleven poll cycles");

        harness.Seeks().ShouldAllBe(offset => offset == 42L);
        harness.Commits().ShouldBeEmpty("B 排在卡住的 A 后面，A 没处理成功之前 B 一条都不能提交");
        logger.CriticalEntries.ShouldBe(1, "十一个周期里只有卡住的第一个周期该记 Critical");
    }

    private sealed class RecordingLogger : ILogger<KafkaEventBus>
    {
        private int _criticalEntries;

        public int CriticalEntries => Volatile.Read(ref _criticalEntries);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Critical)
                Interlocked.Increment(ref _criticalEntries);
        }
    }
}
