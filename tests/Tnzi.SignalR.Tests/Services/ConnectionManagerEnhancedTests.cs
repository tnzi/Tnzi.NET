namespace Tnzi.SignalR.Tests.Services;

/// <summary>
/// Enhanced ConnectionManager tests covering metadata, groups, and admin queries
/// </summary>
public class ConnectionManagerEnhancedTests
{
    private readonly ConnectionManager _connectionManager;

    public ConnectionManagerEnhancedTests()
    {
        var loggerMock = new Mock<ILogger<ConnectionManager>>();
        _connectionManager = new ConnectionManager(loggerMock.Object);
    }

    #region GetAllOnlineUserIdsAsync

    [Fact]
    public async Task GetAllOnlineUserIdsAsync_Should_Return_Empty_When_No_Users()
    {
        // Act
        var result = await _connectionManager.GetAllOnlineUserIdsAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAllOnlineUserIdsAsync_Should_Return_All_Online_Users()
    {
        // Arrange
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        var userId3 = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId1, "conn1");
        await _connectionManager.AddConnectionAsync(userId2, "conn2");
        await _connectionManager.AddConnectionAsync(userId3, "conn3");

        // Act
        var result = await _connectionManager.GetAllOnlineUserIdsAsync();

        // Assert
        var resultList = result.ToList();
        resultList.Count.ShouldBe(3);
        resultList.ShouldContain(userId1);
        resultList.ShouldContain(userId2);
        resultList.ShouldContain(userId3);
    }

    [Fact]
    public async Task GetAllOnlineUserIdsAsync_Should_Exclude_Removed_Users()
    {
        // Arrange
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId1, "conn1");
        await _connectionManager.AddConnectionAsync(userId2, "conn2");
        await _connectionManager.RemoveConnectionAsync(userId1, "conn1");

        // Act
        var result = await _connectionManager.GetAllOnlineUserIdsAsync();

        // Assert
        var resultList = result.ToList();
        resultList.Count.ShouldBe(1);
        resultList.ShouldContain(userId2);
    }

    #endregion

    #region GetTotalConnectionCountAsync

    [Fact]
    public async Task GetTotalConnectionCountAsync_Should_Return_Zero_When_No_Connections()
    {
        // Act
        var count = await _connectionManager.GetTotalConnectionCountAsync();

        // Assert
        count.ShouldBe(0);
    }

    [Fact]
    public async Task GetTotalConnectionCountAsync_Should_Count_All_Connections()
    {
        // Arrange
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId1, "conn1");
        await _connectionManager.AddConnectionAsync(userId1, "conn2");
        await _connectionManager.AddConnectionAsync(userId2, "conn3");

        // Act
        var count = await _connectionManager.GetTotalConnectionCountAsync();

        // Assert
        count.ShouldBe(3);
    }

    [Fact]
    public async Task GetTotalConnectionCountAsync_Should_Decrease_After_Remove()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn2");
        await _connectionManager.RemoveConnectionAsync(userId, "conn1");

        // Act
        var count = await _connectionManager.GetTotalConnectionCountAsync();

        // Assert
        count.ShouldBe(1);
    }

    #endregion

    #region Metadata

    [Fact]
    public async Task AddConnectionAsync_With_Metadata_Should_Store_Metadata()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var metadata = new ConnectionMetadata
        {
            IpAddress = "192.168.1.1",
            UserAgent = "TestAgent/1.0",
            HubName = "TestHub",
            UserName = "testuser",
        };

        // Act
        await _connectionManager.AddConnectionAsync(userId, "conn1", metadata);

        // Assert
        var stored = await _connectionManager.GetConnectionMetadataAsync("conn1");
        stored.ShouldNotBeNull();
        stored.IpAddress.ShouldBe("192.168.1.1");
        stored.UserAgent.ShouldBe("TestAgent/1.0");
        stored.HubName.ShouldBe("TestHub");
        stored.UserName.ShouldBe("testuser");
        stored.UserId.ShouldBe(userId);
        stored.ConnectionId.ShouldBe("conn1");
    }

    [Fact]
    public async Task GetConnectionMetadataAsync_Should_Return_Null_For_Unknown_Connection()
    {
        // Act
        var result = await _connectionManager.GetConnectionMetadataAsync("unknown");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task RemoveConnectionAsync_Should_Clean_Metadata()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var metadata = new ConnectionMetadata { IpAddress = "1.2.3.4" };
        await _connectionManager.AddConnectionAsync(userId, "conn1", metadata);

        // Act
        await _connectionManager.RemoveConnectionAsync(userId, "conn1");

        // Assert
        var stored = await _connectionManager.GetConnectionMetadataAsync("conn1");
        stored.ShouldBeNull();
    }

    [Fact]
    public async Task RemoveUserConnectionsAsync_Should_Clean_All_Metadata()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1", new ConnectionMetadata { IpAddress = "1.1.1.1" });
        await _connectionManager.AddConnectionAsync(userId, "conn2", new ConnectionMetadata { IpAddress = "2.2.2.2" });

        // Act
        await _connectionManager.RemoveUserConnectionsAsync(userId);

        // Assert
        (await _connectionManager.GetConnectionMetadataAsync("conn1")).ShouldBeNull();
        (await _connectionManager.GetConnectionMetadataAsync("conn2")).ShouldBeNull();
    }

    [Fact]
    public async Task ConnectionMetadata_ConnectedAt_Should_Be_Set()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var before = DateTime.UtcNow;
        var metadata = new ConnectionMetadata();

        // Act
        await _connectionManager.AddConnectionAsync(userId, "conn1", metadata);

        // Assert
        var stored = await _connectionManager.GetConnectionMetadataAsync("conn1");
        stored.ShouldNotBeNull();
        stored.ConnectedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
    }

    #endregion

    #region GetUserIdByConnectionAsync

    [Fact]
    public async Task GetUserIdByConnectionAsync_Should_Return_UserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");

        // Act
        var result = await _connectionManager.GetUserIdByConnectionAsync("conn1");

        // Assert
        result.ShouldBe(userId);
    }

    [Fact]
    public async Task GetUserIdByConnectionAsync_Should_Return_Null_For_Unknown()
    {
        // Act
        var result = await _connectionManager.GetUserIdByConnectionAsync("unknown");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetUserIdByConnectionAsync_Should_Return_Null_After_Removal()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.RemoveConnectionAsync(userId, "conn1");

        // Act
        var result = await _connectionManager.GetUserIdByConnectionAsync("conn1");

        // Assert
        result.ShouldBeNull();
    }

    #endregion

    #region Group Management

    [Fact]
    public async Task AddToGroupAsync_Should_Track_Group_Membership()
    {
        // Arrange
        await _connectionManager.AddConnectionAsync(Guid.NewGuid(), "conn1");

        // Act
        await _connectionManager.AddToGroupAsync("conn1", "group1");

        // Assert
        var groups = await _connectionManager.GetConnectionGroupsAsync("conn1");
        groups.ShouldContain("group1");
    }

    [Fact]
    public async Task AddToGroupAsync_Should_Allow_Multiple_Groups()
    {
        // Arrange
        await _connectionManager.AddConnectionAsync(Guid.NewGuid(), "conn1");

        // Act
        await _connectionManager.AddToGroupAsync("conn1", "group1");
        await _connectionManager.AddToGroupAsync("conn1", "group2");
        await _connectionManager.AddToGroupAsync("conn1", "group3");

        // Assert
        var groups = (await _connectionManager.GetConnectionGroupsAsync("conn1")).ToList();
        groups.Count.ShouldBe(3);
        groups.ShouldContain("group1");
        groups.ShouldContain("group2");
        groups.ShouldContain("group3");
    }

    [Fact]
    public async Task GetGroupConnectionsAsync_Should_Return_All_Connections_In_Group()
    {
        // Arrange
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(user1, "conn1");
        await _connectionManager.AddConnectionAsync(user2, "conn2");
        await _connectionManager.AddToGroupAsync("conn1", "group1");
        await _connectionManager.AddToGroupAsync("conn2", "group1");

        // Act
        var connections = (await _connectionManager.GetGroupConnectionsAsync("group1")).ToList();

        // Assert
        connections.Count.ShouldBe(2);
        connections.ShouldContain("conn1");
        connections.ShouldContain("conn2");
    }

    [Fact]
    public async Task RemoveFromGroupAsync_Should_Remove_Membership()
    {
        // Arrange
        await _connectionManager.AddConnectionAsync(Guid.NewGuid(), "conn1");
        await _connectionManager.AddToGroupAsync("conn1", "group1");
        await _connectionManager.AddToGroupAsync("conn1", "group2");

        // Act
        await _connectionManager.RemoveFromGroupAsync("conn1", "group1");

        // Assert
        var groups = (await _connectionManager.GetConnectionGroupsAsync("conn1")).ToList();
        groups.Count.ShouldBe(1);
        groups.ShouldContain("group2");
        groups.ShouldNotContain("group1");
    }

    [Fact]
    public async Task RemoveConnectionAsync_Should_Clean_Group_Memberships()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddToGroupAsync("conn1", "group1");
        await _connectionManager.AddToGroupAsync("conn1", "group2");

        // Act
        await _connectionManager.RemoveConnectionAsync(userId, "conn1");

        // Assert
        var groupConns = (await _connectionManager.GetGroupConnectionsAsync("group1")).ToList();
        groupConns.ShouldBeEmpty();
        var connGroups = (await _connectionManager.GetConnectionGroupsAsync("conn1")).ToList();
        connGroups.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetGroupConnectionsAsync_Should_Return_Empty_For_Unknown_Group()
    {
        // Act
        var result = await _connectionManager.GetGroupConnectionsAsync("unknown");

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetConnectionGroupsAsync_Should_Return_Empty_For_Unknown_Connection()
    {
        // Act
        var result = await _connectionManager.GetConnectionGroupsAsync("unknown");

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveUserConnectionsAsync_Should_Clean_Group_Memberships()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn2");
        await _connectionManager.AddToGroupAsync("conn1", "group1");
        await _connectionManager.AddToGroupAsync("conn2", "group1");
        await _connectionManager.AddToGroupAsync("conn2", "group2");

        // Act
        await _connectionManager.RemoveUserConnectionsAsync(userId);

        // Assert
        (await _connectionManager.GetGroupConnectionsAsync("group1")).ShouldBeEmpty();
        (await _connectionManager.GetGroupConnectionsAsync("group2")).ShouldBeEmpty();
    }

    #endregion

    #region Concurrent Access

    [Fact]
    public async Task Should_Handle_Concurrent_AddRemove()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tasks = new List<Task>();

        // Act - concurrent adds
        for (int i = 0; i < 100; i++)
        {
            var connId = $"conn{i}";
            tasks.Add(Task.Run(async () => await _connectionManager.AddConnectionAsync(userId, connId)));
        }
        await Task.WhenAll(tasks);

        // Assert - all connections added
        var count = await _connectionManager.GetConnectionCountAsync(userId);
        count.ShouldBe(100);

        // Act - concurrent removes
        tasks.Clear();
        for (int i = 0; i < 100; i++)
        {
            var connId = $"conn{i}";
            tasks.Add(Task.Run(async () => await _connectionManager.RemoveConnectionAsync(userId, connId)));
        }
        await Task.WhenAll(tasks);

        // Assert - all connections removed
        count = await _connectionManager.GetConnectionCountAsync(userId);
        count.ShouldBe(0);
        (await _connectionManager.IsUserOnlineAsync(userId)).ShouldBeFalse();
    }

    #endregion
}
