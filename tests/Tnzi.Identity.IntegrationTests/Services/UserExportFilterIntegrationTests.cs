using Microsoft.AspNetCore.Identity;
using Tnzi.EFCore;
using Tnzi.Exceptions;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 管理端用户 CSV 导出与列表是同一个集合：同样的筛选、同样的排序，超过上限时拒绝而不是截断。
/// </summary>
/// <remarks>
/// 管理台导出时带的就是当前列表的查询条件。此前导出只认关键词与组织：筛到「待接受邀请」再点导出，
/// 文件里是只按关键词匹配的全部账号；命中超过五万行时静默截断成前五万行。两种情况下文件本身都看不出异样。
/// </remarks>
public class UserExportFilterIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly UserService _users;

    public UserExportFilterIntegrationTests()
    {
        _users = CreateService();
    }

    private UserService CreateService(int exportMaxRows = UserService.DefaultExportMaxRows) => new(
        UserManager,
        ServiceProvider.GetRequiredService<RoleManager<Role>>(),
        CreateRepository<User>(),
        ServiceProvider,
        eventBus: EventBusMock.Object,
        currentUser: ServiceProvider.GetRequiredService<ICurrentUser>(),
        userRoleRepository: new EFCoreRepository<TestIdentityDbContext, UserRole>(DbContext, serviceProvider: ServiceProvider))
    {
        ExportMaxRows = exportMaxRows,
    };

    private sealed record Seeded(User Invitee, User Locked, User Plain, User RoleMember, Role Role);

    private async Task<Seeded> SeedAsync()
    {
        var invitee = await CreateUserAsync(email: "bravo-invitee@example.com", emailConfirmed: false, creationTime: DateTime.UtcNow.AddMinutes(-4));
        var locked = await CreateUserAsync(email: "delta-locked@example.com", creationTime: DateTime.UtcNow.AddMinutes(-3));
        var plain = await CreateUserAsync(email: "alpha-plain@example.com", creationTime: DateTime.UtcNow.AddMinutes(-2));
        var roleMember = await CreateUserAsync(email: "charlie-member@example.com", creationTime: DateTime.UtcNow.AddMinutes(-1));

        invitee.PendingActions = PendingUserActions.InvitationPending;
        locked.LockoutEnabled = true;
        locked.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1);

        var role = new Role { Id = Guid.NewGuid(), Name = "Auditors", NormalizedName = "AUDITORS" };
        DbContext.Roles.Add(role);
        DbContext.UserRoles.Add(new UserRole { UserId = roleMember.Id, RoleId = role.Id });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        return new Seeded(invitee, locked, plain, roleMember, role);
    }

    /// <summary>CSV 的数据行按顺序取首列 Id。</summary>
    private static List<Guid> ExportedIds(string csv) => csv
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\r'))
        .Where(line => line.Length > 0)
        .Skip(1)
        .Select(line => Guid.Parse(line.Split(',')[0]))
        .ToList();

    private async Task<List<Guid>> ExportAsync(UserListQueryDto query)
    {
        var result = await _users.ExportUsersCsvAsync(query);
        Assert.True(result.Succeeded, result.Message);
        return ExportedIds(result.Data!);
    }

    [Fact]
    public async Task Export_WithPendingActionFilter_ContainsOnlyThoseAccounts()
    {
        var seeded = await SeedAsync();

        var ids = await ExportAsync(new UserListQueryDto { PendingAction = PendingUserActions.InvitationPending });

        Assert.Equal([seeded.Invitee.Id], ids);
    }

    [Fact]
    public async Task Export_WithLockAndEmailConfirmedFilters_ContainsOnlyMatchingAccounts()
    {
        var seeded = await SeedAsync();

        Assert.Equal([seeded.Locked.Id], await ExportAsync(new UserListQueryDto { IsLockedOut = true }));
        Assert.Equal([seeded.Invitee.Id], await ExportAsync(new UserListQueryDto { IsEmailConfirmed = false }));
    }

    [Fact]
    public async Task Export_WithRoleFilter_ContainsOnlyRoleMembers()
    {
        var seeded = await SeedAsync();

        var ids = await ExportAsync(new UserListQueryDto { RoleId = seeded.Role.Id });

        Assert.Equal([seeded.RoleMember.Id], ids);
    }

    [Fact]
    public async Task Export_FollowsTheListSortOrder()
    {
        var seeded = await SeedAsync();

        var ids = await ExportAsync(new UserListQueryDto { SortBy = "email", SortDescending = false });

        Assert.Equal([seeded.Plain.Id, seeded.Invitee.Id, seeded.RoleMember.Id, seeded.Locked.Id], ids);
    }

    [Fact]
    public async Task Export_MatchesTheListForTheSameQuery()
    {
        await SeedAsync();
        var query = new UserListQueryDto { IsLockedOut = false, IsEmailConfirmed = true, PageIndex = 1, PageSize = 50 };

        var list = await _users.GetListAsync(query);
        var exported = await ExportAsync(query);

        Assert.True(list.Succeeded, list.Message);
        Assert.Equal(list.Data!.Items.Select(u => u.Id).ToList(), exported);
    }

    [Fact]
    public async Task Export_AboveTheRowLimit_IsRefusedNotTruncated()
    {
        await SeedAsync();
        var capped = CreateService(exportMaxRows: 3);

        var result = await capped.ExportUsersCsvAsync(new UserListQueryDto());

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_EXPORT_TOO_LARGE, result.ErrorCode);
        Assert.Contains("4 users", result.Message);
    }

    [Fact]
    public async Task Export_AtTheRowLimit_ReturnsEveryRow()
    {
        await SeedAsync();
        var capped = CreateService(exportMaxRows: 4);

        var result = await capped.ExportUsersCsvAsync(new UserListQueryDto());

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(4, ExportedIds(result.Data!).Count);
    }
}
