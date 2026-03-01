namespace Tnzi.SignalR.Tests.Events;

/// <summary>
/// Tests for SignalR connection events
/// </summary>
public class ConnectionEventsTests
{
    [Fact]
    public void UserConnectedEvent_Should_Initialize_With_Defaults()
    {
        // Act
        var evt = new UserConnectedEvent();

        // Assert
        evt.EventId.ShouldNotBe(Guid.Empty);
        evt.EventTime.ShouldBeGreaterThan(DateTime.MinValue);
        evt.ConnectionId.ShouldBe(string.Empty);
        evt.HubName.ShouldBe(string.Empty);
        evt.UserId.ShouldBe(Guid.Empty);
        evt.UserName.ShouldBeNull();
        evt.IpAddress.ShouldBeNull();
        evt.TotalConnections.ShouldBe(0);
    }

    [Fact]
    public void UserConnectedEvent_Should_Set_Properties()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var evt = new UserConnectedEvent
        {
            UserId = userId,
            ConnectionId = "conn-123",
            HubName = "ChatHub",
            UserName = "testuser",
            IpAddress = "192.168.1.1",
            TotalConnections = 3,
        };

        // Assert
        evt.UserId.ShouldBe(userId);
        evt.ConnectionId.ShouldBe("conn-123");
        evt.HubName.ShouldBe("ChatHub");
        evt.UserName.ShouldBe("testuser");
        evt.IpAddress.ShouldBe("192.168.1.1");
        evt.TotalConnections.ShouldBe(3);
    }

    [Fact]
    public void UserDisconnectedEvent_Should_Initialize_With_Defaults()
    {
        // Act
        var evt = new UserDisconnectedEvent();

        // Assert
        evt.EventId.ShouldNotBe(Guid.Empty);
        evt.EventTime.ShouldBeGreaterThan(DateTime.MinValue);
        evt.ConnectionId.ShouldBe(string.Empty);
        evt.HubName.ShouldBe(string.Empty);
        evt.UserId.ShouldBe(Guid.Empty);
        evt.Reason.ShouldBeNull();
        evt.RemainingConnections.ShouldBe(0);
        evt.WentOffline.ShouldBeFalse();
    }

    [Fact]
    public void UserDisconnectedEvent_Should_Set_Properties()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var evt = new UserDisconnectedEvent
        {
            UserId = userId,
            ConnectionId = "conn-456",
            HubName = "NotificationHub",
            Reason = "Client timeout",
            RemainingConnections = 2,
            WentOffline = false,
        };

        // Assert
        evt.UserId.ShouldBe(userId);
        evt.ConnectionId.ShouldBe("conn-456");
        evt.HubName.ShouldBe("NotificationHub");
        evt.Reason.ShouldBe("Client timeout");
        evt.RemainingConnections.ShouldBe(2);
        evt.WentOffline.ShouldBeFalse();
    }

    [Fact]
    public void UserDisconnectedEvent_WentOffline_Should_Be_True_When_No_Remaining()
    {
        // Act
        var evt = new UserDisconnectedEvent
        {
            RemainingConnections = 0,
            WentOffline = true,
        };

        // Assert
        evt.WentOffline.ShouldBeTrue();
        evt.RemainingConnections.ShouldBe(0);
    }

    [Fact]
    public void Events_Should_Implement_IEvent()
    {
        // Act
        var connectedEvent = new UserConnectedEvent();
        var disconnectedEvent = new UserDisconnectedEvent();

        // Assert
        connectedEvent.ShouldBeAssignableTo<IEvent>();
        disconnectedEvent.ShouldBeAssignableTo<IEvent>();
    }

    [Fact]
    public void Events_Should_Have_Unique_EventIds()
    {
        // Act
        var event1 = new UserConnectedEvent();
        var event2 = new UserConnectedEvent();

        // Assert
        event1.EventId.ShouldNotBe(event2.EventId);
    }
}
