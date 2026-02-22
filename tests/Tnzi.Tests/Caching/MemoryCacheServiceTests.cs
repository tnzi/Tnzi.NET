
namespace Tnzi.Tests.Caching;

/// <summary>
/// MemoryCacheService 测试类
/// </summary>
public class MemoryCacheServiceTests
{
    private readonly MemoryCacheService _cache;
    private readonly IMemoryCache _memoryCache;

    public MemoryCacheServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<MemoryCacheService>>();
        var cachingOptions = Microsoft.Extensions.Options.Options.Create(new CachingOptions());
        _cache = new MemoryCacheService(_memoryCache, logger, cachingOptions);
    }

    [Fact]
    public async Task GetAsync_WhenKeyNotExists_ReturnsNull()
    {
        // Act
        var result = await _cache.GetAsync<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsValue()
    {
        // Arrange
        const string key = "test-key";
        const string value = "test-value";

        // Act
        await _cache.SetAsync(key, value);
        var result = await _cache.GetAsync<string>(key);

        // Assert
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task RemoveAsync_RemovesKey()
    {
        // Arrange
        const string key = "test-key";
        await _cache.SetAsync(key, "test-value");

        // Act
        await _cache.RemoveAsync(key);
        var result = await _cache.GetAsync<string>(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyExists_ReturnsTrue()
    {
        // Arrange
        const string key = "test-key";
        await _cache.SetAsync(key, "test-value");

        // Act
        var exists = await _cache.ExistsAsync(key);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyNotExists_ReturnsFalse()
    {
        // Act
        var exists = await _cache.ExistsAsync("nonexistent");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task IncrementAsync_IncrementsValue()
    {
        // Arrange
        const string key = "counter";

        // Act
        var result1 = await _cache.IncrementAsync(key);
        var result2 = await _cache.IncrementAsync(key, 5);
        var result3 = await _cache.IncrementAsync(key, 10);

        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(6, result2);
        Assert.Equal(16, result3);
    }

    [Fact]
    public async Task DecrementAsync_DecrementsValue()
    {
        // Arrange
        const string key = "counter";
        await _cache.SetAsync(key, 100L);

        // Act
        var result = await _cache.DecrementAsync(key, 10);

        // Assert
        Assert.Equal(90, result);
    }

    [Fact]
    public async Task TrySetAsync_WhenKeyNotExists_SetsValueAndReturnsTrue()
    {
        // Arrange
        const string key = "test-key";
        const string value = "test-value";

        // Act
        var result = await _cache.TrySetAsync(key, value);
        var storedValue = await _cache.GetAsync<string>(key);

        // Assert
        Assert.True(result);
        Assert.Equal(value, storedValue);
    }

    [Fact]
    public async Task TrySetAsync_WhenKeyExists_ReturnsFalse()
    {
        // Arrange
        const string key = "test-key";
        await _cache.SetAsync(key, "existing-value");

        // Act
        var result = await _cache.TrySetAsync(key, "new-value");
        var storedValue = await _cache.GetAsync<string>(key);

        // Assert
        Assert.False(result);
        Assert.Equal("existing-value", storedValue);
    }

    [Fact]
    public async Task RemoveByPatternAsync_RemovesMatchingKeys()
    {
        // Arrange
        await _cache.SetAsync("user:1:name", "Alice");
        await _cache.SetAsync("user:2:name", "Bob");
        await _cache.SetAsync("user:3:email", "charlie@test.com");
        await _cache.SetAsync("product:1:name", "Widget");

        // Act
        await _cache.RemoveByPatternAsync("user:*:name");

        // Assert
        Assert.False(await _cache.ExistsAsync("user:1:name"));
        Assert.False(await _cache.ExistsAsync("user:2:name"));
        Assert.True(await _cache.ExistsAsync("user:3:email")); // 不匹配模式
        Assert.True(await _cache.ExistsAsync("product:1:name")); // 不匹配模式
    }

    [Fact]
    public async Task RemoveByPatternAsync_WithQuestionMark_MatchesSingleCharacter()
    {
        // Arrange
        await _cache.SetAsync("item:1", "value1");
        await _cache.SetAsync("item:2", "value2");
        await _cache.SetAsync("item:10", "value10");

        // Act
        await _cache.RemoveByPatternAsync("item:?");

        // Assert
        Assert.False(await _cache.ExistsAsync("item:1"));
        Assert.False(await _cache.ExistsAsync("item:2"));
        Assert.True(await _cache.ExistsAsync("item:10")); // 两位数不匹配 ?
    }
}