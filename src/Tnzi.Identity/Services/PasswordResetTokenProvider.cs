namespace Tnzi.Identity.Services;

/// <summary>
/// 密码重置令牌的提供者：有效期取 <c>Identity:Recovery:ResetTokenExpirationMinutes</c>。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 此前重置令牌走 ASP.NET Identity 的 <c>Default</c> 提供者，有效期是它的默认值 <b>1 天</b>；
/// <c>ResetTokenExpirationMinutes</c> 只被邮件文案读，设置中心里调它、邮件里写着 N 分钟，链接却在 24 小时内都能改密码。
/// 不改全局的 <see cref="DataProtectionTokenProviderOptions.TokenLifespan"/>：邮箱确认、换绑邮箱等同样走 <c>Default</c>，
/// 它们的有效期与找回密码无关。所以重置单独挂一个提供者，只有 <c>Tokens.PasswordResetTokenProvider</c> 指向它。
/// </para>
/// <para>
/// 有效期是运行时设置：提供者随每个 <see cref="UserManager{TUser}"/>（按请求）新建，构造时从
/// <see cref="IOptionsMonitor{TOptions}"/> 读当前值，改了设置下一个请求即生效。
/// 保护器名沿用默认的 <c>DataProtectorTokenProvider</c>：升级前发出、仍在新有效期内的链接照常可用。
/// </para>
/// </remarks>
public class PasswordResetTokenProvider : DataProtectorTokenProvider<User>
{
    /// <summary>在 <c>IdentityOptions.Tokens.ProviderMap</c> 里的名字。</summary>
    public const string ProviderName = "TnziPasswordReset";

    /// <summary>
    /// 初始化一个 <see cref="PasswordResetTokenProvider"/> 类型的新实例。
    /// </summary>
    public PasswordResetTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptionsMonitor<IdentityOptions> identityOptions,
        ILogger<DataProtectorTokenProvider<User>> logger)
        : base(dataProtectionProvider, new OptionsWrapper<DataProtectionTokenProviderOptions>(CreateOptions(identityOptions)), logger)
    {
    }

    private static DataProtectionTokenProviderOptions CreateOptions(IOptionsMonitor<IdentityOptions> identityOptions)
    {
        Check.NotNull(identityOptions);
        return new DataProtectionTokenProviderOptions
        {
            TokenLifespan = TimeSpan.FromMinutes(identityOptions.CurrentValue.Recovery.ResetTokenExpirationMinutes),
        };
    }
}
