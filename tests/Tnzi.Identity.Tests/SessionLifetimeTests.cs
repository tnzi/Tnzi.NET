namespace Tnzi.Identity.Tests;

/// <summary>
/// 会话生命周期的三条算术：绝对上限怎么算、滑动续期怎么被它夹住、一条会话什么时候算死。
/// </summary>
/// <remarks>
/// ★ <b>闲置超时那一组尤其要在这里测。</b>它默认关闭（<c>SessionTimeoutMinutes = 0</c>），
/// 于是集成测试跑到的恒是「不启用」那条分支 —— 判据若留在服务的私有方法里，
/// <b>没有任何测试能证明它启用时真的工作</b>，而那正是这个开关此前的处境
/// （配置项在设置中心里，改它却什么都不会发生）。
/// </remarks>
public class SessionLifetimeTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    private static bool IsAlive(
        bool revoked = false,
        DateTime? expiresAt = null,
        DateTime? absoluteExpiresAt = null,
        DateTime? lastActivity = null,
        int idleMinutes = 0)
        => SessionLifetime.IsAlive(
            revoked, expiresAt, absoluteExpiresAt, lastActivity ?? Now, idleMinutes, Now);

    // ---- 闲置超时 ----

    [Fact]
    public void IdleTimeout_Disabled_NeverExpires()
    {
        // 十年没动，但没开这个开关 —— 照常活着。这是默认配置下的行为。
        Assert.True(IsAlive(lastActivity: Now.AddYears(-10), idleMinutes: 0));
    }

    [Fact]
    public void IdleTimeout_WithinWindow_IsAlive()
    {
        Assert.True(IsAlive(lastActivity: Now.AddMinutes(-29), idleMinutes: 30));
    }

    [Fact]
    public void IdleTimeout_Elapsed_IsDead()
    {
        Assert.False(IsAlive(lastActivity: Now.AddMinutes(-31), idleMinutes: 30));
    }

    /// <summary>边界取闭区间：正好到点即判死，不多给一个刻度。</summary>
    [Fact]
    public void IdleTimeout_ExactlyAtTheBoundary_IsDead()
    {
        Assert.False(IsAlive(lastActivity: Now.AddMinutes(-30), idleMinutes: 30));
    }

    /// <summary>负数与 0 同义：不启用（配置验证器也拒绝负数，这里是兜底）。</summary>
    [Fact]
    public void IdleTimeout_Negative_IsTreatedAsDisabled()
    {
        Assert.True(IsAlive(lastActivity: Now.AddYears(-10), idleMinutes: -5));
    }

    // ---- 绝对上限 ----

    [Fact]
    public void AbsoluteExpiry_Elapsed_IsDead_EvenWhenActive()
    {
        // 刚刚还在用、滑动窗口也没到 —— 但绝对上限过了。这正是它存在的意义。
        Assert.False(IsAlive(
            expiresAt: Now.AddDays(7),
            absoluteExpiresAt: Now.AddSeconds(-1),
            lastActivity: Now,
            idleMinutes: 60));
    }

    [Fact]
    public void AbsoluteExpiry_Null_MeansNoCap()
    {
        Assert.True(IsAlive(expiresAt: Now.AddDays(7), absoluteExpiresAt: null));
    }

    // ---- 滑动硬过期 / 撤销 ----

    [Fact]
    public void SlidingExpiry_Elapsed_IsDead()
    {
        Assert.False(IsAlive(expiresAt: Now.AddSeconds(-1)));
    }

    [Fact]
    public void Revoked_IsDeadRegardlessOfEveryOtherClock()
    {
        Assert.False(IsAlive(
            revoked: true,
            expiresAt: Now.AddDays(7),
            absoluteExpiresAt: Now.AddDays(30),
            lastActivity: Now,
            idleMinutes: 60));
    }

    [Fact]
    public void AllClocksHealthy_IsAlive()
    {
        Assert.True(IsAlive(
            expiresAt: Now.AddDays(7),
            absoluteExpiresAt: Now.AddDays(30),
            lastActivity: Now.AddMinutes(-1),
            idleMinutes: 60));
    }

    // ---- 续期夹紧 ----

    [Fact]
    public void Clamp_DoesNotPushPastTheAbsoluteCap()
    {
        var cap = Now.AddHours(2);
        Assert.Equal(cap, SessionLifetime.ClampToAbsolute(Now.AddDays(7), cap));
    }

    [Fact]
    public void Clamp_LeavesRequestsInsideTheCapAlone()
    {
        var requested = Now.AddHours(1);
        Assert.Equal(requested, SessionLifetime.ClampToAbsolute(requested, Now.AddHours(2)));
    }

    [Fact]
    public void Clamp_WithoutACap_IsIdentity()
    {
        var requested = Now.AddDays(7);
        Assert.Equal(requested, SessionLifetime.ClampToAbsolute(requested, null));
    }

    // ---- 后台维护阈值 ----

    /// <summary>
    /// ★★ 闲置超时不启用（默认）时<b>绝不能</b>参与取小 —— 直接 Min 会得到 0，
    /// 意味着「所有会话都算失活」，后台任务下一轮就把全站用户踢下线。
    /// </summary>
    [Fact]
    public void InactiveThreshold_IdleDisabled_FallsBackToTheRefreshWindow()
    {
        Assert.Equal(TimeSpan.FromDays(7), SessionLifetime.ResolveInactiveThreshold(0, 7));
        Assert.Equal(TimeSpan.FromDays(7), SessionLifetime.ResolveInactiveThreshold(-30, 7));
    }

    [Fact]
    public void InactiveThreshold_IdleShorter_Wins()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), SessionLifetime.ResolveInactiveThreshold(30, 7));
    }

    /// <summary>闲置超时配得比刷新周期还长时，取刷新周期 —— 会话本来就活不到那么久。</summary>
    [Fact]
    public void InactiveThreshold_IdleLonger_YieldsToTheRefreshWindow()
    {
        Assert.Equal(TimeSpan.FromDays(7), SessionLifetime.ResolveInactiveThreshold(60 * 24 * 30, 7));
    }

    /// <summary>刷新周期为 0 / 负数时按 1 天兜底（与既有实现同口径）。</summary>
    [Fact]
    public void InactiveThreshold_ClampsTheRefreshWindowToAtLeastOneDay()
    {
        Assert.Equal(TimeSpan.FromDays(1), SessionLifetime.ResolveInactiveThreshold(0, 0));
    }

    // ---- 绝对上限的计算 ----

    [Fact]
    public void ComputeAbsoluteExpiry_ZeroHours_MeansNoCap()
    {
        Assert.Null(SessionLifetime.ComputeAbsoluteExpiry(Now, 0));
    }

    [Fact]
    public void ComputeAbsoluteExpiry_CountsFromCreation()
    {
        Assert.Equal(Now.AddHours(720), SessionLifetime.ComputeAbsoluteExpiry(Now, 720));
    }
}
