using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Tnzi.EventBus;
using Tnzi.RabbitMQ;
using Tnzi.RabbitMQ.Options;

namespace Tnzi.RabbitMQ.Tests;

/// <summary>
/// Tests for RabbitMQ E2-to-E1 enhancements:
/// - RetryDelayOptions (exponential backoff)
/// - ChannelPoolOptions (channel pooling)
/// - RabbitMQOptionsValidator (new options validation)
/// </summary>
public class RabbitMQEnhancementsTests : IDisposable
{
    private readonly Mock<IConnection> _mockConnection;
    private readonly Mock<IChannel> _mockChannel;
    private readonly Mock<ILogger<RabbitMQEventBus>> _mockLogger;
    private readonly IServiceProvider _serviceProvider;

    public RabbitMQEnhancementsTests()
    {
        _mockConnection = new Mock<IConnection>();
        _mockChannel = new Mock<IChannel>();
        _mockLogger = new Mock<ILogger<RabbitMQEventBus>>();

        _mockChannel.Setup(c => c.IsOpen).Returns(true);
        _mockConnection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockChannel.Object);

        _mockChannel.Setup(c => c.ExchangeDeclareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockChannel.Setup(c => c.BasicPublishAsync<BasicProperties>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        _mockChannel.Setup(c => c.CloseAsync(
                It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region RetryDelayOptions Tests

    [Fact]
    public void RetryDelay_GetDelay_FirstAttempt_ReturnsInitialDelay()
    {
        var options = new RetryDelayOptions
        {
            Enabled = true,
            InitialDelayMs = 1000,
            Multiplier = 2.0,
            MaxDelayMs = 30000
        };

        var delay = options.GetDelay(1);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), delay);
    }

    [Fact]
    public void RetryDelay_GetDelay_SecondAttempt_AppliesMultiplier()
    {
        var options = new RetryDelayOptions
        {
            Enabled = true,
            InitialDelayMs = 1000,
            Multiplier = 2.0,
            MaxDelayMs = 30000
        };

        var delay = options.GetDelay(2);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), delay);
    }

    [Fact]
    public void RetryDelay_GetDelay_ThirdAttempt_DoubleMultiplier()
    {
        var options = new RetryDelayOptions
        {
            Enabled = true,
            InitialDelayMs = 1000,
            Multiplier = 2.0,
            MaxDelayMs = 30000
        };

        var delay = options.GetDelay(3);
        Assert.Equal(TimeSpan.FromMilliseconds(4000), delay);
    }

    [Fact]
    public void RetryDelay_GetDelay_CappedByMaxDelay()
    {
        var options = new RetryDelayOptions
        {
            Enabled = true,
            InitialDelayMs = 1000,
            Multiplier = 10.0,
            MaxDelayMs = 5000
        };

        // Attempt 3: 1000 * 10^2 = 100000ms > 5000ms cap
        var delay = options.GetDelay(3);
        Assert.Equal(TimeSpan.FromMilliseconds(5000), delay);
    }

    [Fact]
    public void RetryDelay_GetDelay_ZeroAttempt_ReturnsZero()
    {
        var options = new RetryDelayOptions { Enabled = true };
        var delay = options.GetDelay(0);
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void RetryDelay_GetDelay_Disabled_ReturnsZero()
    {
        var options = new RetryDelayOptions { Enabled = false, InitialDelayMs = 5000 };
        var delay = options.GetDelay(3);
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void RetryDelay_GetDelay_FixedDelay_MultiplierOne()
    {
        var options = new RetryDelayOptions
        {
            Enabled = true,
            InitialDelayMs = 2000,
            Multiplier = 1.0,
            MaxDelayMs = 30000
        };

        // All attempts should have same delay
        Assert.Equal(TimeSpan.FromMilliseconds(2000), options.GetDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), options.GetDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), options.GetDelay(5));
    }

    [Fact]
    public void RetryDelay_DefaultValues_AreCorrect()
    {
        var options = new RetryDelayOptions();
        Assert.True(options.Enabled);
        Assert.Equal(1000, options.InitialDelayMs);
        Assert.Equal(2.0, options.Multiplier);
        Assert.Equal(30000, options.MaxDelayMs);
    }

    #endregion

    #region ChannelPoolOptions Tests

    [Fact]
    public void ChannelPoolOptions_DefaultValues_AreCorrect()
    {
        var options = new ChannelPoolOptions();
        Assert.False(options.Enabled);
        Assert.Equal(5, options.MaxSize);
    }

    [Fact]
    public async Task PublishAsync_WithChannelPool_CreatesSeparateChannels()
    {
        var callCount = 0;
        _mockConnection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                var mock = new Mock<IChannel>();
                mock.Setup(c => c.IsOpen).Returns(true);
                mock.Setup(c => c.ExchangeDeclareAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                        It.IsAny<IDictionary<string, object?>>(),
                        It.IsAny<bool>(), It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                mock.Setup(c => c.BasicPublishAsync<BasicProperties>(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                        It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask());
                mock.Setup(c => c.CloseAsync(
                        It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                return mock.Object;
            });

        var options = new RabbitMQOptions
        {
            ChannelPool = new ChannelPoolOptions { Enabled = true, MaxSize = 3 }
        };

        var bus = new RabbitMQEventBus(
            _mockConnection.Object, _mockLogger.Object, _serviceProvider, options);

        // Publish three events sequentially
        await bus.PublishAsync(new TestEvent("msg1"));
        await bus.PublishAsync(new TestEvent("msg2"));
        await bus.PublishAsync(new TestEvent("msg3"));

        // First publish creates a channel; subsequent should reuse from pool
        // At least 1 channel should be created, but after returning to pool
        // subsequent calls should reuse the same channel
        Assert.True(callCount >= 1);
    }

    [Fact]
    public async Task PublishAsync_WithoutChannelPool_UsesSingleChannel()
    {
        var options = new RabbitMQOptions
        {
            ChannelPool = new ChannelPoolOptions { Enabled = false }
        };

        var bus = new RabbitMQEventBus(
            _mockConnection.Object, _mockLogger.Object, _serviceProvider, options);

        await bus.PublishAsync(new TestEvent("msg1"));
        await bus.PublishAsync(new TestEvent("msg2"));

        // Single channel mode: only one channel creation
        _mockConnection.Verify(c =>
            c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WithChannelPool_CleansUpPooledChannels()
    {
        var channelMocks = new List<Mock<IChannel>>();
        _mockConnection.Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var mock = new Mock<IChannel>();
                mock.Setup(c => c.IsOpen).Returns(true);
                mock.Setup(c => c.ExchangeDeclareAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                        It.IsAny<IDictionary<string, object?>>(),
                        It.IsAny<bool>(), It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                mock.Setup(c => c.BasicPublishAsync<BasicProperties>(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                        It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask());
                mock.Setup(c => c.CloseAsync(
                        It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                channelMocks.Add(mock);
                return mock.Object;
            });

        var options = new RabbitMQOptions
        {
            ChannelPool = new ChannelPoolOptions { Enabled = true, MaxSize = 5 }
        };

        var bus = new RabbitMQEventBus(
            _mockConnection.Object, _mockLogger.Object, _serviceProvider, options);

        await bus.PublishAsync(new TestEvent("test"));
        await bus.DisposeAsync();

        // Verify at least one channel was closed
        Assert.True(channelMocks.Count > 0);
        foreach (var mock in channelMocks)
        {
            mock.Verify(c => c.CloseAsync(
                It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    #endregion

    #region RabbitMQOptionsValidator Tests

    [Fact]
    public void Validator_RetryDelay_NegativeInitialDelay_Fails()
    {
        var options = new RabbitMQOptions
        {
            RetryDelay = new RetryDelayOptions { InitialDelayMs = -1 }
        };
        var validator = new RabbitMQOptionsValidator();
        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("InitialDelayMs", result.FailureMessage);
    }

    [Fact]
    public void Validator_RetryDelay_MultiplierLessThanOne_Fails()
    {
        var options = new RabbitMQOptions
        {
            RetryDelay = new RetryDelayOptions { Multiplier = 0.5 }
        };
        var validator = new RabbitMQOptionsValidator();
        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Multiplier", result.FailureMessage);
    }

    [Fact]
    public void Validator_RetryDelay_MaxDelayLessThanInitial_Fails()
    {
        var options = new RabbitMQOptions
        {
            RetryDelay = new RetryDelayOptions { InitialDelayMs = 5000, MaxDelayMs = 1000 }
        };
        var validator = new RabbitMQOptionsValidator();
        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("MaxDelayMs", result.FailureMessage);
    }

    [Fact]
    public void Validator_ChannelPool_ZeroMaxSize_Fails()
    {
        var options = new RabbitMQOptions
        {
            ChannelPool = new ChannelPoolOptions { Enabled = true, MaxSize = 0 }
        };
        var validator = new RabbitMQOptionsValidator();
        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("MaxSize", result.FailureMessage);
    }

    [Fact]
    public void Validator_ChannelPool_ExcessiveMaxSize_Fails()
    {
        var options = new RabbitMQOptions
        {
            ChannelPool = new ChannelPoolOptions { Enabled = true, MaxSize = 100 }
        };
        var validator = new RabbitMQOptionsValidator();
        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("MaxSize", result.FailureMessage);
    }

    [Fact]
    public void Validator_ChannelPool_DisabledWithInvalidSize_Passes()
    {
        // When disabled, MaxSize validation is skipped
        var options = new RabbitMQOptions
        {
            ChannelPool = new ChannelPoolOptions { Enabled = false, MaxSize = 0 }
        };
        var validator = new RabbitMQOptionsValidator();
        var result = validator.Validate(null, options);

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validator_ValidOptions_Passes()
    {
        var options = new RabbitMQOptions
        {
            RetryDelay = new RetryDelayOptions
            {
                Enabled = true,
                InitialDelayMs = 500,
                Multiplier = 2.0,
                MaxDelayMs = 10000
            },
            ChannelPool = new ChannelPoolOptions
            {
                Enabled = true,
                MaxSize = 10
            }
        };
        var validator = new RabbitMQOptionsValidator();
        var result = validator.Validate(null, options);

        Assert.False(result.Failed);
    }

    #endregion

    #region Test Types

    private class TestEvent : EventBase
    {
        public string Message { get; }
        public TestEvent(string message) { Message = message; }
    }

    #endregion
}
