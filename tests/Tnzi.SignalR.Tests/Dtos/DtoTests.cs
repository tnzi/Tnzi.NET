namespace Tnzi.SignalR.Tests.Dtos;

/// <summary>
/// Tests for SignalR DTOs
/// </summary>
public class DtoTests
{
    [Fact]
    public void SignalRStatsDto_Should_Have_Defaults()
    {
        // Act
        var dto = new SignalRStatsDto();

        // Assert
        dto.OnlineUserCount.ShouldBe(0);
        dto.TotalConnectionCount.ShouldBe(0);
        dto.Timestamp.ShouldBeGreaterThan(DateTime.MinValue);
    }

    [Fact]
    public void OnlineUserDto_Should_Have_Defaults()
    {
        // Act
        var dto = new OnlineUserDto();

        // Assert
        dto.UserId.ShouldBe(Guid.Empty);
        dto.ConnectionCount.ShouldBe(0);
        dto.Connections.ShouldNotBeNull();
        dto.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void ConnectionInfoDto_Should_Have_Defaults()
    {
        // Act
        var dto = new ConnectionInfoDto();

        // Assert
        dto.ConnectionId.ShouldBe(string.Empty);
        dto.UserId.ShouldBeNull();
        dto.UserName.ShouldBeNull();
        dto.ConnectedAt.ShouldBeNull();
        dto.IpAddress.ShouldBeNull();
        dto.UserAgent.ShouldBeNull();
        dto.HubName.ShouldBeNull();
        dto.Groups.ShouldNotBeNull();
        dto.Groups.ShouldBeEmpty();
    }

    [Fact]
    public void ConnectionMetadata_Should_Have_Defaults()
    {
        // Act
        var metadata = new ConnectionMetadata();

        // Assert
        metadata.ConnectionId.ShouldBe(string.Empty);
        metadata.UserId.ShouldBeNull();
        metadata.UserName.ShouldBeNull();
        metadata.ConnectedAt.ShouldBeGreaterThan(DateTime.MinValue);
        metadata.IpAddress.ShouldBeNull();
        metadata.UserAgent.ShouldBeNull();
        metadata.HubName.ShouldBeNull();
        metadata.Properties.ShouldNotBeNull();
        metadata.Properties.ShouldBeEmpty();
    }

    [Fact]
    public void ConnectionMetadata_Properties_Should_Be_Extensible()
    {
        // Arrange
        var metadata = new ConnectionMetadata();

        // Act
        metadata.Properties["deviceType"] = "mobile";
        metadata.Properties["appVersion"] = "2.1.0";

        // Assert
        metadata.Properties.Count.ShouldBe(2);
        metadata.Properties["deviceType"].ShouldBe("mobile");
        metadata.Properties["appVersion"].ShouldBe("2.1.0");
    }
}
