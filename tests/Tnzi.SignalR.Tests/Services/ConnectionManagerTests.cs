
namespace Tnzi.SignalR.Tests.Services;

/// <summary>
/// ConnectionManager 单元测试（基于内存实现）
/// </summary>
public class ConnectionManagerTests
{
    private readonly ConnectionManager _connectionManager;

    public ConnectionManagerTests()
    {
        var loggerMock = new Mock<ILogger<ConnectionManager>>();
        _connectionManager = new ConnectionManager(loggerMock.Object);
    }

    [Fact]
    public async Task GetUserConnectionsAsync_Should_Return_Empty_When_No_Connections()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var result = await _connectionManager.GetUserConnectionsAsync(userId);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetUserConnectionsAsync_Should_Return_Connections_When_Exist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn2");
        await _connectionManager.AddConnectionAsync(userId, "conn3");

        // Act
        var result = await _connectionManager.GetUserConnectionsAsync(userId);

        // Assert
        result.ShouldNotBeNull();
        var resultList = result.ToList();
        resultList.Count.ShouldBe(3);
        resultList.ShouldContain("conn1");
        resultList.ShouldContain("conn2");
        resultList.ShouldContain("conn3");
    }

    [Fact]
    public async Task AddConnectionAsync_Should_Add_New_Connection()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn2");

        // Act
        await _connectionManager.AddConnectionAsync(userId, "conn1");

        // Assert
        var result = await _connectionManager.GetUserConnectionsAsync(userId);
        var resultList = result.ToList();
        resultList.ShouldContain("conn1");
        resultList.ShouldContain("conn2");
        resultList.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AddConnectionAsync_Should_Create_New_Set_When_None_Exist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _connectionManager.AddConnectionAsync(userId, "conn1");

        // Assert
        var result = await _connectionManager.GetUserConnectionsAsync(userId);
        var resultList = result.ToList();
        resultList.ShouldContain("conn1");
        resultList.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RemoveConnectionAsync_Should_Remove_Connection()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn2");

        // Act
        await _connectionManager.RemoveConnectionAsync(userId, "conn1");

        // Assert
        var result = await _connectionManager.GetUserConnectionsAsync(userId);
        var resultList = result.ToList();
        resultList.ShouldNotContain("conn1");
        resultList.ShouldContain("conn2");
        resultList.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RemoveConnectionAsync_Should_Clean_Up_When_No_Connections_Left()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");

        // Act
        await _connectionManager.RemoveConnectionAsync(userId, "conn1");

        // Assert
        var result = await _connectionManager.GetUserConnectionsAsync(userId);
        result.ShouldBeEmpty();
        var isOnline = await _connectionManager.IsUserOnlineAsync(userId);
        isOnline.ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveUserConnectionsAsync_Should_Remove_All_Connections()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn2");
        await _connectionManager.AddConnectionAsync(userId, "conn3");

        // Act
        await _connectionManager.RemoveUserConnectionsAsync(userId);

        // Assert
        var result = await _connectionManager.GetUserConnectionsAsync(userId);
        result.ShouldBeEmpty();
        var isOnline = await _connectionManager.IsUserOnlineAsync(userId);
        isOnline.ShouldBeFalse();
    }

    [Fact]
    public async Task IsUserOnlineAsync_Should_Return_True_When_Has_Connections()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn2");

        // Act
        var result = await _connectionManager.IsUserOnlineAsync(userId);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task IsUserOnlineAsync_Should_Return_False_When_No_Connections()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var result = await _connectionManager.IsUserOnlineAsync(userId);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task GetConnectionCountAsync_Should_Return_Correct_Count()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn2");
        await _connectionManager.AddConnectionAsync(userId, "conn3");

        // Act
        var count = await _connectionManager.GetConnectionCountAsync(userId);

        // Assert
        count.ShouldBe(3);
    }

    [Fact]
    public async Task GetConnectionCountAsync_Should_Return_Zero_When_No_Connections()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var count = await _connectionManager.GetConnectionCountAsync(userId);

        // Assert
        count.ShouldBe(0);
    }

    [Fact]
    public async Task GetOnlineUserCountAsync_Should_Return_Correct_Count()
    {
        // Arrange
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId1, "conn1");
        await _connectionManager.AddConnectionAsync(userId2, "conn2");

        // Act
        var count = await _connectionManager.GetOnlineUserCountAsync();

        // Assert
        count.ShouldBe(2);
    }

    [Fact]
    public async Task GetOnlineUserCountAsync_Should_Return_Zero_When_No_Users()
    {
        // Act
        var count = await _connectionManager.GetOnlineUserCountAsync();

        // Assert
        count.ShouldBe(0);
    }

    [Fact]
    public async Task AddConnectionAsync_Should_Be_Idempotent()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act - 添加相同连接两次
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn1");

        // Assert - 应该只有一个连接
        var count = await _connectionManager.GetConnectionCountAsync(userId);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task RemoveConnectionAsync_Should_Handle_NonExistent_Connection()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act & Assert - 不应该抛出异常
        await _connectionManager.RemoveConnectionAsync(userId, "nonexistent");

        var result = await _connectionManager.GetUserConnectionsAsync(userId);
        result.ShouldBeEmpty();
    }
}
