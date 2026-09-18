using Tnzi.Storage;

namespace Tnzi.Identity.Tests.Integration;

/// <summary>
/// 把一个文件 id 写进 <c>UserDetail.AvatarId</c> 之前必须问一句 <see cref="IFileReadAccessProbe"/>：
/// 「这个人本来就读得到这份文件吗」。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 这是全仓最强的一处 <c>[FileField]</c> 写入：<c>AvatarId</c> 标着 <c>[FileField(Public = true)]</c>，
/// 引用登记的同一个事务里 <c>FileReferenceProcessor</c> 会把那份文件翻成 <c>IsPublic = true</c>，
/// 之后**匿名** <c>GET files/{id}/download</c> 直接放行 —— 不像 Chat / Finance / Signing 那样还要过一层
/// 按记录的可见性判据。而写入口是自助的 <c>PUT users/profile</c>，不要任何权限码；实体 ID 是顺序 GUID。
/// 不问一句就写，任何登录用户猜一个 id 填进头像，那份 HR 档案 / 合同 / 支票就对全世界公开了。
/// </para>
/// <para>
/// 探针的实现随 <c>Tnzi.Storage</c> 注册，Identity 不引用它（契约在核心 <c>Tnzi</c> 程序集）：
/// 这里用替身回答「能 / 不能」，被测的是服务自己那道门。仓储是真的 SQLite，断言的是落库状态。
/// </para>
/// </remarks>
public class UserDetailAvatarGuardTests : IntegrationTestBase
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<UserManager<User>> _userManager;

    public UserDetailAvatarGuardTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _userManager.Setup(m => m.FindByIdAsync(_userId.ToString()))
            .ReturnsAsync(new User { Id = _userId, UserName = "victim-picker" });
    }

    /// <summary>UserDetail 对 User 有外键，行得先在；TnziDbContext 只认异步保存，故不能放进构造函数。</summary>
    private async Task<UserDetailService> BuildAsync(IFileReadAccessProbe? probe)
    {
        if (!await DbContext.Users.AnyAsync(u => u.Id == _userId))
        {
            DbContext.Users.Add(new User { Id = _userId, UserName = "victim-picker", NormalizedUserName = "VICTIM-PICKER" });
            await DbContext.SaveChangesAsync();
            DbContext.ChangeTracker.Clear();
        }

        return new UserDetailService(
            new EFCoreRepository<IdentityTestDbContext, UserDetail, Guid>(DbContext, serviceProvider: ServiceProvider),
            _userManager.Object,
            ServiceProvider,
            probe);
    }

    private static IFileReadAccessProbe Answering(bool canRead)
    {
        var probe = new Mock<IFileReadAccessProbe>();
        probe.Setup(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(canRead);
        return probe.Object;
    }

    private async Task<UserDetail?> StoredDetailAsync()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<UserDetail>().AsNoTracking().SingleOrDefaultAsync(d => d.UserId == _userId);
    }

    [Fact]
    public async Task Create_WhenCallerCannotReadTheFile_Rejected403AndWritesNothing()
    {
        var service = await BuildAsync(Answering(canRead: false));

        var result = await service.CreateOrUpdateAsync(_userId, new CreateUserDetailDto { Nickname = "x", AvatarId = Guid.NewGuid() });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await StoredDetailAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Update_WhenCallerCannotReadTheFile_Rejected403AndKeepsTheOldAvatar()
    {
        var mine = Guid.NewGuid();
        await (await BuildAsync(Answering(canRead: true))).CreateOrUpdateAsync(_userId, new CreateUserDetailDto { AvatarId = mine });

        var result = await (await BuildAsync(Answering(canRead: false)))
            .CreateOrUpdateAsync(_userId, new CreateUserDetailDto { Nickname = "renamed", AvatarId = Guid.NewGuid() });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        var stored = await StoredDetailAsync();
        stored.ShouldNotBeNull();
        stored.AvatarId.ShouldBe(mine);
        stored.Nickname.ShouldBeNull();
    }

    [Fact]
    public async Task Write_WhenCallerCanReadTheFile_Persists()
    {
        var mine = Guid.NewGuid();

        var result = await (await BuildAsync(Answering(canRead: true))).CreateOrUpdateAsync(_userId, new CreateUserDetailDto { AvatarId = mine });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await StoredDetailAsync())!.AvatarId.ShouldBe(mine);
    }

    /// <summary>
    /// 前端按 REPLACE 语义每次保存都把现有的 avatarId 原样带回来。文件被发布是写入那一刻的事，
    /// 再存一次同一个 id 没有发布任何新东西；此时再问探针，头像文件一旦被删，用户从此改不了昵称。
    /// </summary>
    [Fact]
    public async Task Update_WithTheSameAvatar_DoesNotAskTheProbeAgain()
    {
        var mine = Guid.NewGuid();
        await (await BuildAsync(Answering(canRead: true))).CreateOrUpdateAsync(_userId, new CreateUserDetailDto { AvatarId = mine });
        var probe = new Mock<IFileReadAccessProbe>(MockBehavior.Strict);

        var result = await (await BuildAsync(probe.Object)).CreateOrUpdateAsync(_userId, new CreateUserDetailDto { Nickname = "renamed", AvatarId = mine });

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = await StoredDetailAsync();
        stored!.Nickname.ShouldBe("renamed");
        stored.AvatarId.ShouldBe(mine);
        probe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Write_WithoutAnAvatar_DoesNotNeedTheProbe()
    {
        var result = await (await BuildAsync(probe: null)).CreateOrUpdateAsync(_userId, new CreateUserDetailDto { Nickname = "no-avatar", AvatarUrl = "https://cdn.example/a.png" });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await StoredDetailAsync())!.Nickname.ShouldBe("no-avatar");
    }

    /// <summary>
    /// 存储模块缺席时拒绝，不是跳过：「跳过校验」与「校验通过」在接口上完全一致。
    /// 501 而不是 503：这不是暂时性故障，重试永远不会好。
    /// </summary>
    [Fact]
    public async Task Write_WithAvatarButWithoutStorageModule_Rejected501()
    {
        var result = await (await BuildAsync(probe: null)).CreateOrUpdateAsync(_userId, new CreateUserDetailDto { AvatarId = Guid.NewGuid() });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message!.ShouldContain("Tnzi.Storage");
        (await StoredDetailAsync()).ShouldBeNull();
    }

    /// <summary>Guid.Empty 在 [FileField] 那一侧会被静默忽略（不产生引用行）：与其留一个安静的空操作，不如当场说不。</summary>
    [Fact]
    public async Task Write_WithAnEmptyGuid_Rejected400()
    {
        var result = await (await BuildAsync(Answering(canRead: true))).CreateOrUpdateAsync(_userId, new CreateUserDetailDto { AvatarId = Guid.Empty });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await StoredDetailAsync()).ShouldBeNull();
    }
}
