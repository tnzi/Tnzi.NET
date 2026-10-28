using Microsoft.AspNetCore.Identity;
using Tnzi.Identity.Events;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 删除角色等于把它从每个成员身上摘掉，必须像摘掉一样通知出去。
/// </summary>
/// <remarks>
/// ★★★ 成员关系（<c>Identity_UserRole</c>）随角色级联删除，数据库里不留任何痕迹，
/// 而授权模块的每用户权限缓存（30 分钟）只在收到 <see cref="UserRolesChangedEvent"/> 时失效。
/// 此前删除只发 <see cref="RoleDeletedEvent"/>（无人订阅），于是「删掉角色收回一批人的权限」
/// 在缓存命中的那段时间里没有生效，界面上角色已经不见了。
/// 这里跑在真实的 SQLite 上：成员必须在删除<b>之前</b>取到，删完级联已经把它们抹掉了，mock 证明不了这个顺序。
/// </remarks>
public class RoleDeletionMembershipEventsIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly RoleService _roles;

    public RoleDeletionMembershipEventsIntegrationTests()
    {
        _roles = new RoleService(
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            DbContext,
            ServiceProvider);
    }

    private async Task<Role> CreateRoleAsync(string name, params User[] members)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant() };
        DbContext.Roles.Add(role);
        foreach (var member in members)
        {
            DbContext.UserRoles.Add(new UserRole { UserId = member.Id, RoleId = role.Id });
        }

        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return role;
    }

    private void VerifyRemovalPublished(User member, Guid roleId, Times times)
        => EventBusMock.Verify(
            x => x.PublishAsync(
                It.Is<UserRolesChangedEvent>(e =>
                    e.UserId == member.Id
                    && e.UserName == member.UserName
                    && e.RemovedRoleIds.Contains(roleId)
                    && e.AddedRoleIds.Count == 0
                    && e.ChangeType == UserRolesChangeType.RoleDeleted),
                It.IsAny<CancellationToken>()),
            times);

    [Fact]
    public async Task Delete_PublishesARoleRemoval_ForEveryMember_AndOnlyForThem()
    {
        var alice = await CreateUserAsync(email: "alice@example.com");
        var bob = await CreateUserAsync(email: "bob@example.com");
        var outsider = await CreateUserAsync(email: "outsider@example.com");
        var payroll = await CreateRoleAsync("Payroll", alice, bob);
        await CreateRoleAsync("Support", outsider);

        var result = await _roles.DeleteAsync(payroll.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(await DbContext.UserRoles.AnyAsync(ur => ur.RoleId == payroll.Id));
        VerifyRemovalPublished(alice, payroll.Id, Times.Once());
        VerifyRemovalPublished(bob, payroll.Id, Times.Once());
        EventBusMock.Verify(
            x => x.PublishAsync(It.Is<UserRolesChangedEvent>(e => e.UserId == outsider.Id), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task DeleteMany_PublishesARoleRemoval_ForEveryMemberOfEveryDeletedRole()
    {
        var alice = await CreateUserAsync(email: "alice@example.com");
        var bob = await CreateUserAsync(email: "bob@example.com");
        var payroll = await CreateRoleAsync("Payroll", alice);
        var finance = await CreateRoleAsync("Finance", alice, bob);

        var result = await _roles.DeleteManyAsync([payroll.Id, finance.Id]);

        Assert.True(result.Succeeded, result.Message);
        VerifyRemovalPublished(alice, payroll.Id, Times.Once());
        VerifyRemovalPublished(alice, finance.Id, Times.Once());
        VerifyRemovalPublished(bob, finance.Id, Times.Once());
        VerifyRemovalPublished(bob, payroll.Id, Times.Never());
    }

    /// <summary>删除被拒（系统角色）时成员一个都没少，不能发「已摘掉」。</summary>
    [Fact]
    public async Task RefusedDelete_PublishesNothing()
    {
        var alice = await CreateUserAsync(email: "alice@example.com");
        var system = new Role { Id = Guid.NewGuid(), Name = "Root", NormalizedName = "ROOT", IsSystem = true };
        DbContext.Roles.Add(system);
        DbContext.UserRoles.Add(new UserRole { UserId = alice.Id, RoleId = system.Id });
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await _roles.DeleteAsync(system.Id);

        Assert.False(result.Succeeded);
        Assert.True(await DbContext.UserRoles.AnyAsync(ur => ur.RoleId == system.Id));
        EventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<UserRolesChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
}
