
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

    [Fact]
    public async Task CheckPermissionAsync_WithCachedPermissions_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissionName = "read";
        var permissions = new List<string> { "read", "write" };

        _cacheMock.Setup(x => x.GetAsync<IEnumerable<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var result = await _cache.CheckPermissionAsync(userId, permissionName);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckPermissionAsync_WithNoCachedPermissions_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissionName = "read";

        _cacheMock.Setup(x => x.GetAsync<IEnumerable<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string>?)null);

        // Act
        var result = await _cache.CheckPermissionAsync(userId, permissionName);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckPermissionAsync_WithPermissionNotInCache_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissionName = "delete";
        var permissions = new List<string> { "read", "write" };

        _cacheMock.Setup(x => x.GetAsync<IEnumerable<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var result = await _cache.CheckPermissionAsync(userId, permissionName);

        // Assert
        result.ShouldBeFalse();
    }

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
    
    [Fact]
    public async Task CheckPermissionsAsync_WithCachedPermissions_ReturnsCorrectResults()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissionNames = new List<string> { "read", "write", "delete" };
        var cachedPermissions = new List<string> { "read", "write" };

        _cacheMock.Setup(x => x.GetAsync<IEnumerable<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedPermissions);

        // Act
        var result = await _cache.CheckPermissionsAsync(userId, permissionNames);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(3);
        result["read"].ShouldBeTrue();
        result["write"].ShouldBeTrue();
        result["delete"].ShouldBeFalse();
    }
    
    [Fact]
    public async Task CheckPermissionsAsync_WithNoCachedPermissions_ReturnsAllFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissionNames = new List<string> { "read", "write" };

        _cacheMock.Setup(x => x.GetAsync<IEnumerable<string>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string>?)null);

        // Act
        var result = await _cache.CheckPermissionsAsync(userId, permissionNames);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);
        result["read"].ShouldBeFalse();
        result["write"].ShouldBeFalse();
    }
    
    [Fact]
    public async Task CheckPermissionsAsync_WithEmptyList_ReturnsEmptyDictionary()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var permissionNames = new List<string>();

        // Act
        var result = await _cache.CheckPermissionsAsync(userId, permissionNames);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(0);
    }
}