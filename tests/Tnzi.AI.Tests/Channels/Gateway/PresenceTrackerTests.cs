using Tnzi.AI.Channels.Gateway;
using Tnzi.AI.Channels.Gateway.Events;
using Tnzi.AI.Channels.Gateway.Models;

namespace Tnzi.AI.Tests.Channels.Gateway;

public class PresenceTrackerTests
{
    private readonly DefaultPresenceTracker _tracker = new(NullLogger<DefaultPresenceTracker>.Instance);

    private static GatewayConnectionInfo CreateConnection(string id = "conn-1", string? userId = "user-1", string clientType = "web")
    {
        return new GatewayConnectionInfo
        {
            ConnectionId = id,
            UserId = userId,
            ClientType = clientType,
            ConnectedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public void TrackConnection_AddsToList()
    {
        // Arrange
        var connection = CreateConnection();

        // Act
        _tracker.TrackConnection(connection);

        // Assert
        var connections = _tracker.GetConnections();
        connections.Count.ShouldBe(1);
        connections[0].ConnectionId.ShouldBe("conn-1");
    }

    [Fact]
    public void RemoveConnection_RemovesFromList()
    {
        // Arrange
        _tracker.TrackConnection(CreateConnection("conn-1"));
        _tracker.TrackConnection(CreateConnection("conn-2"));

        // Act
        _tracker.RemoveConnection("conn-1");

        // Assert
        var connections = _tracker.GetConnections();
        connections.Count.ShouldBe(1);
        connections[0].ConnectionId.ShouldBe("conn-2");
    }

    [Fact]
    public void OnPresenceChanged_FiresOnTrack()
    {
        // Arrange
        PresenceChangedEvent? received = null;
        _tracker.OnPresenceChanged += e => received = e;

        // Act
        _tracker.TrackConnection(CreateConnection("conn-1", "user-1", "cli"));

        // Assert
        received.ShouldNotBeNull();
        received.ConnectionId.ShouldBe("conn-1");
        received.UserId.ShouldBe("user-1");
        received.ClientType.ShouldBe("cli");
        received.IsOnline.ShouldBeTrue();
    }

    [Fact]
    public void OnPresenceChanged_FiresOnRemove()
    {
        // Arrange
        _tracker.TrackConnection(CreateConnection("conn-1", "user-1", "web"));
        PresenceChangedEvent? received = null;
        _tracker.OnPresenceChanged += e => received = e;

        // Act
        _tracker.RemoveConnection("conn-1");

        // Assert
        received.ShouldNotBeNull();
        received.ConnectionId.ShouldBe("conn-1");
        received.IsOnline.ShouldBeFalse();
    }

    [Fact]
    public void RemoveConnection_NonExistent_NoOp()
    {
        // Arrange
        var eventFired = false;
        _tracker.OnPresenceChanged += _ => eventFired = true;

        // Act
        _tracker.RemoveConnection("non-existent");

        // Assert
        eventFired.ShouldBeFalse();
        _tracker.GetConnections().Count.ShouldBe(0);
    }

    [Fact]
    public void GetConnections_ThreadSafe()
    {
        // Act - add 100 connections in parallel
        Parallel.For(0, 100, i =>
        {
            _tracker.TrackConnection(CreateConnection($"conn-{i}"));
        });

        // Assert
        var connections = _tracker.GetConnections();
        connections.Count.ShouldBe(100);
    }

    // The tracking API is synchronous, so the publish is not awaited; a publish that fails
    // asynchronously must still leave a trace instead of vanishing with the discarded task.
    [Fact]
    public async Task TrackAndRemove_PublishFailsAsynchronously_IsLogged()
    {
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(b => b.PublishAsync(It.IsAny<GatewayClientConnectedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(FailLaterAsync());
        eventBus
            .Setup(b => b.PublishAsync(It.IsAny<GatewayClientDisconnectedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(FailLaterAsync());
        var logger = new WarningCounter();
        var tracker = new DefaultPresenceTracker(logger, eventBus.Object);

        tracker.TrackConnection(CreateConnection("conn-1"));
        tracker.RemoveConnection("conn-1");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (logger.Warnings < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        logger.Warnings.ShouldBe(2);
    }

    private static async Task FailLaterAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("broker unavailable");
    }

    private sealed class WarningCounter : ILogger<DefaultPresenceTracker>
    {
        private int _warnings;

        public int Warnings => Volatile.Read(ref _warnings);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning && exception is not null)
            {
                Interlocked.Increment(ref _warnings);
            }
        }
    }
}
