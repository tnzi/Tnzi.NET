
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

    [Fact]
    public async Task GetUserPermissionNamesAsync_WithCachedValue_ReturnsFromCache()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissions = new List<string> { "read", "write" };

        _cacheMock.Setup(x => x.GetAsync<IEnumerable<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var result = await _cache.GetUserPermissionNamesAsync(userId);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(2);
    }

    [Fact]
    public async Task GetUserPermissionNamesAsync_WithNoCachedValue_ReturnsNull()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _cacheMock.Setup(x => x.GetAsync<IEnumerable<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string>?)null);

        // Act
        var result = await _cache.GetUserPermissionNamesAsync(userId);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task SetUserPermissionNamesAsync_CallsCache()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissions = new List<string> { "read", "write" };

        // Act
        await _cache.SetUserPermissionNamesAsync(userId, permissions);

        // Assert - 验证 SetAsync 被调用
        _cacheMock.Verify(x => x.SetAsync(
            It.IsAny<string>(), 
            It.IsAny<object>(), 
            It.IsAny<TimeSpan>(), 
            It.IsAny<CancellationToken>()), 
            Times.Once);
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
    // removed — the per-user cache key holds EXPLICIT grants only, so any
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