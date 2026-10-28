using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Tnzi.Exceptions;
using Tnzi.Identity.Services;
using Tnzi.Security.Authorization;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 建号 / 改号带角色时，角色被拒（不存在 404、委托护栏 403）必须在<b>写下任何东西之前</b>答出来。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <c>UserManager</c> 每一步都自己提交，框架默认也没有请求级工作单元，所以「先建号、再分配角色」
/// 在角色被拒时留下的是一个已经落库的账号：没有密码、没有角色、<c>PendingActions = None</c>。
/// 经邀请走到这里时，它不受「待接受邀请」守卫管辖，开着验证码登录的部署里收得到那个邮箱验证码的人就能登进来；
/// 改正角色重试则撞重名。改号同理：邮箱 / 手机号 / 组织已经写下、旧角色已经摘掉，才发现新角色不让加。
/// </para>
/// <para>
/// 走真实 <c>UserManager</c> + SQLite，断言用 <c>IgnoreQueryFilters</c> 重读：「先写后补偿」留下的软删行也会被看见，
/// 证明的是「根本没写」而不是「写了又删」。
/// </para>
/// </remarks>
public class UserRoleWriteOrderIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IFunctionAuthorizationService> _functionAuthorization = new();
    private readonly UserService _users;
    private readonly InvitationService _invitations;

    public UserRoleWriteOrderIntegrationTests()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.Id).Returns(Guid.NewGuid());
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(It.IsAny<Guid>())).ReturnsAsync(false);

        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: _currentUser.Object,
            functionAuthorization: _functionAuthorization.Object);

        var urlGenerator = new Mock<IInvitationUrlGenerator>();
        urlGenerator.Setup(g => g.GenerateUrl(It.IsAny<User>(), It.IsAny<string>())).Returns("https://example.test/accept");
        var options = new Mock<IOptionsMonitor<IdentityOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new IdentityOptions());
        _invitations = new InvitationService(
            ServiceProvider,
            UserManager,
            _users,
            new AuthTokenService(CreateRepository<AuthToken>(), ServiceProvider.GetRequiredService<IDataProtectionProvider>(), ServiceProvider),
            new Mock<IInvitationAcceptanceHandler>().Object,
            urlGenerator.Object,
            options.Object,
            new UserTenantScopeProvider(CreateRepository<User>()));
    }

    private async Task<Role> SeedRoleAsync(string name, bool membershipAllowed)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant() };
        DbContext.Roles.Add(role);
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        _functionAuthorization.Setup(a => a.CanManageRoleMembershipAsync(It.IsAny<Guid>(), role.Id)).ReturnsAsync(membershipAllowed);
        return role;
    }

    /// <summary>这个邮箱名下有没有任何一行账号，软删的也算。</summary>
    private async Task<bool> AnyAccountRowAsync(string email)
    {
        DbContext.ChangeTracker.Clear();
        var normalized = email.ToUpperInvariant();
        return await DbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalized);
    }

    private async Task<User> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Users.SingleAsync(u => u.Id == id);
    }

    private async Task<bool> IsInRoleAsync(Guid userId, Guid roleId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
    }

    [Fact]
    public async Task Create_with_a_role_the_caller_cannot_grant_is_forbidden_and_writes_no_account()
    {
        var strong = await SeedRoleAsync("Payroll", membershipAllowed: false);

        var result = await _users.CreateAsync(new CreateUserDto { Email = "alice@corp.example", Password = "Password123!", RoleIds = [strong.Id] });

        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.FORBIDDEN, result.ErrorCode);
        Assert.False(await AnyAccountRowAsync("alice@corp.example"));
    }

    [Fact]
    public async Task Create_with_an_unknown_role_is_404_and_writes_no_account()
    {
        var result = await _users.CreateAsync(new CreateUserDto { Email = "bob@corp.example", Password = "Password123!", RoleIds = [Guid.NewGuid()] });

        Assert.Equal(404, result.Code);
        Assert.False(await AnyAccountRowAsync("bob@corp.example"));
    }

    /// <summary>被拒之后改正角色重试必须能成：此前第二次撞的是第一次留下的那个账号（重名）。</summary>
    [Fact]
    public async Task Create_retried_with_corrected_roles_succeeds_after_a_refusal()
    {
        var strong = await SeedRoleAsync("Payroll", membershipAllowed: false);
        var plain = await SeedRoleAsync("Staff", membershipAllowed: true);
        await _users.CreateAsync(new CreateUserDto { Email = "carol@corp.example", Password = "Password123!", RoleIds = [strong.Id] });

        var retried = await _users.CreateAsync(new CreateUserDto { Email = "carol@corp.example", Password = "Password123!", RoleIds = [plain.Id] });

        Assert.True(retried.Succeeded, retried.Message);
        Assert.True(await IsInRoleAsync(retried.Data!.Id, plain.Id));
    }

    /// <summary>
    /// 邀请走的是同一个建号出口：被拒的邀请不能留下一个无密码、无 Pending 标记的账号。
    /// </summary>
    [Fact]
    public async Task Invite_with_a_role_the_caller_cannot_grant_leaves_no_account()
    {
        var strong = await SeedRoleAsync("Payroll", membershipAllowed: false);

        var result = await _invitations.InviteAsync(new CreateInvitationDto { Email = "dave@corp.example", RoleIds = [strong.Id] });

        Assert.Equal(403, result.Code);
        Assert.False(await AnyAccountRowAsync("dave@corp.example"));
    }

    /// <summary>批量建号同理，整批问完再动手：第二个人的角色被拒，第一个人也不该已经建好。</summary>
    [Fact]
    public async Task CreateMany_with_a_forbidden_role_in_a_later_item_creates_nobody()
    {
        var strong = await SeedRoleAsync("Payroll", membershipAllowed: false);
        var plain = await SeedRoleAsync("Staff", membershipAllowed: true);

        var result = await _users.CreateManyAsync(
        [
            new CreateUserDto { Email = "first@corp.example", Password = "Password123!", RoleIds = [plain.Id] },
            new CreateUserDto { Email = "second@corp.example", Password = "Password123!", RoleIds = [strong.Id] },
        ]);

        Assert.Equal(403, result.Code);
        Assert.False(await AnyAccountRowAsync("first@corp.example"));
        Assert.False(await AnyAccountRowAsync("second@corp.example"));
    }

    [Fact]
    public async Task Update_adding_a_forbidden_role_changes_neither_contact_details_nor_roles()
    {
        var strong = await SeedRoleAsync("Payroll", membershipAllowed: false);
        var target = await CreateUserAsync(email: "erin@corp.example", phoneNumber: "+15550000001");

        var result = await _users.UpdateAsync(target.Id, new UpdateUserDto
        {
            Email = "erin.new@corp.example",
            PhoneNumber = "+15550000002",
            RoleIds = [strong.Id],
        });

        Assert.Equal(403, result.Code);
        var stored = await ReloadAsync(target.Id);
        Assert.Equal("erin@corp.example", stored.Email);
        Assert.Equal("+15550000001", stored.PhoneNumber);
        Assert.True(stored.EmailConfirmed);
        Assert.False(await IsInRoleAsync(target.Id, strong.Id));
    }

    /// <summary>
    /// 摘除与授予同受护栏：要摘的角色被拒时，旧角色留着、新角色没加、邮箱也没改。
    /// 此前联系方式先落库，摘角色那一步才答 403。
    /// </summary>
    [Fact]
    public async Task Update_swapping_out_a_forbidden_role_keeps_the_old_role_and_adds_nothing()
    {
        var strong = await SeedRoleAsync("Payroll", membershipAllowed: false);
        var plain = await SeedRoleAsync("Staff", membershipAllowed: true);
        var target = await CreateUserAsync(email: "frank@corp.example");
        DbContext.UserRoles.Add(new UserRole { UserId = target.Id, RoleId = strong.Id });
        await SaveChangesAsync();

        var result = await _users.UpdateAsync(target.Id, new UpdateUserDto { Email = "frank.new@corp.example", RoleIds = [plain.Id] });

        Assert.Equal(403, result.Code);
        Assert.True(await IsInRoleAsync(target.Id, strong.Id));
        Assert.False(await IsInRoleAsync(target.Id, plain.Id));
        Assert.Equal("frank@corp.example", (await ReloadAsync(target.Id)).Email);
    }

    [Fact]
    public async Task Update_adding_an_unknown_role_is_404_and_changes_nothing()
    {
        var target = await CreateUserAsync(email: "gina@corp.example");

        var result = await _users.UpdateAsync(target.Id, new UpdateUserDto { Email = "gina.new@corp.example", RoleIds = [Guid.NewGuid()] });

        Assert.Equal(404, result.Code);
        Assert.Equal("gina@corp.example", (await ReloadAsync(target.Id)).Email);
    }

    /// <summary>对照：角色全部放行时，建号与改号照常把角色挂上、把旧的摘掉。</summary>
    [Fact]
    public async Task Allowed_role_changes_go_through_on_create_and_update()
    {
        var plain = await SeedRoleAsync("Staff", membershipAllowed: true);
        var other = await SeedRoleAsync("Support", membershipAllowed: true);

        var created = await _users.CreateAsync(new CreateUserDto { Email = "hank@corp.example", Password = "Password123!", RoleIds = [plain.Id] });
        Assert.True(created.Succeeded, created.Message);
        Assert.True(await IsInRoleAsync(created.Data!.Id, plain.Id));

        var updated = await _users.UpdateAsync(created.Data.Id, new UpdateUserDto { RoleIds = [other.Id] });
        Assert.True(updated.Succeeded, updated.Message);
        Assert.False(await IsInRoleAsync(created.Data.Id, plain.Id));
        Assert.True(await IsInRoleAsync(created.Data.Id, other.Id));
    }
}
