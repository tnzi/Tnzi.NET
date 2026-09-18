using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 「启用 / 解锁」对账号锁定字段的真实效果 —— 必须走真 <see cref="UserManager{TUser}"/>。
///
/// ★★★ 此前 <c>EnableAsync</c> 先 <c>SetLockoutEnabledAsync(user, false)</c>
/// 再 <c>SetLockoutEndDateAsync(user, null)</c>，而 ASP.NET Identity 的后者在
/// <c>LockoutEnabled == false</c> 时直接返回 <c>UserLockoutNotEnabled</c> 失败且什么都不写。
/// 于是「停用 → 启用」的真实结果是：<c>LockoutEnabled</c> 落成 false、<c>LockoutEnd</c> 仍是
/// 一百年后、接口答「User enabled successfully」、列表继续显示已停用；与此同时该账号的
/// 登录失败锁定被永久关掉（<c>IsLockedOutAsync</c> 的第一句就是查 <c>LockoutEnabled</c>）。
/// 既有单测全部 mock 掉 UserManager，看不见这条前置条件。
/// </summary>
public class UserLockoutIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly UserService _service;

    public UserLockoutIntegrationTests()
    {
        _service = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: ServiceProvider.GetRequiredService<ICurrentUser>(),
            cache: Cache);
    }

    private async Task<User> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Users.SingleAsync(u => u.Id == id);
    }

    [Fact]
    public async Task Disable_Then_Enable_ClearsLockoutEnd_AndKeepsLockoutEnabled()
    {
        var user = await CreateUserAsync(email: "enable@example.com");

        var disabled = await _service.DisableAsync(user.Id, "test");
        Assert.True(disabled.Succeeded, disabled.Message);
        var afterDisable = await ReloadAsync(user.Id);
        Assert.True(afterDisable.LockoutEnabled);
        Assert.NotNull(afterDisable.LockoutEnd);

        var enabled = await _service.EnableAsync(user.Id);
        Assert.True(enabled.Succeeded, enabled.Message);

        var afterEnable = await ReloadAsync(user.Id);
        Assert.Null(afterEnable.LockoutEnd);
        Assert.True(afterEnable.LockoutEnabled, "Enable must keep the failed-login lockout mechanism armed.");
        Assert.False(await UserManager.IsLockedOutAsync(afterEnable));
    }

    [Fact]
    public async Task Enable_Then_MaxFailedPasswords_StillLocksOut()
    {
        var user = await CreateUserAsync(email: "bruteforce@example.com");
        await _service.DisableAsync(user.Id);
        var enabled = await _service.EnableAsync(user.Id);
        Assert.True(enabled.Succeeded, enabled.Message);

        var tracked = await UserManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(tracked);
        for (var i = 0; i < UserManager.Options.Lockout.MaxFailedAccessAttempts; i++)
        {
            await UserManager.AccessFailedAsync(tracked);
        }

        Assert.True(await UserManager.IsLockedOutAsync(tracked),
            "After an admin Enable the account must still lock out on repeated failed passwords.");
    }

    [Fact]
    public async Task Lock_Then_Unlock_ClearsLockoutEnd()
    {
        var user = await CreateUserAsync(email: "unlock@example.com");

        var locked = await _service.LockAsync(user.Id, DateTimeOffset.UtcNow.AddDays(3), "test");
        Assert.True(locked.Succeeded, locked.Message);

        var unlocked = await _service.UnlockAsync(user.Id);
        Assert.True(unlocked.Succeeded, unlocked.Message);

        var after = await ReloadAsync(user.Id);
        Assert.Null(after.LockoutEnd);
        Assert.True(after.LockoutEnabled);
    }

    /// <summary>
    /// 历史上被「启用」过的行 <c>LockoutEnabled</c> 已经是 false。解锁不能在这种行上永远失败，
    /// 它要顺手把锁定机制重新武装起来 —— 与 Enable 共用同一个原语。
    /// </summary>
    [Fact]
    public async Task Unlock_OnARowWithLockoutDisabled_RearmsLockout_AndClearsLockoutEnd()
    {
        var user = await CreateUserAsync(email: "legacy@example.com");
        user.LockoutEnabled = false;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        await SaveChangesAsync();

        var unlocked = await _service.UnlockAsync(user.Id);
        Assert.True(unlocked.Succeeded, unlocked.Message);

        var after = await ReloadAsync(user.Id);
        Assert.Null(after.LockoutEnd);
        Assert.True(after.LockoutEnabled);
    }

    /// <summary>
    /// 存量修复：此前每一次「启用」都留下一行 <c>LockoutEnabled = false</c>，那些账号自此没有
    /// 暴力破解保护且零症状。启动任务在锁定开启的部署里把它们置回 true。
    /// </summary>
    [Fact]
    public async Task RepairTask_RearmsLockout_OnRowsLeftDisabledByTheOldEnable()
    {
        var victim = await CreateUserAsync(email: "victim@example.com");
        var healthy = await CreateUserAsync(email: "healthy@example.com");
        victim.LockoutEnabled = false;
        await SaveChangesAsync();

        var options = new Tnzi.Identity.Options.IdentityOptions();
        options.AccountSecurity.EnableLockout = true;

        var repaired = await LockoutProtectionRepairStartupTask.RepairAsync(
            CreateRepository<User>(), options, NullLogger.Instance);

        Assert.Equal(1, repaired);
        Assert.True((await ReloadAsync(victim.Id)).LockoutEnabled);
        Assert.True((await ReloadAsync(healthy.Id)).LockoutEnabled);
    }

    /// <summary>
    /// 锁定整体关闭的部署里 <c>LockoutEnabled = false</c> 是合法状态（新用户就是这么建的），
    /// 修复任务不能碰。
    /// </summary>
    [Fact]
    public async Task RepairTask_DoesNothing_WhenLockoutIsDisabledByConfiguration()
    {
        var user = await CreateUserAsync(email: "nolockout@example.com");
        user.LockoutEnabled = false;
        await SaveChangesAsync();

        var options = new Tnzi.Identity.Options.IdentityOptions();
        options.AccountSecurity.EnableLockout = false;

        var repaired = await LockoutProtectionRepairStartupTask.RepairAsync(
            CreateRepository<User>(), options, NullLogger.Instance);

        Assert.Equal(0, repaired);
        Assert.False((await ReloadAsync(user.Id)).LockoutEnabled);
    }
}
