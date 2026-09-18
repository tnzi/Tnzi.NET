namespace Tnzi.Identity.Services;

/// <summary>
/// 存量修复：把被旧版「启用」关掉的登录失败锁定重新武装起来。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么会有这样的行。</b>2026-09-12 之前 <c>UserService.EnableAsync</c> 的第一句是
/// <c>SetLockoutEnabledAsync(user, false)</c>。ASP.NET Identity 的 <c>IsLockedOutAsync</c>
/// 第一句就是查 <c>LockoutEnabled</c>，于是每一个被管理员「启用」过的账号从那一刻起
/// 密码可以无限次猜 —— <c>MaxFailedLoginAttempts</c> 对它逐字失效，登录日志里只是一串 Failed，
/// 没有任何一处会说「这个账号不再受锁定保护」。Enable 已经修好，但那些行还在。
/// </para>
/// <para>
/// ★ <b>只在 <c>AccountSecurity.EnableLockout</c> 为 true 时动手。</b>锁定整体关闭的部署里
/// <c>Lockout.AllowedForNewUsers</c> 也是 false，新用户建出来就是 <c>LockoutEnabled = false</c>，
/// 那是合法状态；而在锁定开启的部署里，这个字段除了旧版 Enable 没有别的来源会把它置 false
/// （停用 / 锁定 / 注销 / 邀请全部置 true）。
/// </para>
/// <para>
/// 每次启动跑一遍、幂等（修完之后 0 行命中）。绕过变更跟踪的整表 UPDATE：
/// 一条语句，不逐行加载，也不触发审计字段。
/// </para>
/// </remarks>
internal sealed class LockoutProtectionRepairStartupTask : IPostMigrationStartupTask
{
    public async Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Check.NotNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var users = scope.ServiceProvider.GetRequiredService<IRepository<User, Guid>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<LockoutProtectionRepairStartupTask>>();

        await RepairAsync(users, options, logger, cancellationToken);
    }

    /// <summary>
    /// 把 <c>LockoutEnabled = false</c> 的行置回 true；返回修复的行数。
    /// </summary>
    internal static async Task<int> RepairAsync(
        IRepository<User, Guid> users,
        IdentityOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(users);
        Check.NotNull(options);
        Check.NotNull(logger);

        if (!options.AccountSecurity.EnableLockout)
        {
            return 0;
        }

        var repaired = await users
            .Where(u => !u.LockoutEnabled)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LockoutEnabled, true), cancellationToken);

        if (repaired > 0)
        {
            logger.LogWarning(
                "Re-armed failed-login lockout on {Count} user account(s) that a previous Enable had left "
                + "without brute-force protection.",
                repaired);
        }

        return repaired;
    }
}
