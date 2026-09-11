namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IInvitationAcceptanceHandler"/>
/// <remarks>
/// <para>
/// 框架自带的最小实现：<b>要求设置密码</b>，此外什么都不要求。
/// 够一个内部系统开箱跑起来，也仅此而已。
/// </para>
/// <para>
/// ★ <strong>要收别的字段、要强制绑 TOTP，就换掉整个实现</strong>（注册自己的
/// <see cref="IInvitationAcceptanceHandler"/> 即可覆盖，不必继承本类）。
/// 框架刻意不在这里堆配置开关：「要不要强制二次验证」在不同公司、
/// 甚至同一公司的不同角色之间都不一样，做成开关只会得到一个谁都不合用的半成品。
/// </para>
/// <para>
/// ★ 密码策略不在这里重复实现：<c>UserManager.AddPasswordAsync</c> 会跑完整的
/// <c>PasswordValidator</c>（长度、复杂度、以及框架自己的
/// <see cref="IPasswordPolicyService"/> 规则），失败信息原样回给前端。
/// </para>
/// </remarks>
public class DefaultInvitationAcceptanceHandler : ApplicationService, IInvitationAcceptanceHandler
{
    /// <summary>还差「设置密码」这一步时回给前端的步骤名。</summary>
    public const string SetPasswordStep = "SetPassword";

    private readonly UserManager<User> _userManager;

    /// <summary>
    /// 初始化一个 <see cref="DefaultInvitationAcceptanceHandler"/> 类型的新实例。
    /// </summary>
    public DefaultInvitationAcceptanceHandler(IServiceProvider serviceProvider, UserManager<User> userManager)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
    }

    /// <inheritdoc />
    public async Task<Result<InvitationAcceptOutcome>> AcceptAsync(
        User user,
        AcceptInvitationDto input,
        JsonElement? profile,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(user);
        Check.NotNull(input);

        if (string.IsNullOrWhiteSpace(input.Password))
        {
            // 不是错误，是「还没做完」：前端据此把设密码那一步显示出来。
            // 令牌因此不被消费，用户可以拿同一条链接回来。
            return Ok(InvitationAcceptOutcome.NeedsMore(SetPasswordStep));
        }

        // 账号是无密码创建的，所以是 Add 而不是 Reset。
        // 万一已经有密码（重复提交、或消费应用自己先设过），先移除再加，避免 AddPasswordAsync 报错。
        if (await _userManager.HasPasswordAsync(user))
        {
            var removed = await _userManager.RemovePasswordAsync(user);
            if (!removed.Succeeded)
            {
                return Fail<InvitationAcceptOutcome>(
                    $"Failed to set password: {removed.FormatErrors()}",
                    400,
                    ErrorCodes.IDENTITY_INVALID_PASSWORD);
            }
        }

        var result = await _userManager.AddPasswordAsync(user, input.Password);
        if (!result.Succeeded)
        {
            return Fail<InvitationAcceptOutcome>(
                $"Failed to set password: {result.FormatErrors()}",
                400,
                ErrorCodes.IDENTITY_PASSWORD_TOO_WEAK);
        }

        return Ok(InvitationAcceptOutcome.Done());
    }
}
