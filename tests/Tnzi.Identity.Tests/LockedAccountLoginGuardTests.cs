namespace Tnzi.Identity.Tests;

/// <summary>
/// 框架内置守卫：账号锁定 / 停用即否决签发。
/// </summary>
/// <remarks>
/// ★ 这里只验守卫自身的裁决。「它有没有真的挂在各条签发路径上」是另一回事，
/// 由 <c>AuthServiceTests</c> 里那几条路径级用例把守 —— 两者都需要，
/// 一个正确但没被调用的守卫，与没有这个守卫是同一回事。
/// </remarks>
public class LockedAccountLoginGuardTests
{
    [Fact]
    public async Task LockedOutAccount_IsDenied()
    {
        var (guard, user) = CreateGuard(supportsLockout: true, isLockedOut: true);

        var result = await guard.EvaluateAsync(Context(user));

        Assert.False(result.Allowed);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.IDENTITY_USER_LOCKED, result.ErrorCode);
    }

    [Fact]
    public async Task UsableAccount_IsAllowed()
    {
        var (guard, user) = CreateGuard(supportsLockout: true, isLockedOut: false);

        var result = await guard.EvaluateAsync(Context(user));

        Assert.True(result.Allowed);
    }

    /// <summary>
    /// store 不支持锁定时放行 —— 与密码路径同源（<c>UserManager.IsLockedOutAsync</c>
    /// 在这种 store 上根本无法回答），不能因为问不出来就拒绝所有人。
    /// </summary>
    [Fact]
    public async Task StoreWithoutLockoutSupport_IsAllowed()
    {
        var (guard, user) = CreateGuard(supportsLockout: false, isLockedOut: true);

        var result = await guard.EvaluateAsync(Context(user));

        Assert.True(result.Allowed);
    }

    /// <summary>
    /// ★ 拒绝时如实告知，而不是伪装成「用户名或密码错误」。
    /// </summary>
    /// <remarks>
    /// 这是对 <c>ILoginGuard</c> 默认建议（<c>DenyAsInvalidCredentials</c>）的<strong>刻意例外</strong>，
    /// 所以要钉住：账号被停用这件事本人从别处也能知道，告知无害；
    /// 反过来说「密码错了」，会让被停用的用户一遍遍去重置密码。
    /// </remarks>
    [Fact]
    public async Task Denial_TellsTheTruth_RatherThanMimickingBadCredentials()
    {
        var (guard, user) = CreateGuard(supportsLockout: true, isLockedOut: true);

        var result = await guard.EvaluateAsync(Context(user));

        Assert.NotEqual("Invalid username or password", result.Message);
        Assert.NotEqual(ErrorCodes.IDENTITY_INVALID_PASSWORD, result.ErrorCode);
        // 真实原因带上登录方式，运维在登录日志里能看出是哪条路径被拦的。
        Assert.Contains(nameof(LoginMethod.Passkey), result.AuditReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// 排在所有消费方守卫之前：账号还能不能登录，比任何自定义准入策略都更基础。
    /// </summary>
    [Fact]
    public void Guard_RunsBeforeAnyConsumerGuard()
    {
        var (guard, _) = CreateGuard(supportsLockout: true, isLockedOut: false);

        // ILoginGuard.Order 的默认值是 0，消费方守卫通常不改它。
        Assert.True(guard.Order < 0);
    }

    /// <summary>
    /// ★★ 刷新遇到的是失败计数写出来的临时锁定（剩余时长不超过失败锁定窗口）时放行：
    /// 握着绑定会话的有效刷新令牌已经证明了身份，而把它当停用处理，等于让任何知道用户名的人输错几次密码
    /// 就能把受害者的全部在线会话撤掉。
    /// </summary>
    [Fact]
    public async Task Refresh_DuringATemporaryFailureLockout_IsAllowed()
    {
        var (guard, user) = CreateGuard(supportsLockout: true, isLockedOut: true, lockoutEnd: DateTimeOffset.UtcNow.AddMinutes(20));

        var result = await guard.EvaluateAsync(Context(user, LoginMethod.RefreshToken));

        Assert.True(result.Allowed);
    }

    /// <summary>对照组：同一枚临时锁定挡住每一条交互式登录，那正是锁定要挡的东西。</summary>
    [Theory]
    [InlineData(LoginMethod.Password)]
    [InlineData(LoginMethod.PasswordWithRefreshToken)]
    [InlineData(LoginMethod.Passkey)]
    [InlineData(LoginMethod.VerificationCode)]
    [InlineData(LoginMethod.TwoFactor)]
    [InlineData(LoginMethod.OAuth)]
    public async Task InteractiveSignIn_DuringATemporaryFailureLockout_IsDenied(LoginMethod method)
    {
        var (guard, user) = CreateGuard(supportsLockout: true, isLockedOut: true, lockoutEnd: DateTimeOffset.UtcNow.AddMinutes(20));

        var result = await guard.EvaluateAsync(Context(user, method));

        Assert.False(result.Allowed);
        Assert.Equal(ErrorCodes.IDENTITY_USER_LOCKED, result.ErrorCode);
    }

    /// <summary>
    /// 对照组：比失败锁定窗口长的锁定（停用 = 一百年后、管理员锁定默认一天），刷新照旧否决。
    /// 锁定截止时间读不出来时同样否决（失败关闭）。
    /// </summary>
    [Theory]
    [InlineData(36500d * 24 * 60)]
    [InlineData(24d * 60)]
    [InlineData(null)]
    public async Task Refresh_DuringALongerOrUnknownLockout_IsDenied(double? minutesLeft)
    {
        DateTimeOffset? end = minutesLeft.HasValue ? DateTimeOffset.UtcNow.AddMinutes(minutesLeft.Value) : null;
        var (guard, user) = CreateGuard(supportsLockout: true, isLockedOut: true, lockoutEnd: end);

        var result = await guard.EvaluateAsync(Context(user, LoginMethod.RefreshToken));

        Assert.False(result.Allowed);
        Assert.Equal(ErrorCodes.IDENTITY_USER_LOCKED, result.ErrorCode);
    }

    private static LoginGuardContext Context(User user, LoginMethod method = LoginMethod.Passkey)
        => new(user, method, "127.0.0.1", "test-agent");

    private static (LockedAccountLoginGuard Guard, User User) CreateGuard(bool supportsLockout, bool isLockedOut, DateTimeOffset? lockoutEnd = null)
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "guarded" };
        var store = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Object.Options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
        userManager.Setup(x => x.SupportsUserLockout).Returns(supportsLockout);
        userManager.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(isLockedOut);
        userManager.Setup(x => x.GetLockoutEndDateAsync(user)).ReturnsAsync(lockoutEnd);

        return (new LockedAccountLoginGuard(userManager.Object), user);
    }
}
