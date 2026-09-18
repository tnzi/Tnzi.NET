using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 三处按 <c>LockoutEnd</c>（<see cref="DateTimeOffset"/>）做大小比较的查询必须能在 SQLite 上执行。
/// </summary>
/// <remarks>
/// <para>
/// ★ EF Core 的 SQLite 提供者<b>不翻译</b> <see cref="DateTimeOffset"/> 的大小比较（只翻译等值），
/// 于是 <c>GET admin/users/statistics</c>、<c>GET admin/users?isLockedOut=</c> 与
/// <c>GET admin/login-security/overview</c> 在 SQLite 部署上一律 500 —— 而 SQLite 是框架宣称支持的提供者。
/// 此前两组租户裁剪的集成测试各自绕开了这条路径（「在 SQLite 上翻译不了」），于是它从没被任何测试跑到过。
/// </para>
/// <para>
/// 修法在 <c>IdentityDbContext</c>：SQLite 下把 <c>User.LockoutEnd</c> 映射成与 Microsoft.Data.Sqlite
/// 逐字相同格式的 UTC 文本，比较因此退化成字典序（对同一偏移量的 ISO 文本，字典序 = 时间序），存储形态不变、零迁移。
/// 其它提供者一字不动。
/// </para>
/// </remarks>
public class LockoutEndSqliteQueryTests : RelationalIdentityIntegrationTestBase
{
    private readonly UserService _users;
    private readonly LoginSecurityService _security;

    public LockoutEndSqliteQueryTests()
    {
        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: ServiceProvider.GetRequiredService<ICurrentUser>(),
            cache: Cache);
        _security = new LoginSecurityService(
            ServiceProvider,
            new UserTenantScopeProvider(CreateRepository<User>()),
            ServiceProvider.GetRequiredService<IOptionsMonitor<IdentityOptions>>(),
            CreateRepository<LoginLog>(),
            UserManager);
    }

    private async Task<(User locked, User expired, User never)> SeedAsync()
    {
        var locked = await CreateUserAsync(email: "locked@example.com");
        var expired = await CreateUserAsync(email: "expired@example.com");
        var never = await CreateUserAsync(email: "never@example.com");

        locked.LockoutEnabled = true;
        locked.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1);
        expired.LockoutEnabled = true;
        expired.LockoutEnd = DateTimeOffset.UtcNow.AddDays(-1);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (locked, expired, never);
    }

    [Fact]
    public async Task Statistics_CountActiveAndLockedUsers_OnSqlite()
    {
        await SeedAsync();

        var result = await _users.GetStatisticsAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.Data!.TotalUsers);
        Assert.Equal(2, result.Data.ActiveUsers);
        Assert.Equal(1, result.Data.LockedUsers);
    }

    [Fact]
    public async Task List_IsLockedOutFilter_SeparatesCurrentFromExpiredLockouts_OnSqlite()
    {
        var (locked, expired, never) = await SeedAsync();

        var lockedOnly = await _users.GetListAsync(new UserListQueryDto { PageIndex = 1, PageSize = 50, IsLockedOut = true });
        Assert.True(lockedOnly.Succeeded, lockedOnly.Message);
        Assert.Equal([locked.Id], lockedOnly.Data!.Items.Select(u => u.Id).ToArray());

        var notLocked = await _users.GetListAsync(new UserListQueryDto { PageIndex = 1, PageSize = 50, IsLockedOut = false });
        Assert.True(notLocked.Succeeded, notLocked.Message);
        var ids = notLocked.Data!.Items.Select(u => u.Id).ToHashSet();
        Assert.Contains(expired.Id, ids);
        Assert.Contains(never.Id, ids);
        Assert.DoesNotContain(locked.Id, ids);
    }

    [Fact]
    public async Task SecurityOverview_CountsLockedOutUsers_OnSqlite()
    {
        await SeedAsync();

        var result = await _security.GetSecurityOverviewAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Data!.LockedOutUsers);
    }

    /// <summary>
    /// 管理员传进来的 <c>LockoutEnd</c> 可以带非零偏移量（JSON 里的 <c>+08:00</c>）。
    /// 文本比较只对同一偏移量成立，所以写入时必须归一到 UTC，否则这一行在「是否仍锁定」的判断里会算错。
    /// </summary>
    [Fact]
    public async Task LockoutEnd_WithNonUtcOffset_StillComparesByInstant_OnSqlite()
    {
        var user = await CreateUserAsync(email: "offset@example.com");
        user.LockoutEnabled = true;
        // 一小时前到期的锁定，用 +08:00 表示：文本是「UTC 再加七小时」，不归一到 UTC 就会被字典序算成仍在未来。
        user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(-1).ToOffset(TimeSpan.FromHours(8));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var stats = await _users.GetStatisticsAsync();

        Assert.True(stats.Succeeded, stats.Message);
        Assert.Equal(0, stats.Data!.LockedUsers);
        Assert.Equal(1, stats.Data.ActiveUsers);

        var reloaded = await DbContext.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(user.LockoutEnd.Value.UtcDateTime, reloaded.LockoutEnd!.Value.UtcDateTime, TimeSpan.FromMilliseconds(1));
    }
}
