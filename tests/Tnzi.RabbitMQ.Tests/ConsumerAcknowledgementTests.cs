using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Tnzi.EventBus;
using Tnzi.Json;
using Tnzi.RabbitMQ.Options;

namespace Tnzi.RabbitMQ.Tests;

/// <summary>
/// 消费端确认语义：什么情况下一条消息可以被 ACK。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：处理器抛异常时消息仍被 ACK，于是它<b>永久消失</b> ——
/// 不重试、不进死信、除日志外无迹可寻。成因是错误隔离方法把异常吞在自己内部且返回
/// <c>Task</c>，<c>Task.WhenAll</c> 因此永不抛出，消费回调走的是正常路径。
/// 重试与死信机制（<c>HandleConsumerErrorAsync</c>）一直都在，只是够不着。
/// </para>
/// <para>
/// ★ <b>这类缺陷读代码时最像"没问题"</b>：日志里有 <c>LogError</c>、注释写着"错误隔离"、
/// 死信队列声明得好好的，每一处单看都对。错的是它们之间那条没接上的线。
/// </para>
/// <para>
/// Kafka 侧的同一缺陷已于 2026-05-28 修复（<c>KafkaConsumeDecider</c> 三态处置），
/// 本模块此前未跟进 —— 所以这里也顺带锁住"两个总线在失败处置上不再分叉"。
/// </para>
/// </remarks>
public class ConsumerAcknowledgementTests : IDisposable
{
    private const ulong DeliveryTag = 42UL;

    private readonly Mock<IConnection> _mockConnection;
    private readonly Mock<IChannel> _mockChannel;
    private readonly RabbitMQOptions _options;

    /// <summary>
    /// 代理侧动作的发生顺序。ACK 本身不是罪名 —— <b>没先把副本送进队列就 ACK</b> 才是。
    /// </summary>
    private readonly List<string> _brokerCalls = [];

    private ServiceProvider? _serviceProvider;

    public ConsumerAcknowledgementTests()
    {
        _mockConnection = new Mock<IConnection>();
        _mockChannel = new Mock<IChannel>();
        _options = new RabbitMQOptions();

        // 退避在测试里毫无价值，只会让每个用例白等一秒
        _options.RetryDelay.InitialDelayMs = 0;

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
            .Returns(Task.CompletedTask);

        _mockChannel.Setup(c => c.BasicQosAsync(
                It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockChannel.Setup(c => c.BasicAckAsync(
                It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => _brokerCalls.Add("ack"))
            .Returns(new ValueTask());

        _mockChannel.Setup(c => c.BasicNackAsync(
                It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => _brokerCalls.Add("nack"))
            .Returns(new ValueTask());

        _mockChannel.Setup(c => c.BasicPublishAsync<BasicProperties>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                (_, _, _, properties, _, _) => _brokerCalls.Add($"republish(retry={RetryCountOf(properties)})"))
            .Returns(new ValueTask());

        _mockChannel.Setup(c => c.CloseAsync(
                It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _serviceProvider?.Dispose();

    /// <summary>
    /// 处理器抛异常时消息不会被丢掉：带递增计数的副本先入队，原消息才被确认。
    /// </summary>
    /// <remarks>
    /// ★ 断言的是<b>顺序</b>而不是"不许 ACK"。原消息最终确实被 ACK 了，那是对的 ——
    /// 副本已经在队列里，留着原消息只会重复投递。真正的缺陷形态是
    /// <b>ACK 之前什么都没发生</b>：修复前这里只有一条孤零零的 <c>ack</c>。
    /// 顺序反过来（先 ACK 再重发）同样会丢消息，重发失败时原件已经没了，
    /// 所以这条断言两个方向都守着。
    /// </remarks>
    [Fact]
    public async Task AFailingHandler_RepublishesARetryCopyBeforeAcknowledgingTheOriginal()
    {
        var handler = new RecordingHandler(shouldThrow: true);
        var bus = CreateBusWith(handler);

        var consumer = await SubscribeAndCaptureConsumerAsync(bus);
        await DeliverAsync(consumer, new ProbeEvent("boom"));

        handler.Invocations.ShouldBe(1, "处理器应当真的被调用过，否则这个测试什么也没证明");

        _brokerCalls.ShouldBe(
            ["republish(retry=1)", "ack"],
            "修复前这里只有一条 ack：消息被确认后永久消失，不重试、不进死信、无迹可寻");
    }

    /// <summary>
    /// 重试预算耗尽后进死信队列，而不是被丢弃。
    /// </summary>
    /// <remarks>
    /// 主队列声明了 <c>x-dead-letter-exchange</c>，故 <c>Nack(requeue: false)</c> 即"投进死信"。
    /// </remarks>
    [Fact]
    public async Task AFailingHandler_AfterTheRetryBudgetIsSpent_GoesToTheDeadLetterQueue()
    {
        var handler = new RecordingHandler(shouldThrow: true);
        var bus = CreateBusWith(handler);

        var consumer = await SubscribeAndCaptureConsumerAsync(bus);
        await DeliverAsync(consumer, new ProbeEvent("boom"), retryCount: _options.MaxRetryCount);

        // 只 nack 不 ack、也不再重发副本：多发一份会让死信队列里出现重复且计数永远推不动
        _brokerCalls.ShouldBe(["nack"]);

        _mockChannel.Verify(
            c => c.BasicNackAsync(DeliveryTag, false, false, It.IsAny<CancellationToken>()),
            Times.Once,
            "requeue=false 才会命中主队列的 x-dead-letter-exchange");
    }

    /// <summary>
    /// 一个处理器失败会让整条消息重投，即使另一个成功了。
    /// </summary>
    /// <remarks>
    /// 这是 at-least-once 的代价，也是"错误隔离"的边界所在：隔离的是处理器之间
    /// （一个失败不拖累其余<b>本轮的执行</b>），不是"对代理隐瞒失败"。
    /// 因此处理器必须幂等 —— 成功的那个会在重投时被再执行一次。
    /// </remarks>
    [Fact]
    public async Task OneFailingHandlerAmongSeveral_StillGetsTheMessageRetried()
    {
        var failing = new RecordingHandler(shouldThrow: true);
        var succeeding = new SecondRecordingHandler();
        var bus = CreateBusWith(failing, succeeding);

        var consumer = await SubscribeAndCaptureConsumerAsync(bus);
        await DeliverAsync(consumer, new ProbeEvent("mixed"));

        succeeding.Invocations.ShouldBe(1, "错误隔离意味着另一个处理器本轮仍然跑完");
        failing.Invocations.ShouldBe(1);

        _brokerCalls.ShouldBe(
            ["republish(retry=1)", "ack"],
            "一个处理器失败就足以让整条消息重投 —— 部分成功不是可以确认的理由");
    }

    /// <summary>
    /// 全部成功仍然照常 ACK —— 防止把守卫做成"什么都不确认"。
    /// </summary>
    [Fact]
    public async Task AllHandlersSucceeding_AcknowledgesTheMessage()
    {
        var handler = new RecordingHandler(shouldThrow: false);
        var bus = CreateBusWith(handler);

        var consumer = await SubscribeAndCaptureConsumerAsync(bus);
        await DeliverAsync(consumer, new ProbeEvent("fine"));

        handler.Invocations.ShouldBe(1);

        _brokerCalls.ShouldBe(["ack"], "全部成功就该干干净净地确认一次，不重发也不 nack");
    }

    /// <summary>
    /// 没有任何处理器订阅时照常 ACK —— 没人关心的消息不该堆在队列里。
    /// </summary>
    [Fact]
    public async Task NoHandlers_AcknowledgesTheMessage()
    {
        var bus = CreateBusWith();

        var consumer = await SubscribeAndCaptureConsumerAsync(bus);
        await DeliverAsync(consumer, new ProbeEvent("nobody-home"));

        _brokerCalls.ShouldBe(["ack"]);
    }

    /// <summary>
    /// 反序列化不出来的毒消息进死信队列，而不是被静默丢弃。
    /// </summary>
    /// <remarks>
    /// 刻意<b>不</b>走重试：同样的字节重投多少次都是同样的结果，只会白白烧掉重试预算。
    /// 但也不能 ACK —— 那是把一条本该有人去看的坏消息连同它记录的那件事一起扔掉。
    /// </remarks>
    [Fact]
    public async Task AnUndeserializableMessage_GoesToTheDeadLetterQueueRatherThanBeingDropped()
    {
        var handler = new RecordingHandler(shouldThrow: false);
        var bus = CreateBusWith(handler);

        var consumer = await SubscribeAndCaptureConsumerAsync(bus);
        await DeliverRawAsync(consumer, Encoding.UTF8.GetBytes("null"));

        handler.Invocations.ShouldBe(0);

        _brokerCalls.ShouldBe(
            ["nack"],
            "修复前这里是一条 ack：毒消息被确认后再没有人能发现它曾经存在；" +
            "而重投同样的字节只会白白烧掉重试预算，所以也不该出现 republish");

        _mockChannel.Verify(
            c => c.BasicNackAsync(DeliveryTag, false, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// ★ 表态动作只发生一次 —— 即使它自己失败了。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 早先「干活」与「向代理表态」在同一个 <c>try</c> 里，而 <c>catch</c> 也会调
    /// <c>HandleConsumerErrorAsync</c>。于是一旦 Nack / 重发本身抛异常（网络抖动、
    /// channel 已关），同一个 <c>DeliveryTag</c> 会被确认<b>两次</b> ——
    /// RabbitMQ 以 <c>PRECONDITION_FAILED</c> 关掉整条 channel，
    /// <b>这个事件类型就此静默停止消费</b>，直到进程重启。
    /// </para>
    /// <para>
    /// 未确认的消息本来就会在 channel 关闭时由代理自动重投，所以「到此为止」是安全的；
    /// 而补第二次确认换来的不是可靠性，是一条死掉的消费通道。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WhenAcknowledgingItselfFails_ItIsNotAttemptedASecondTime()
    {
        var attempts = 0;
        _mockChannel.Setup(c => c.BasicNackAsync(
                It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => { attempts++; _brokerCalls.Add("nack"); })
            .Throws(new InvalidOperationException("channel is already closed"));

        var bus = CreateBusWith(new RecordingHandler(shouldThrow: false));
        var consumer = await SubscribeAndCaptureConsumerAsync(bus);

        // 毒消息 → 直接进死信，而那次 nack 会抛
        await DeliverRawAsync(consumer, Encoding.UTF8.GetBytes("null"));

        attempts.ShouldBe(1, "重复确认同一个 DeliveryTag 会让代理关掉整条 channel");
        _brokerCalls.ShouldBe(["nack"], "不得再补一次 nack，也不得退而求其次去 ack");
    }

    #region Harness

    private RabbitMQEventBus CreateBusWith(params object[] handlers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var handler in handlers)
        {
            services.AddSingleton(typeof(IEventHandler<ProbeEvent>), handler);
        }

        _serviceProvider = services.BuildServiceProvider();

        return new RabbitMQEventBus(
            _mockConnection.Object,
            NullLogger<RabbitMQEventBus>.Instance,
            _serviceProvider,
            _options);
    }

    /// <summary>
    /// 订阅并夺回框架交给代理的那个消费者，之后就能在没有真实 broker 的情况下投递消息。
    /// </summary>
    private async Task<IAsyncBasicConsumer> SubscribeAndCaptureConsumerAsync(RabbitMQEventBus bus)
    {
        IAsyncBasicConsumer? captured = null;

        _mockChannel.Setup(c => c.BasicConsumeAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>?, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) => captured = consumer)
            .ReturnsAsync("consumer-tag");

        await bus.SubscribeEventAsync<ProbeEvent>();

        captured.ShouldNotBeNull("没抓到消费者就无法投递消息，测试会假绿");
        return captured!;
    }

    private Task DeliverAsync(IAsyncBasicConsumer consumer, ProbeEvent @event, int retryCount = 0)
        => DeliverRawAsync(
            consumer,
            JsonSerializer.SerializeToUtf8Bytes(@event, TnziJsonDefaults.Options),
            retryCount);

    private Task DeliverRawAsync(IAsyncBasicConsumer consumer, byte[] body, int retryCount = 0)
    {
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?> { ["x-retry-count"] = retryCount }
        };

        return consumer.HandleBasicDeliverAsync(
            "consumer-tag",
            DeliveryTag,
            false,
            "Tnzi.Events",
            nameof(ProbeEvent),
            properties,
            body,
            CancellationToken.None);
    }

    private static int RetryCountOf(BasicProperties properties)
        => properties.Headers != null
            && properties.Headers.TryGetValue("x-retry-count", out var raw)
            && raw is int count
                ? count
                : -1;

    public class ProbeEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;

        public ProbeEvent()
        {
        }

        public ProbeEvent(string message) => Message = message;
    }

    private sealed class RecordingHandler : IEventHandler<ProbeEvent>
    {
        private readonly bool _shouldThrow;

        public RecordingHandler(bool shouldThrow) => _shouldThrow = shouldThrow;

        public int Invocations { get; private set; }

        public Task HandleAsync(ProbeEvent @event, CancellationToken cancellationToken = default)
        {
            Invocations++;
            if (_shouldThrow)
                throw new InvalidOperationException("handler blew up");

            return Task.CompletedTask;
        }
    }

    private sealed class SecondRecordingHandler : IEventHandler<ProbeEvent>
    {
        public int Invocations { get; private set; }

        public Task HandleAsync(ProbeEvent @event, CancellationToken cancellationToken = default)
        {
            Invocations++;
            return Task.CompletedTask;
        }
    }

    #endregion
}
