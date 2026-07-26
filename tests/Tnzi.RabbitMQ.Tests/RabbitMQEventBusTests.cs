using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Tnzi.EventBus;
using Tnzi.RabbitMQ;
using Tnzi.RabbitMQ.Options;

namespace Tnzi.RabbitMQ.Tests;

/// <summary>
/// RabbitMQEventBus 单元测试
/// 注意：RabbitMQ.Client 7.x 中 ExchangeDeclareAsync(7 params) 和 CloseAsync(CancellationToken) 是扩展方法，
/// 必须 mock IChannel 接口的实际方法签名
/// </summary>
public class RabbitMQEventBusTests : IDisposable
{
    private readonly Mock<IConnection> _mockConnection;
    private readonly Mock<IChannel> _mockChannel;
    private readonly Mock<ILogger<RabbitMQEventBus>> _mockLogger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMQOptions _options;

    public RabbitMQEventBusTests()
    {
        _mockConnection = new Mock<IConnection>();
        _mockChannel = new Mock<IChannel>();
        _mockLogger = new Mock<ILogger<RabbitMQEventBus>>();
        _options = new RabbitMQOptions();

        // 模拟 channel 创建
        _mockChannel.Setup(c => c.IsOpen).Returns(true);
        _mockConnection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockChannel.Object);

        // 模拟 exchange 声明 (IChannel 接口方法: 8 参数，包含 passive 和 noWait)
        _mockChannel.Setup(c => c.ExchangeDeclareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // 模拟 publish (IChannel 泛型接口方法)
        _mockChannel.Setup(c => c.BasicPublishAsync<BasicProperties>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        // 模拟 channel 关闭 (IChannel 接口方法: CloseAsync(ushort, string, bool, CancellationToken))
        // 扩展方法 CloseAsync(CancellationToken) 内部委托给此方法
        _mockChannel.Setup(c => c.CloseAsync(
                It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    private RabbitMQEventBus CreateEventBus(string? exchangeName = null)
    {
        return new RabbitMQEventBus(
            _mockConnection.Object,
            _mockLogger.Object,
            _serviceProvider,
            _options,
            exchangeName);
    }

    public void Dispose()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new RabbitMQEventBus(null!, _mockLogger.Object, _serviceProvider, _options));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new RabbitMQEventBus(_mockConnection.Object, null!, _serviceProvider, _options));
    }

    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new RabbitMQEventBus(_mockConnection.Object, _mockLogger.Object, null!, _options));
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new RabbitMQEventBus(_mockConnection.Object, _mockLogger.Object, _serviceProvider, null!));
    }

    [Fact]
    public void Constructor_ValidParams_CreatesInstance()
    {
        var bus = CreateEventBus();
        bus.ShouldNotBeNull();
        bus.IsLocal.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_CustomExchangeName_IsAccepted()
    {
        var bus = CreateEventBus("custom.exchange");
        bus.ShouldNotBeNull();
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_ValidEvent_PublishesToChannel()
    {
        var bus = CreateEventBus();
        var @event = new TestEvent("test-message");

        await bus.PublishAsync(@event);

        // 验证 channel 创建
        _mockConnection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // 验证 publish 调用 (泛型接口方法)
        _mockChannel.Verify(c => c.BasicPublishAsync<BasicProperties>(
            "Tnzi.Events",
            It.IsAny<string>(),
            false,
            It.IsAny<BasicProperties>(),
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_NullEvent_ThrowsArgumentNullException()
    {
        var bus = CreateEventBus();
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await bus.PublishAsync<TestEvent>(null!));
    }

    [Fact]
    public async Task PublishAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var bus = CreateEventBus();
        bus.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(async () =>
            await bus.PublishAsync(new TestEvent("test")));
    }

    [Fact]
    public async Task PublishAsync_ChannelRecovery_RecreatesChannelWhenClosed()
    {
        var bus = CreateEventBus();

        // 第一次发布成功（_mockChannel.IsOpen = true）
        await bus.PublishAsync(new TestEvent("test1"));
        _mockConnection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        // 模拟 channel 关闭（连接断开后恢复场景）
        _mockChannel.Setup(c => c.IsOpen).Returns(false);

        // 创建恢复后的新 channel
        var recoveredChannel = new Mock<IChannel>();
        recoveredChannel.Setup(c => c.IsOpen).Returns(true);
        recoveredChannel.Setup(c => c.ExchangeDeclareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        recoveredChannel.Setup(c => c.BasicPublishAsync<BasicProperties>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        _mockConnection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recoveredChannel.Object);

        // 第二次发布 - 检测到 IsOpen=false，触发 channel 恢复
        await bus.PublishAsync(new TestEvent("test2"));

        // 验证 channel 创建被调用了两次（初次 + 恢复）
        _mockConnection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // 验证恢复后的 channel 被用于发布
        recoveredChannel.Verify(c => c.BasicPublishAsync<BasicProperties>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_MultipleEvents_ReusesChannel()
    {
        var bus = CreateEventBus();

        await bus.PublishAsync(new TestEvent("msg1"));
        await bus.PublishAsync(new TestEvent("msg2"));
        await bus.PublishAsync(new TestEvent("msg3"));

        // Channel 应该只创建一次
        _mockConnection.Verify(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region PublishDelayedAsync Tests

    [Fact]
    public async Task PublishDelayedAsync_ZeroDelay_PublishesImmediately()
    {
        var bus = CreateEventBus();
        var @event = new TestEvent("test");

        await bus.PublishDelayedAsync(@event, TimeSpan.Zero);

        _mockChannel.Verify(c => c.BasicPublishAsync<BasicProperties>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishDelayedAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var bus = CreateEventBus();
        bus.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(async () =>
            await bus.PublishDelayedAsync(new TestEvent("test"), TimeSpan.FromSeconds(1)));
    }

    #endregion

    #region HasHandlers / GetHandlerCount Tests

    [Fact]
    public void HasHandlers_NoHandlersRegistered_ReturnsFalse()
    {
        var bus = CreateEventBus();
        bus.HasHandlers<TestEvent>().ShouldBeFalse();
    }

    [Fact]
    public void GetHandlerCount_NoHandlersRegistered_ReturnsZero()
    {
        var bus = CreateEventBus();
        bus.GetHandlerCount<TestEvent>().ShouldBe(0);
    }

    [Fact]
    public void HasHandlers_WithRegisteredHandler_ReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();
        var sp = services.BuildServiceProvider();

        var bus = new RabbitMQEventBus(
            _mockConnection.Object, _mockLogger.Object, sp, _options);

        bus.HasHandlers<TestEvent>().ShouldBeTrue();
        bus.GetHandlerCount<TestEvent>().ShouldBe(1);

        (sp as IDisposable)?.Dispose();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var bus = CreateEventBus();
        bus.Dispose();
        bus.Dispose(); // 第二次调用不应抛出
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        var bus = CreateEventBus();
        await bus.DisposeAsync();
        await bus.DisposeAsync(); // 第二次调用不应抛出
    }

    [Fact]
    public async Task DisposeAsync_WithPublishChannel_ClosesChannel()
    {
        var bus = CreateEventBus();

        // 先发布以创建 publish channel
        await bus.PublishAsync(new TestEvent("test"));

        await bus.DisposeAsync();

        // 验证 channel 被关闭 (IChannel 接口方法: CloseAsync(ushort, string, bool, CancellationToken))
        _mockChannel.Verify(c => c.CloseAsync(
            It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Subscribe / Unsubscribe Tests

    [Fact]
    public void Subscribe_LogsWarning()
    {
        var bus = CreateEventBus();
        bus.Subscribe<TestEvent, TestEventHandler>();

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Unsubscribe_LogsWarning()
    {
        var bus = CreateEventBus();
        bus.Unsubscribe<TestEvent, TestEventHandler>();

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void UnsubscribeAll_LogsWarning()
    {
        var bus = CreateEventBus();
        bus.UnsubscribeAll<TestEvent>();

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region IIntegrationEventBus Tests

    [Fact]
    public async Task IIntegrationEventBus_PublishAsync_DelegatesToPublishAsync()
    {
        var bus = CreateEventBus();
        IIntegrationEventBus integrationBus = bus;
        var @event = new TestIntegrationEvent("integration-test");

        await integrationBus.PublishAsync(@event);

        _mockChannel.Verify(c => c.BasicPublishAsync<BasicProperties>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Test Types

    public class TestEvent : EventBase
    {
        public string Message { get; }

        public TestEvent(string message)
        {
            Message = message;
        }
    }

    public class TestIntegrationEvent : EventBase, IIntegrationEvent
    {
        public string Message { get; }
        public string SourceService => "TestService";

        public TestIntegrationEvent(string message)
        {
            Message = message;
        }
    }

    public class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    #endregion
}
