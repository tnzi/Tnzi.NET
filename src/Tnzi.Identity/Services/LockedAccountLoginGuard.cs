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
/// （见 <c>UserService</c>），因此它们与「密码试错被临时锁定」在这里是同一件事。
/// </para>
/// </remarks>
public sealed class LockedAccountLoginGuard : ILoginGuard
{
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

        if (!_userManager.SupportsUserLockout)
        {
            return LoginGuardResult.Allow();
        }

        return await _userManager.IsLockedOutAsync(context.User)
            ? LoginGuardResult.Deny(
                "Account is locked",
                403,
                ErrorCodes.IDENTITY_USER_LOCKED,
                auditReason: $"Account is locked out (method: {context.Method})")
            : LoginGuardResult.Allow();
    }
}
