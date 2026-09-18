using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Authorization.Events.Handlers;
using Tnzi.Identity.Events;

namespace Tnzi.Authorization.Tests.Handlers;

/// <summary>
/// 角色变更后的权限缓存失效处理器：失效失败必须冒泡给总线（错误隔离 + 重试 + DLQ），
/// 否则被撤销的权限会静默保留到缓存 TTL。这半段处理器本来就不吞异常；此前吞掉它的是
/// Redis 实现的 <c>RemoveAsync</c>，本用例与 Redis 侧的 fail-closed 用例合起来才构成整条链路的证据。
/// </summary>
public class UserRolesChangedEventHandlerTests
{
    private static UserRolesChangedEvent RoleRevoked(Guid userId) => new()
    {
        UserId = userId,
        ChangeType = UserRolesChangeType.Removed,
        RemovedRoleIds = [Guid.NewGuid()]
    };

    [Fact]
    public async Task HandleAsync_PropagatesInvalidationFailure()
    {
        var cache = new Mock<ICache>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis timeout"));
        var handler = new UserRolesChangedEventHandler(
            NullLogger<UserRolesChangedEventHandler>.Instance, new FunctionAuthCache(cache.Object));

        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(RoleRevoked(Guid.NewGuid())));
    }

    [Fact]
    public async Task HandleAsync_InvalidatesTheUsersPermissionEntry()
    {
        var userId = Guid.NewGuid();
        var cache = new Mock<ICache>();
        var handler = new UserRolesChangedEventHandler(
            NullLogger<UserRolesChangedEventHandler>.Instance, new FunctionAuthCache(cache.Object));

        await handler.HandleAsync(RoleRevoked(userId));

        cache.Verify(c => c.RemoveAsync(CacheKeys.Authorization.UserFunctions(userId), It.IsAny<CancellationToken>()), Times.Once);
    }
}
