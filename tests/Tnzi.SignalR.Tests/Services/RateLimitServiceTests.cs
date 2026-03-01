namespace Tnzi.SignalR.Tests.Services;

/// <summary>
/// Tests for RateLimitService
/// </summary>
public class RateLimitServiceTests
{
    private readonly Mock<ICache> _cacheMock;
    private readonly ConnectionManager _connectionManager;
    private readonly RateLimitService _rateLimitService;

    public RateLimitServiceTests()
    {
        _cacheMock = new Mock<ICache>();
        var loggerMock = new Mock<ILogger<ConnectionManager>>();
        _connectionManager = new ConnectionManager(loggerMock.Object);

        var options = new SignalROptions
        {
            RateLimit = new RateLimitOptions
            {
                Enabled = true,
                MaxConnectionsPerUser = 3,
                MaxMessagesPerMinute = 10,
                BanDuration = TimeSpan.FromMinutes(5),
            }
        };
        var optionsMock = new Mock<IOptions<SignalROptions>>();
        optionsMock.Setup(o => o.Value).Returns(options);

        var rateLimitLoggerMock = new Mock<ILogger<RateLimitService>>();
        _rateLimitService = new RateLimitService(
            _cacheMock.Object,
            _connectionManager,
            optionsMock.Object,
            rateLimitLoggerMock.Object);
    }

    [Fact]
    public async Task CheckConnectionLimitAsync_Should_Allow_Under_Limit()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn2");

        // Act
        var result = await _rateLimitService.CheckConnectionLimitAsync(userId);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckConnectionLimitAsync_Should_Reject_At_Limit()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _connectionManager.AddConnectionAsync(userId, "conn1");
        await _connectionManager.AddConnectionAsync(userId, "conn2");
        await _connectionManager.AddConnectionAsync(userId, "conn3");

        // Act
        var result = await _rateLimitService.CheckConnectionLimitAsync(userId);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckMessageRateLimitAsync_Should_Allow_Under_Limit()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var cacheKey = CacheKeys.SignalR.MessageRateCount(userId);
        _cacheMock.Setup(c => c.GetAsync<int?>(cacheKey, default))
            .ReturnsAsync(5);

        // Act
        var result = await _rateLimitService.CheckMessageRateLimitAsync(userId);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckMessageRateLimitAsync_Should_Reject_At_Limit()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var cacheKey = CacheKeys.SignalR.MessageRateCount(userId);
        _cacheMock.Setup(c => c.GetAsync<int?>(cacheKey, default))
            .ReturnsAsync(10);

        // Act
        var result = await _rateLimitService.CheckMessageRateLimitAsync(userId);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task IsUserBannedAsync_Should_Return_False_When_Not_Banned()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var cacheKey = CacheKeys.SignalR.UserBan(userId);
        _cacheMock.Setup(c => c.ExistsAsync(cacheKey, default))
            .ReturnsAsync(false);

        // Act
        var result = await _rateLimitService.IsUserBannedAsync(userId);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task IsUserBannedAsync_Should_Return_True_When_Banned()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var cacheKey = CacheKeys.SignalR.UserBan(userId);
        _cacheMock.Setup(c => c.ExistsAsync(cacheKey, default))
            .ReturnsAsync(true);

        // Act
        var result = await _rateLimitService.IsUserBannedAsync(userId);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task BanUserAsync_Should_Set_Cache_Entry()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var duration = TimeSpan.FromMinutes(10);
        var cacheKey = CacheKeys.SignalR.UserBan(userId);

        // Act
        await _rateLimitService.BanUserAsync(userId, duration);

        // Assert
        _cacheMock.Verify(c => c.SetAsync(cacheKey, true, duration, default), Times.Once);
    }
}
