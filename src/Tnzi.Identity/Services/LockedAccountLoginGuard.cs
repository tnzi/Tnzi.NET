namespace Tnzi.Identity.Services;

/// <summary>
/// 框架内置守卫：账号处于锁定 / 停用状态时否决签发。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <strong>存在的理由是这道检查从来没有单独存在过。</strong>密码登录挡得住被停用的账号，
/// 靠的是 <c>SignInManager.CheckPasswordSignInAsync</c> 内部的 <c>PreSignInCheck</c>
/// <strong>顺手</strong>做掉的 —— 它绑在「校验密码」这个动作上。
/// 于是每引入一条<strong>凭据校验不在 SignInManager 里</strong>的登录方式
/// （验证码登录、OAuth、passkey），这道检查就跟着消失一次，
/// 而且<strong>不会有任何东西报错</strong>：被停用的用户照常拿到完整令牌。
/// </para>
/// <para>
/// ★ <strong>做成守卫而不是在每条路径上各查一遍</strong>：
/// <see cref="ILoginGuardEvaluator"/> 是全部签发路径的唯一共同调用点
/// （<c>AuthService</c> 的五处 + <c>OAuthService</c> + <c>RegistrationService</c>），
/// 挂在这里就是一处实现覆盖全部，且<strong>后续新增的登录方式自动受它保护</strong> ——
/// 逐处修补挡不住第八条路径出现。顺带还白拿了守卫链既有的语义：
/// 拒绝会累加失败计数、记一条带真实原因的登录失败日志。
/// </para>
/// <para>
/// ★ 用 <see cref="LoginGuardResult.Deny"/> 而不是
/// <see cref="LoginGuardResult.DenyAsInvalidCredentials"/>：走到守卫说明凭据已经过关，
/// 而「这个账号被停用了」本人从别处也能知道，告知无害 —— 这正是 <c>Deny</c> 的文档
/// 点名的适用场景。反过来对外说「用户名或密码错误」，会让被停用的用户去反复重置密码。
/// </para>
/// <para>
/// ★ 判定与密码路径<strong>同源</strong>（<c>UserManager.IsLockedOutAsync</c>，
/// 内含 <c>LockoutEnabled</c> 前置判断），所以两条路径不会对同一个账号给出不同答案。
/// 框架里的「停用账号 / 注销账户」都是 <c>SetLockoutEndDateAsync(UtcNow + 100 年)</c>
/// （见 <c>UserService</c>），因此对交互式登录而言，它们与「密码试错被临时锁定」是同一件事。
/// </para>
/// <para>
/// ★★★ <strong>唯一的例外是刷新令牌</strong>（<see cref="LoginMethod.RefreshToken"/>）遇到失败计数写出来的临时锁定。
/// 刷新被否决时会话连同刷新令牌一起撤销；若临时锁定也算，只知道用户名的匿名者输错几次密码就能把账号锁上，
/// 受害者每台设备的下一次刷新都被登出，锁定结束后也回不来，每个锁定周期重锁一次就能一直把他挡在外面。
/// 锁定挡的是新的登录尝试，握着一枚绑定会话的有效刷新令牌本身就证明了身份；access token 在锁定期间
/// 本来也照常可用。判据是剩余锁定时长不超过失败锁定窗口（<c>Lockout.DefaultLockoutTimeSpan</c>，
/// <c>AccessFailedAsync</c> 只写得出这么长的锁定）：停用（一百年）、管理员锁定（默认一天）都更长，照旧否决；
/// 读不出截止时间也照旧否决。管理员停用 / 锁定在动作当下就已经撤掉了会话，这里是它的拉取层兜底。
/// </para>
/// </remarks>
public sealed class LockedAccountLoginGuard : ILoginGuard
{
    /// <summary>
    /// 判定失败锁定时容忍的时钟偏差：锁定由一个实例写下、由另一个实例判定。
    /// </summary>
    private static readonly TimeSpan FailureLockoutClockSkew = TimeSpan.FromMinutes(2);

    private readonly UserManager<User> _userManager;

    /// <summary>
    /// 初始化一个 <see cref="LockedAccountLoginGuard"/> 类型的新实例。
    /// </summary>
    public LockedAccountLoginGuard(UserManager<User> userManager)
    {
        _userManager = Check.NotNull(userManager);
    }

    /// <summary>
    /// 最先执行：账号还能不能登录，比任何自定义准入策略都更基础。
    /// </summary>
    public int Order => int.MinValue;

    /// <inheritdoc />
    public async Task<LoginGuardResult> EvaluateAsync(LoginGuardContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        if (!_userManager.SupportsUserLockout || !await _userManager.IsLockedOutAsync(context.User))
        {
            return LoginGuardResult.Allow();
        }

        if (context.Method == LoginMethod.RefreshToken && await IsFailureLockoutAsync(context.User))
        {
            return LoginGuardResult.Allow();
        }

        return LoginGuardResult.Deny(
            "Account is locked",
            403,
            ErrorCodes.IDENTITY_USER_LOCKED,
            auditReason: $"Account is locked out (method: {context.Method})");
    }

    /// <summary>
    /// 这次锁定是不是失败计数能写出来的那种：剩余时长不超过一个失败锁定窗口（多给一点时钟偏差的余量）。
    /// 截止时间读不出来时答「不是」，按停用处理。
    /// </summary>
    private async Task<bool> IsFailureLockoutAsync(User user)
    {
        var lockoutEnd = await _userManager.GetLockoutEndDateAsync(user);
        if (!lockoutEnd.HasValue)
        {
            return false;
        }

        var failureWindow = _userManager.Options.Lockout.DefaultLockoutTimeSpan + FailureLockoutClockSkew;
        return lockoutEnd.Value - DateTimeOffset.UtcNow <= failureWindow;
    }
}
