namespace Tnzi.Tests.Caching;

/// <summary>
/// <see cref="CacheInvalidationService"/> 的按模式失效必须对任意 <see cref="ICache"/> 生效：
/// <c>RemoveByPatternAsync</c> 是 <see cref="ICache"/> 的必选成员，默认的 <see cref="MemoryCacheService"/>
/// 也有完整实现。此前服务只在 <c>_cache is IPatternCache</c> 时才委托，而内存实现从未标记那个接口，
/// 于是默认配置下调用只记一条 Warning、一条缓存都不删，与 Redis 部署的行为不一致且零异常。
/// </summary>
public class CacheInvalidationServiceTests
{
    private static MemoryCacheService CreateMemoryCache()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<MemoryCacheService>>();
        var cachingOptions = Microsoft.Extensions.Options.Options.Create(new CachingOptions());
        return new MemoryCacheService(memoryCache, logger, cachingOptions);
    }

    [Fact]
    public async Task InvalidateByPatternAsync_WithMemoryCache_RemovesMatchingKeys()
    {
        var cache = CreateMemoryCache();
        await cache.SetAsync("user:1:name", "Alice");
        await cache.SetAsync("user:2:name", "Bob");
        await cache.SetAsync("product:1:name", "Widget");
        var service = new CacheInvalidationService(cache);

        await service.InvalidateByPatternAsync("user:*");

        Assert.False(await cache.ExistsAsync("user:1:name"));
        Assert.False(await cache.ExistsAsync("user:2:name"));
        Assert.True(await cache.ExistsAsync("product:1:name"));
    }

    [Fact]
    public async Task InvalidateByPatternAsync_DelegatesToICacheRemoveByPattern()
    {
        var cache = new Mock<ICache>(MockBehavior.Strict);
        cache.Setup(c => c.RemoveByPatternAsync("order:*", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        var service = new CacheInvalidationService(cache.Object);

        await service.InvalidateByPatternAsync("order:*");

        cache.Verify(c => c.RemoveByPatternAsync("order:*", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateByPatternAsync_BlankPattern_DoesNothing()
    {
        var cache = new Mock<ICache>(MockBehavior.Strict);
        var service = new CacheInvalidationService(cache.Object);

        await service.InvalidateByPatternAsync("   ");

        cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidateByPatternAsync_CacheThrows_IsSwallowedAndLogged()
    {
        var cache = new Mock<ICache>();
        cache.Setup(c => c.RemoveByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var logger = new Mock<ILogger<CacheInvalidationService>>();
        var service = new CacheInvalidationService(cache.Object, logger.Object);

        await service.InvalidateByPatternAsync("user:*");

        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
