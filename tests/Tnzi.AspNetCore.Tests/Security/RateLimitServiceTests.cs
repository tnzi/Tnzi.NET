
namespace Tnzi.AspNetCore.Tests.Security;

/// <summary>
/// 限流服务测试
/// </summary>
public class RateLimitServiceTests
{
    private readonly Mock<ICache> _cacheMock;
    private readonly RateLimitService _service;

    public RateLimitServiceTests()
    {
        _cacheMock = new Mock<ICache>();
        _service = new RateLimitService(_cacheMock.Object);
    }

    [Fact]
    public async Task IncrementAndGetAsync_WithFixedWindow_ShouldIncrementWithExpiration()
    {
        // Arrange
        var key = "test-key";
        var windowSeconds = 60;
        var cacheKey = "RateLimit:fixed:test-key";
        var expiration = TimeSpan.FromSeconds(windowSeconds);

        _cacheMock.Setup(c => c.IncrementAsync(cacheKey, 1, expiration, default))
            .ReturnsAsync(1L);

        // Act
        var result = await _service.IncrementAndGetAsync(key, windowSeconds, RateLimitAlgorithm.FixedWindow);

        // Assert
        Assert.Equal(1L, result);
        _cacheMock.Verify(c => c.IncrementAsync(cacheKey, 1, expiration, default), Times.Once);
    }

    [Fact]
    public async Task IncrementAndGetAsync_WithFixedWindow_ShouldReturnIncrementedCount()
    {
        // Arrange
        var key = "test-key";
        var windowSeconds = 60;
        var cacheKey = "RateLimit:fixed:test-key";
        var expiration = TimeSpan.FromSeconds(windowSeconds);

        _cacheMock.Setup(c => c.IncrementAsync(cacheKey, 1, expiration, default))
            .ReturnsAsync(5L);

        // Act
        var result = await _service.IncrementAndGetAsync(key, windowSeconds, RateLimitAlgorithm.FixedWindow);

        // Assert
        Assert.Equal(5L, result);
    }

    [Fact]
    public async Task IncrementAndGetAsync_WithTokenBucket_ShouldThrowNotSupportedException()
    {
        // Arrange
        var key = "test-key";
        var windowSeconds = 60;

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _service.IncrementAndGetAsync(key, windowSeconds, RateLimitAlgorithm.TokenBucket));
    }

    [Fact]
    public async Task IncrementAndGetAsync_WithLeakyBucket_ShouldThrowNotSupportedException()
    {
        // Arrange
        var key = "test-key";
        var windowSeconds = 60;

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _service.IncrementAndGetAsync(key, windowSeconds, RateLimitAlgorithm.LeakyBucket));
    }

    [Fact]
    public async Task IncrementAndGetAsync_WithDefaultAlgorithm_ShouldUseFixedWindow()
    {
        // Arrange
        var key = "test-key";
        var windowSeconds = 60;
        var cacheKey = "RateLimit:fixed:test-key";
        var expiration = TimeSpan.FromSeconds(windowSeconds);

        _cacheMock.Setup(c => c.IncrementAsync(cacheKey, 1, expiration, default))
            .ReturnsAsync(1L);

        // Act
        var result = await _service.IncrementAndGetAsync(key, windowSeconds);

        // Assert
        Assert.Equal(1L, result);
        _cacheMock.Verify(c => c.IncrementAsync(cacheKey, 1, expiration, default), Times.Once);
    }
}
