using Tnzi.Security.Authorization;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// <see cref="ChatAccessService.FilterDisabledAsync"/> 必须走批量判定。
///
/// ★ 此前它是逐个 <c>await IsGrantedAsync(id, "chat.use")</c> 的循环，而每次判定至少一次
/// 无缓存的角色查询：通讯录搜索、群发、会话列表都经这里，N 个用户 = N 次以上 DB 往返。
/// 现在交给 <see cref="IFunctionAuthorizationService.FilterGrantedAsync"/> 一次算完。
/// </summary>
public class ChatAccessServiceBatchTests
{
    private static ChatAccessService Build(Mock<IPermissionChecker> checker, Mock<IFunctionAuthorizationService> authorization)
    {
        var services = new ServiceCollection();
        return new ChatAccessService(services.BuildServiceProvider(), checker.Object, authorization.Object);
    }

    [Fact]
    public async Task FilterDisabled_UsesOneBatchCall_NotAPerUserLoop()
    {
        var granted = Guid.NewGuid();
        var denied = Guid.NewGuid();
        var checker = new Mock<IPermissionChecker>(MockBehavior.Strict);
        var authorization = new Mock<IFunctionAuthorizationService>();
        authorization
            .Setup(a => a.FilterGrantedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), ChatAccessService.UsePermission))
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid> { granted });
        var service = Build(checker, authorization);

        var disabled = await service.FilterDisabledAsync([granted, denied, denied, Guid.Empty]);

        disabled.ShouldBe(new HashSet<Guid> { denied });
        authorization.Verify(
            a => a.FilterGrantedAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), ChatAccessService.UsePermission),
            Times.Once);
        checker.Verify(c => c.IsGrantedAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task FilterDisabled_FailsOpen_WhenAuthorizationIsAbsent()
    {
        var service = new ChatAccessService(new ServiceCollection().BuildServiceProvider());

        var disabled = await service.FilterDisabledAsync([Guid.NewGuid()]);

        disabled.ShouldBeEmpty();
    }
}
