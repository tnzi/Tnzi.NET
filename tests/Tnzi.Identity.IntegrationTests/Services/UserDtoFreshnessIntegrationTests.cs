using Microsoft.AspNetCore.Identity;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// <c>GET admin/users/{id}</c> 与 <c>GET users/profile</c> 读到的是账号此刻的样子，不是半小时前的。
/// </summary>
/// <remarks>
/// ★★★ 此前 <c>GetByIdAsync</c> 把整个 <see cref="UserDto"/>（角色、待办义务、锁定、两步验证、失败次数、资料）
/// 缓存 30 分钟，而改这些字段的写入方散在十几处：分配 / 移除角色、管理员重置密码、两步验证开关、接受邀请、
/// 登录失败锁定、资料服务……只有少数几处记得清缓存。管理端给人勾上一个角色、保存成功，刷新回来的还是旧角色，
/// 勾选框弹回去；再点保存就撞「已在该角色中」。<c>IsLockedOut</c> 还是随时间变化的，缓存本身就不可能对。
/// 这里覆盖四类写入方（用户服务里的角色分配与移除、直接经 UserManager 的锁定、资料服务），
/// 每一次写入之后的下一次读都必须是新值。
/// </remarks>
public class UserDtoFreshnessIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly UserService _users;
    private readonly UserDetailService _details;

    public UserDtoFreshnessIntegrationTests()
    {
        _details = new UserDetailService(CreateRepository<UserDetail>(), UserManager, ServiceProvider, new UserTenantScopeProvider(CreateRepository<User>()));
        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            userDetailService: _details);
    }

    private async Task<Role> CreateRoleAsync(string name)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant() };
        DbContext.Roles.Add(role);
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return role;
    }

    private async Task<UserDto> ReadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        var read = await _users.GetByIdAsync(id);
        Assert.True(read.Succeeded, read.Message);
        return read.Data!;
    }

    [Fact]
    public async Task An_assigned_role_shows_on_the_next_read()
    {
        var user = await CreateUserAsync(email: "roles@example.com");
        var editor = await CreateRoleAsync("Editor");
        Assert.Empty((await ReadAsync(user.Id)).Roles);

        Assert.True((await _users.AssignRolesAsync(user.Id, [editor.Id])).Succeeded);

        Assert.Equal(["Editor"], (await ReadAsync(user.Id)).Roles);
    }

    [Fact]
    public async Task A_removed_role_disappears_on_the_next_read()
    {
        var user = await CreateUserAsync(email: "unroles@example.com");
        var editor = await CreateRoleAsync("Editor");
        Assert.True((await _users.AssignRolesAsync(user.Id, [editor.Id])).Succeeded);
        Assert.Equal(["Editor"], (await ReadAsync(user.Id)).Roles);

        Assert.True((await _users.RemoveRolesAsync(user.Id, [editor.Id])).Succeeded);

        Assert.Empty((await ReadAsync(user.Id)).Roles);
    }

    /// <summary>写入方不在用户服务里（这里是登录失败锁定那一类直接经 UserManager 的写入）：下一次读也要看得见。</summary>
    [Fact]
    public async Task A_lockout_written_elsewhere_shows_on_the_next_read()
    {
        var user = await CreateUserAsync(email: "locked@example.com");
        Assert.Null((await ReadAsync(user.Id)).LockoutEnd);

        DbContext.ChangeTracker.Clear();
        var tracked = await UserManager.FindByIdAsync(user.Id.ToString());
        Assert.True((await UserManager.SetLockoutEndDateAsync(tracked!, DateTimeOffset.UtcNow.AddMinutes(15))).Succeeded);

        Assert.NotNull((await ReadAsync(user.Id)).LockoutEnd);
    }

    /// <summary>资料经资料服务直接改（管理端 user-details 与自助资料都走它）：下一次读也要看得见。</summary>
    [Fact]
    public async Task A_profile_change_through_the_detail_service_shows_on_the_next_read()
    {
        var user = await CreateUserAsync(email: "profile@example.com");
        Assert.Null((await ReadAsync(user.Id)).Nickname);

        Assert.True((await _details.CreateOrUpdateAsync(user.Id, new CreateUserDetailDto { Nickname = "Neo" })).Succeeded);

        Assert.Equal("Neo", (await ReadAsync(user.Id)).Nickname);
    }
}
