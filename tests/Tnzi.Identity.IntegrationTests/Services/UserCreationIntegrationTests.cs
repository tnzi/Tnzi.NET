using Microsoft.AspNetCore.Identity;
using Tnzi.Identity.Events;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 管理端建号（<c>POST admin/users</c>）与邀请建号共用的那条路：表单上收的每一栏都要落库，注册事件只在账号可用时发。
/// </summary>
public class UserCreationIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly UserService _users;

    public UserCreationIntegrationTests()
    {
        var details = new UserDetailService(CreateRepository<UserDetail>(), UserManager, ServiceProvider, new UserTenantScopeProvider(CreateRepository<User>()));
        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            userDetailService: details);
    }

    private async Task<UserDetail?> StoredDetailAsync(Guid userId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.UserDetails.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == userId);
    }

    /// <summary>
    /// ★★ 建号表单上的昵称此前被安静丢掉：<c>CreateUserDto.Nickname</c> 映射到 <see cref="User"/>，而昵称住在资料表，
    /// 建号这条路从不写资料。接口答 200、返回的昵称是 null，详情页上也没有。
    /// </summary>
    [Fact]
    public async Task The_nickname_given_at_creation_is_stored_and_returned()
    {
        var created = await _users.CreateAsync(new CreateUserDto { Email = "bob@corp.example", Password = "Password123!", Nickname = "Bob" });

        Assert.True(created.Succeeded, created.Message);
        Assert.Equal("Bob", created.Data!.Nickname);
        Assert.Equal("Bob", (await StoredDetailAsync(created.Data.Id))?.Nickname);
    }

    /// <summary>对照组：没给昵称就不凭空建一行空资料。</summary>
    [Fact]
    public async Task Without_a_nickname_no_profile_row_is_created()
    {
        var created = await _users.CreateAsync(new CreateUserDto { Email = "amy@corp.example", Password = "Password123!" });

        Assert.True(created.Succeeded, created.Message);
        Assert.Null(await StoredDetailAsync(created.Data!.Id));
    }

    /// <summary>对照组：管理员直接建出的账号照旧发布注册事件。</summary>
    [Fact]
    public async Task An_admin_created_account_announces_its_registration()
    {
        var created = await _users.CreateAsync(new CreateUserDto { Email = "cat@corp.example", Password = "Password123!" });

        Assert.True(created.Succeeded, created.Message);
        EventBusMock.Verify(
            x => x.PublishAsync(It.Is<UserRegisteredEvent>(e => e.UserId == created.Data!.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// ★★★ 为邀请建出的账号此刻没有密码、登录不进去，不能宣布「已注册」：框架自带的订阅者据此发一封
    /// 「账号已可用」的欢迎邮件（开着邮箱确认时还附一条只翻转确认位的链接），被邀请人先收到它、再收到真正的邀请。
    /// 注册事件改在接受邀请、账号可用的那一刻发布。
    /// </summary>
    [Fact]
    public async Task An_account_created_for_an_invitation_does_not_announce_a_registration()
    {
        var created = await _users.CreateForInvitationAsync(new CreateUserDto { Email = "dan@corp.example", Nickname = "Dan" });

        Assert.True(created.Succeeded, created.Message);
        Assert.Equal("Dan", (await StoredDetailAsync(created.Data!.Id))?.Nickname);
        EventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<UserRegisteredEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
