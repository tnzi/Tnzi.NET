using Tnzi.Security.Authorization;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 超管目标护栏的判据本身（单个 + 批量），不含任何服务接线。
/// </summary>
public class SuperAdminTargetGuardTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Plain = Guid.NewGuid();

    private static Mock<IFunctionAuthorizationService> Authorization(params Guid[] superAdmins)
    {
        var mock = new Mock<IFunctionAuthorizationService>();
        mock.Setup(a => a.IsSuperAdminAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => superAdmins.Contains(id));
        return mock;
    }

    [Fact]
    public async Task IsForbidden_NonSuperAdminOnASuperAdmin_IsTrue()
    {
        Assert.True(await SuperAdminTargetGuard.IsForbiddenAsync(Authorization(Root).Object, Actor, Root));
    }

    [Fact]
    public async Task IsForbidden_NoAuthorizationModule_NoActor_OrSelf_IsFalse()
    {
        Assert.False(await SuperAdminTargetGuard.IsForbiddenAsync(null, Actor, Root));
        Assert.False(await SuperAdminTargetGuard.IsForbiddenAsync(Authorization(Root).Object, null, Root));
        Assert.False(await SuperAdminTargetGuard.IsForbiddenAsync(Authorization(Root).Object, Guid.Empty, Root));
        Assert.False(await SuperAdminTargetGuard.IsForbiddenAsync(Authorization(Root).Object, Root, Root));
    }

    [Fact]
    public async Task IsAnyForbidden_OneSuperAdminInTheBatch_ByANonSuperAdmin_IsTrue()
    {
        Assert.True(await SuperAdminTargetGuard.IsAnyForbiddenAsync(Authorization(Root).Object, Actor, [Plain, Root]));
    }

    [Fact]
    public async Task IsAnyForbidden_NoSuperAdminInTheBatch_IsFalse()
    {
        Assert.False(await SuperAdminTargetGuard.IsAnyForbiddenAsync(Authorization(Root).Object, Actor, [Plain]));
    }

    [Fact]
    public async Task IsAnyForbidden_SuperAdminActor_IsFalse_AndOnlyAsksAboutItselfOnce()
    {
        var authorization = Authorization(Root, Actor);
        var other = Guid.NewGuid();
        authorization.Setup(a => a.IsSuperAdminAsync(other)).ReturnsAsync(true);

        Assert.False(await SuperAdminTargetGuard.IsAnyForbiddenAsync(authorization.Object, Actor, [Root, other]));
        authorization.Verify(a => a.IsSuperAdminAsync(Actor), Times.Once);
    }

    [Fact]
    public async Task IsAnyForbidden_ActorInTheBatchIsSkipped()
    {
        // 调用者删自己那一行不归这道护栏管（自助路径另有守卫）；批里只有他自己时不拦。
        Assert.False(await SuperAdminTargetGuard.IsAnyForbiddenAsync(Authorization(Actor).Object, Actor, [Actor, Plain]));
    }
}
