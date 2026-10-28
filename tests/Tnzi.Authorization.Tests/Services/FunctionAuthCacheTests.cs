
namespace Tnzi.Authorization.Tests.Services;

/// <summary>
/// FunctionAuthCache 单元测试
/// </summary>
public class FunctionAuthCacheTests
{
    private readonly Mock<ICache> _cacheMock;
    private readonly FunctionAuthCache _cache;

    public FunctionAuthCacheTests()
    {
        _cacheMock = new Mock<ICache>();
        _cache = new FunctionAuthCache(_cacheMock.Object);
    }

    [Fact]
    public void Constructor_WithValidCache_CreatesInstance()
    {
        // Assert
        _cache.ShouldNotBeNull();
    }

    /// <summary>经 JSON 往返的真实缓存（模拟分布式缓存）：值按分区存取。</summary>
    private static FunctionAuthCache RoundTripCache()
        => new(new Integration.JsonRoundTripCache(new MemoryCacheService(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MemoryCacheService>.Instance,
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()))));

    [Fact]
    public async Task GetUserPermissionNamesAsync_ReturnsTheSetStoredForTheSamePartition()
    {
        var cache = RoundTripCache();
        var userId = Guid.NewGuid();

        await cache.SetUserPermissionNamesAsync(userId, "t:a", new List<string> { "read", "write" });

        var result = await cache.GetUserPermissionNamesAsync(userId, "t:a");
        result.ShouldNotBeNull();
        result.ShouldBe(new[] { "read", "write" });
    }

    /// <summary>
    /// 另一个租户视角下算出的集合不能读给这个分区：按未命中处理，由调用方按当前分区重算。
    /// </summary>
    [Fact]
    public async Task GetUserPermissionNamesAsync_TreatsAnotherPartitionAsAMiss()
    {
        var cache = RoundTripCache();
        var userId = Guid.NewGuid();

        await cache.SetUserPermissionNamesAsync(userId, "t:a", new List<string> { "report.export" });

        (await cache.GetUserPermissionNamesAsync(userId, "t:b")).ShouldBeNull();
        (await cache.GetUserPermissionNamesAsync(userId, "host")).ShouldBeNull();
    }

    [Fact]
    public async Task GetUserPermissionNamesAsync_WithNoCachedValue_ReturnsNull()
    {
        (await RoundTripCache().GetUserPermissionNamesAsync(Guid.NewGuid(), "*")).ShouldBeNull();
    }

    /// <summary>
    /// 一个用户只有一个键：任何分区的写入都落在按用户删的那个键上，失效路径不必知道这个人在哪些分区里留过缓存。
    /// </summary>
    [Fact]
    public async Task RemoveUserPermissionNamesAsync_DropsWhicheverPartitionWasStored()
    {
        var cache = RoundTripCache();
        var userId = Guid.NewGuid();
        await cache.SetUserPermissionNamesAsync(userId, "t:a", new List<string> { "read" });

        await cache.RemoveUserPermissionNamesAsync(userId);

        (await cache.GetUserPermissionNamesAsync(userId, "t:a")).ShouldBeNull();
    }

    [Fact]
    public async Task RemoveUserPermissionNamesAsync_CallsCacheRemove()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _cache.RemoveUserPermissionNamesAsync(userId);

        // Assert
        _cacheMock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // NOTE: FunctionAuthCache.CheckPermissionAsync/CheckPermissionsAsync were
    // removed - the per-user cache key holds EXPLICIT grants only, so any
    // check built on it would bypass the super-admin short-circuit.
    // Permission checks live in FunctionAuthorizationService (covered by
    // SuperAdminAccessIntegrationTests).

    [Fact]
    public async Task ClearAllAsync_CallsRemoveByPrefix()
    {
        // Act
        await _cache.ClearAllAsync();

        // Assert - 验证 RemoveByPrefixAsync 被调用
        _cacheMock.Verify(x => x.RemoveByPrefixAsync(
            FunctionAuthCache.UserFunctionsCachePrefix, 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }
    
    [Fact]
    public async Task RemoveUserPermissionNamesAsync_BatchRemove_RemovesAllUsers()
    {
        // Arrange
        var userIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Act
        await _cache.RemoveUserPermissionNamesAsync(userIds);

        // Assert - 验证每个用户的缓存都被删除
        _cacheMock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), 
            Times.Exactly(userIds.Count));
    }
    
    [Fact]
    public async Task RemoveUserPermissionNamesAsync_BatchRemove_WithEmptyList_DoesNothing()
    {
        // Arrange
        var userIds = new List<Guid>();

        // Act
        await _cache.RemoveUserPermissionNamesAsync(userIds);

        // Assert - 验证没有调用 RemoveAsync
        _cacheMock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }
    
}