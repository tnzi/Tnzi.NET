namespace Tnzi.Identity.Services;

/// <summary>
/// 框架内置守卫：账号欠着<strong>阻断位</strong>的事时否决签发。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong>只看阻断位，不看义务位。</strong>
/// <see cref="PendingUserActions"/> 里的两组位语义完全不同：阻断位（未接受邀请）
/// 意味着这个人根本不该进来；义务位（必须改密之类）意味着他已经证明了身份，
/// 只是得先办件事。把义务位也在这里拒掉，用户会看到「登录失败」然后**无处可去** ——
/// 改密码本来就得先登录。义务由 <c>AuthService.IssueTokenAsync</c> 以挑战的形态处理。
/// </para>
/// <para>
/// ★ 具体挡住的是这些（每一条都不会报错、外观全部正常）：
/// <list type="bullet">
/// <item><strong>验证码登录</strong>：<c>AuthService.CodeLoginAsync</c> 会按邮箱找到预建账号，
/// 走「用户已存在」分支、<strong>顺手把 <c>EmailConfirmed</c> 置为 true</strong>，然后照常签发令牌。
/// 只要开着 <c>AllowCodeLogin</c>，被邀请人根本不必点那条邀请链接，收一封验证码邮件就进来了,
/// 带着预设角色，跳过消费应用要求的表单与二次验证。</item>
/// <item><strong>OAuth</strong>：第三方身份一旦与这个邮箱对上，同样直接签发。</item>
/// <item><strong>刷新令牌</strong>：接受邀请前不该存在刷新令牌，但这条路径同样过守卫，
/// 不需要为它单独写一份判断。</item>
/// </list>
/// </para>
/// <para>
/// ★ <strong>做成守卫而不是逐条路径各查一遍</strong>，理由与
/// <see cref="LockedAccountLoginGuard"/> 逐字相同：<see cref="ILoginGuardEvaluator"/>
/// 是全部签发路径的唯一共同调用点，挂在这里就是一处实现覆盖全部，
/// <strong>而且此后新增的登录方式自动受保护</strong>。那份注释里写下的规律
/// （每引入一条凭据校验不在 <c>SignInManager</c> 里的登录方式，绑在密码校验上的检查就消失一次）
/// 对这道检查一样成立。
/// </para>
/// <para>
/// ★ <strong>为什么不复用账号锁定</strong>：邀请创建的账号确实会同时被置上锁定，
/// 但那只是为了让既有的「活跃用户」口径自动把它排除，<strong>安全性一分钱都不押在它上面</strong>。
/// <c>UserService.EnableAsync</c> 会执行 <c>SetLockoutEnabledAsync(user, false)</c>，
/// 而 <c>UserManager.IsLockedOutAsync</c> 内含 <c>LockoutEnabled</c> 前置判断 ——
/// 管理员对一个未接受邀请的账号点一下「启用」，<see cref="LockedAccountLoginGuard"/> 就恒放行了。
/// 判定必须落在一个只有「办完那件事」能清掉的独立字段上。
/// </para>
/// <para>
/// ★ 用 <see cref="LoginGuardResult.Deny"/> 而不是
/// <see cref="LoginGuardResult.DenyAsInvalidCredentials"/>：走到守卫说明凭据已经过关，
/// 而「你的账号还没激活」正是本人需要知道的那句话 —— 含糊其辞只会让他反复去重置密码，
/// 而那条路同样被挡着（<c>PasswordService.ForgotPasswordAsync</c>）。
/// </para>
/// </remarks>
public sealed class PendingActionsLoginGuard : ILoginGuard
{
    /// <summary>
    /// 紧随 <see cref="LockedAccountLoginGuard"/>：两者都是「这个账号能不能登录」这一层，
    /// 比任何自定义准入策略都更基础。
    /// </summary>
    public int Order => int.MinValue + 1;

    /// <inheritdoc />
    public Task<LoginGuardResult> EvaluateAsync(LoginGuardContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        var blocking = context.User.PendingActions & PendingUserActions.Blocking;

        return Task.FromResult(blocking != PendingUserActions.None
            ? LoginGuardResult.Deny(
                "Account activation is pending. Please use the invitation link that was sent to you.",
                403,
                ErrorCodes.IDENTITY_ACTIVATION_PENDING,
                auditReason: $"Blocking pending actions: {blocking} (method: {context.Method})")
            : LoginGuardResult.Allow());
    }
}
