
namespace Tnzi.Tests.Caching;

/// <summary>
/// 缓存批量操作测试
/// </summary>
public class CacheBatchOperationsTests
{
    private readonly MemoryCacheService _cache;
    private readonly IMemoryCache _memoryCache;

    public CacheBatchOperationsTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<MemoryCacheService>>();
        var cachingOptions = Microsoft.Extensions.Options.Options.Create(new CachingOptions());
        _cache = new MemoryCacheService(_memoryCache, logger, cachingOptions);
    }

    [Fact]
    public async Task GetManyAsync_WhenKeysExist_ShouldReturnAllValues()
    {
        // Arrange
        await _cache.SetAsync("key1", "value1");
        await _cache.SetAsync("key2", "value2");
        await _cache.SetAsync("key3", "value3");

        // Act
        var result = await _cache.GetManyAsync<string>(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.Equal("value3", result["key3"]);
    }

    [Fact]
    public async Task GetManyAsync_WhenSomeKeysNotExist_ShouldReturnOnlyExistingKeys()
    {
        // Arrange
        await _cache.SetAsync("key1", "value1");
        await _cache.SetAsync("key2", "value2");

        // Act
        var result = await _cache.GetManyAsync<string>(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("value2", result["key2"]);
        Assert.False(result.ContainsKey("key3"));
    }

    [Fact]
    public async Task GetManyAsync_WhenNoKeysExist_ShouldReturnEmptyDictionary()
    {
        // Act
        var result = await _cache.GetManyAsync<string>(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetManyAsync_WhenKeysIsEmpty_ShouldReturnEmptyDictionary()
    {
        // Act
        var result = await _cache.GetManyAsync<string>(Array.Empty<string>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task SetManyAsync_ShouldSetAllValues()
    {
        // Arrange
        var items = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" },
            { "key3", "value3" }
        };

        // Act
        await _cache.SetManyAsync(items);

        // Assert
        Assert.Equal("value1", await _cache.GetAsync<string>("key1"));
        Assert.Equal("value2", await _cache.GetAsync<string>("key2"));
        Assert.Equal("value3", await _cache.GetAsync<string>("key3"));
    }

    [Fact]
    public async Task SetManyAsync_WithExpiration_ShouldSetExpiration()
    {
        // Arrange
        var items = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };

        // Act
        await _cache.SetManyAsync(items, TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal("value1", await _cache.GetAsync<string>("key1"));
        Assert.Equal("value2", await _cache.GetAsync<string>("key2"));

        // Wait for expiration
        await Task.Delay(1100);

        // Assert
        Assert.Null(await _cache.GetAsync<string>("key1"));
        Assert.Null(await _cache.GetAsync<string>("key2"));
    }

    [Fact]
    public async Task SetManyAsync_WhenItemsIsEmpty_ShouldNotThrow()
    {
        // Arrange
        var items = new Dictionary<string, string>();

        // Act & Assert
        await _cache.SetManyAsync(items);
    }

    [Fact]
    public async Task RemoveManyAsync_ShouldRemoveAllKeys()
    {
        // Arrange
        await _cache.SetAsync("key1", "value1");
        await _cache.SetAsync("key2", "value2");
        await _cache.SetAsync("key3", "value3");

        // Act
        await _cache.RemoveManyAsync(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Null(await _cache.GetAsync<string>("key1"));
        Assert.Null(await _cache.GetAsync<string>("key2"));
        Assert.Null(await _cache.GetAsync<string>("key3"));
    }

    [Fact]
    public async Task RemoveManyAsync_WhenSomeKeysNotExist_ShouldRemoveOnlyExistingKeys()
    {
        // Arrange
        await _cache.SetAsync("key1", "value1");
        await _cache.SetAsync("key2", "value2");

        // Act
        await _cache.RemoveManyAsync(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Null(await _cache.GetAsync<string>("key1"));
        Assert.Null(await _cache.GetAsync<string>("key2"));
    }

    [Fact]
    public async Task RemoveManyAsync_WhenKeysIsEmpty_ShouldNotThrow()
    {
        // Act & Assert
        await _cache.RemoveManyAsync(Array.Empty<string>());
    }

    [Fact]
    public async Task BatchOperations_ShouldWorkTogether()
    {
        // Arrange
        var items = new Dictionary<string, int>
        {
            { "key1", 1 },
            { "key2", 2 },
            { "key3", 3 }
        };

        // Act - Set many
        await _cache.SetManyAsync(items);

        // Assert - Get many
        var retrieved = await _cache.GetManyAsync<int>(new[] { "key1", "key2", "key3" });
        Assert.Equal(3, retrieved.Count);
        Assert.Equal(1, retrieved["key1"]);
        Assert.Equal(2, retrieved["key2"]);
        Assert.Equal(3, retrieved["key3"]);

        // Act - Remove many
        await _cache.RemoveManyAsync(new[] { "key1", "key2" });

        // Assert
        var afterRemove = await _cache.GetManyAsync<int>(new[] { "key1", "key2", "key3" });
        Assert.Single(afterRemove);
        Assert.Equal(3, afterRemove["key3"]);
    }
}