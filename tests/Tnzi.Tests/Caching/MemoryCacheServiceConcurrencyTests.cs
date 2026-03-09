
namespace Tnzi.Tests.Caching;

/// <summary>
/// 线程安全缓存服务并发测试
/// </summary>
public class MemoryCacheServiceConcurrencyTests
{
    private readonly MemoryCacheService _cache;

    public MemoryCacheServiceConcurrencyTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<MemoryCacheService>>();
        var cachingOptions = Microsoft.Extensions.Options.Options.Create(new CachingOptions());
        _cache = new MemoryCacheService(memoryCache, logger, cachingOptions);
    }

    [Fact]
    public async Task IncrementAsync_SequentialCalls_ReturnsCorrectValues()
    {
        // Arrange
        const string key = "seq-counter";

        // Act: 顺序调用 10 次
        var results = new long[10];
        for (int i = 0; i < 10; i++)
        {
            results[i] = await _cache.IncrementAsync(key);
        }

        // Assert: 返回值应该是 1, 2, 3, ..., 10
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, results);
    }

    [Fact]
    public async Task IncrementAsync_WithConcurrentAccess_ReturnsCorrectTotal()
    {
        // Arrange: 创建独立的缓存实例，避免与其他测试的状态污染
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<MemoryCacheService>>();
        var cachingOptions = Microsoft.Extensions.Options.Options.Create(new CachingOptions());
        var cache = new MemoryCacheService(memoryCache, logger, cachingOptions);

        const string key = "concurrent-counter";
        const int totalIncrements = 100;

        // Act: 使用独立任务并发执行，避免 Parallel.ForEachAsync 在测试环境下的调度差异
        var tasks = Enumerable.Range(0, totalIncrements)
            .Select(_ => Task.Run(() => cache.IncrementAsync(key)));
        await Task.WhenAll(tasks);

        // Assert: 验证最终值等于总增量（使用 IncrementAsync(0) 读取实际计数器值）
        var finalValue = await cache.IncrementAsync(key, 0);
        Assert.Equal(totalIncrements, finalValue);

        cache.Dispose();
    }

    [Fact]
    public async Task TrySetAsync_WithConcurrentAccess_OnlyOneSucceeds()
    {
        // Arrange
        const string key = "race-condition-key";
        const int taskCount = 100;
        var successCount = 0;

        // Act: 100 个并发任务尝试设置同一个键
        var tasks = Enumerable.Range(0, taskCount)
            .Select(i => Task.Run(async () =>
            {
                var success = await _cache.TrySetAsync(key, i);
                if (success)
                {
                    Interlocked.Increment(ref successCount);
                }
            }));
        await Task.WhenAll(tasks);

        // Assert: 只有一个任务成功
        Assert.Equal(1, successCount);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_RemovesMatchingKeys()
    {
        // Arrange
        await _cache.SetAsync("user:1", "Alice");
        await _cache.SetAsync("user:2", "Bob");
        await _cache.SetAsync("user:3", "Charlie");
        await _cache.SetAsync("product:1", "Widget");

        // Act
        await _cache.RemoveByPrefixAsync("user:");

        // Assert
        Assert.False(await _cache.ExistsAsync("user:1"));
        Assert.False(await _cache.ExistsAsync("user:2"));
        Assert.False(await _cache.ExistsAsync("user:3"));
        Assert.True(await _cache.ExistsAsync("product:1"));
    }

    [Fact]
    public async Task SetWithTagsAsync_AndRemoveByTag_RemovesTaggedKeys()
    {
        // Arrange
        await _cache.SetWithTagsAsync("item:1", "Value1", new[] { "category:electronics" });
        await _cache.SetWithTagsAsync("item:2", "Value2", new[] { "category:electronics" });
        await _cache.SetWithTagsAsync("item:3", "Value3", new[] { "category:books" });

        // Act
        await _cache.RemoveByTagAsync("category:electronics");

        // Assert
        Assert.False(await _cache.ExistsAsync("item:1"));
        Assert.False(await _cache.ExistsAsync("item:2"));
        Assert.True(await _cache.ExistsAsync("item:3"));
    }
}
