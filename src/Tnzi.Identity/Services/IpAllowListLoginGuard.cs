namespace Tnzi.Identity.Services;

/// <summary>
/// 框架内置守卫：账号开了登录 IP 允许列表时，只从列表内的地址签发令牌。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是守卫而不是控制器里的一次检查</b>：守卫跑在凭据校验之后、任何东西签发之前 ——
/// 没有会话被持久化、失败计数没有被清零、登录日志里没有一条成功记录。放在登录之后的检查
/// 已经造成了这三件事：多设备策略踢掉了一台合法设备、一次暴力尝试的计数被重置、审计里
/// 留下一次并没有发生的登录。
/// </para>
/// <para>
/// <b>为什么拒绝时与「密码错误」逐字相同</b>：守卫在密码校验之后执行，所以<b>任何</b>与凭据失败
/// 可区分的响应都在证明密码是对的，端点就此变成口令预言机 —— 而允许列表的全部前提恰恰是
/// 「凭据泄露了也进不来」。<see cref="LoginGuardResult.DenyAsInvalidCredentials"/> 返回同一个 400、
/// 同一句话，真实原因进登录日志，运维读得到、调用方读不到。
/// </para>
/// <para>
/// <b>三种放行，让一套部署锁不死自己</b>：账号没有策略行（绝大多数账号）、账号持有
/// <c>Identity:AccountSecurity:IpAllowListExemptRoles</c> 里的任一角色、列表为空。最后一条意味着
/// 「开关开着、框里没有」限制不了任何人 —— 写入路径本来就拒绝存这种行，这里是给绕过写入路径的行
/// 留的安全网：被一个空列表拒之门外的第一个人，就是来修它的那个人。
/// </para>
/// <para>
/// <b>刷新令牌也受它管</b>：刷新是一条签发路径，同样过守卫链。于是把一个账号的允许列表收紧之后，
/// 它在列表外已建立的会话会在下一次刷新时结束，而不是活到自然过期。
/// </para>
/// <para>
/// <b>刷新被拒时如实说原因</b>（403 + <see cref="ErrorCodes.IDENTITY_SIGN_IN_IP_NOT_ALLOWED"/>）：
/// 口令预言机的前提是「这个响应证明了一个调用方原本不知道的秘密」，而刷新的调用方手里本来就攥着一枚
/// 有效的刷新令牌，拒绝理由不再证明任何东西。反过来，照搬「用户名或密码错误」会把一个正在换网络的
/// 合法用户送回登录页，让他对着一个正确的密码反复重试。其它签发路径（密码、验证码、2FA、passkey……）
/// 仍然与密码错误同形。
/// </para>
/// </remarks>
public sealed class IpAllowListLoginGuard : ILoginGuard
{
    private readonly IRepository<UserSignInPolicy, Guid> _policies;
    private readonly UserManager<User> _userManager;
    private readonly IOptionsMonitor<IdentityOptions> _options;
    private readonly ICurrentTenant? _currentTenant;

    public IpAllowListLoginGuard(
        IRepository<UserSignInPolicy, Guid> policies,
        UserManager<User> userManager,
        IOptionsMonitor<IdentityOptions> options,
        ICurrentTenant? currentTenant = null)
    {
        _policies = Check.NotNull(policies);
        _userManager = Check.NotNull(userManager);
        _options = Check.NotNull(options);
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// 排在两条「这个账号能不能登录」的内置守卫之后、消费应用的自定义守卫（默认 0）之前：
    /// 它要查一次表，比锁定判断贵，比多数自定义策略便宜。
    /// </summary>
    public int Order => -100;

    /// <summary>刷新路径被允许列表拒绝时对外的文案。</summary>
    public const string RefreshDeniedMessage =
        "Your current network is not on the list of addresses this account is allowed to sign in from. Sign in again from an allowed network.";

    /// <inheritdoc />
    public async Task<LoginGuardResult> EvaluateAsync(LoginGuardContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        var userId = context.User.Id;
        var policy = await _policies.AsQueryable()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (policy == null || !policy.IpAllowListEnabled)
        {
            return LoginGuardResult.Allow();
        }

        // 只有真的开了限制才去查角色：豁免是例外路径，不该让每次登录都多一次角色查询。
        var exemptRoles = _options.CurrentValue.AccountSecurity.IpAllowListExemptRoles;
        var roles = exemptRoles.Length > 0
            ? await GetRolesInUsersTenantAsync(context.User)
            : [];

        return Decide(policy, context.IpAddress, roles, exemptRoles, context.Method);
    }

    /// <summary>
    /// 在账号自己的租户下读角色。
    /// </summary>
    /// <remarks>
    /// ★ <see cref="Role"/> 是多租户实体，全局过滤器按当前租户严格等值。登录是匿名请求，当前租户来自请求解析
    /// （域名 / 请求头 / 无），未必是这个账号的租户；照原样查会读到空角色集，豁免角色落空，
    /// 来恢复账号的那个人正好被拒在门外。与签发令牌时读角色同一个做法：切到账号的租户再查。
    /// </remarks>
    private async Task<IList<string>> GetRolesInUsersTenantAsync(User user)
    {
        if (_currentTenant != null && user.TenantId.HasValue && _currentTenant.Id != user.TenantId)
        {
            using (_currentTenant.Change(user.TenantId.Value))
            {
                return await _userManager.GetRolesAsync(user) ?? [];
            }
        }

        return await _userManager.GetRolesAsync(user) ?? [];
    }

    /// <summary>
    /// 裁决本身，不含 I/O，可以在没有数据库的情况下逐条验证。
    /// </summary>
    /// <param name="policy">账号的策略行；没有时为 null</param>
    /// <param name="ipAddress">框架为本次尝试解析出的客户端地址</param>
    /// <param name="userRoles">账号持有的角色名</param>
    /// <param name="exemptRoles">持有即豁免的角色名（不区分大小写）</param>
    /// <param name="method">本次签发的登录方式；只有 <see cref="LoginMethod.RefreshToken"/> 会拿到如实的拒绝理由</param>
    public static LoginGuardResult Decide(
        UserSignInPolicy? policy,
        string? ipAddress,
        IEnumerable<string> userRoles,
        IEnumerable<string> exemptRoles,
        LoginMethod method = LoginMethod.Password)
    {
        Check.NotNull(userRoles);
        Check.NotNull(exemptRoles);

        if (policy == null || !policy.IpAllowListEnabled)
        {
            return LoginGuardResult.Allow();
        }

        if (userRoles.Intersect(exemptRoles, StringComparer.OrdinalIgnoreCase).Any())
        {
            return LoginGuardResult.Allow();
        }

        var entries = SignInIpAllowList.ParseEntries(policy.AllowedIps);
        if (entries.Count == 0 || SignInIpAllowList.IsAllowed(ipAddress, entries))
        {
            return LoginGuardResult.Allow();
        }

        var shown = string.IsNullOrWhiteSpace(ipAddress) ? "an unknown address" : ipAddress;
        var auditReason = $"IP allow-list: {shown} is not on this account's sign-in allow-list";
        return method == LoginMethod.RefreshToken
            ? LoginGuardResult.Deny(RefreshDeniedMessage, 403, ErrorCodes.IDENTITY_SIGN_IN_IP_NOT_ALLOWED, auditReason)
            : LoginGuardResult.DenyAsInvalidCredentials(auditReason);
    }
}
