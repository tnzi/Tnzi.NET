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

    private static LoginGuardContext Context(User user)
        => new(user, LoginMethod.Passkey, "127.0.0.1", "test-agent");

    private static (LockedAccountLoginGuard Guard, User User) CreateGuard(bool supportsLockout, bool isLockedOut)
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "guarded" };
        var store = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(x => x.SupportsUserLockout).Returns(supportsLockout);
        userManager.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(isLockedOut);

        return (new LockedAccountLoginGuard(userManager.Object), user);
    }
}
