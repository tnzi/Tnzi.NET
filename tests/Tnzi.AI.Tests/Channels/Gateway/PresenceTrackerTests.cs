using Tnzi.AI.Channels.Gateway;
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
}
