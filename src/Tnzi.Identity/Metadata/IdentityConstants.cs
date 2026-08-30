namespace Tnzi.Identity.Metadata;

/// <summary>
/// Identity模块常量定义
/// </summary>
public static class IdentityConstants
{
    /// <summary>
    /// 本模块的表名前缀（不含下划线）。
    /// </summary>
    /// <remarks>
    /// ★ <strong>绝大多数实体不需要引用它</strong>：框架的 <c>TableNamePrefixConfiguration</c>
    /// 会按实体所在程序集反查模块，自动把前缀加上，所以 <c>ToTable("User")</c> 就够了。
    /// 它存在是为了那些<strong>不住在本程序集里的实体</strong> —— 运行时自带的
    /// <c>IdentityUserPasskey&lt;TKey&gt;</c> 属于 <c>Microsoft.Extensions.Identity.Stores</c>，
    /// 自动前缀对它不生效，表名必须在配置里写全。有这个常量，写全的那处才不会与
    /// <see cref="IdentityModule.TableNamePrefix"/> 脱钩。
    /// </remarks>
    public const string TablePrefix = "Identity";

    /// <summary>
    /// Token提供者类型常量
    /// </summary>
    public static class TokenProvider
    {
        public const string JWT = "JWT";
        public const string TwoFactor = "2FA";
        public const string Identity = "Identity";
    }

    /// <summary>
    /// Token名称常量
    /// </summary>
    public static class TokenName
         
    {
        public const string RefreshToken = "RefreshToken";
        public const string TempToken = "TempToken";
        public const string SetPassword = "SetPassword";
    }

    /// <summary>
    /// 登录提供者常量
    /// </summary>
    public static class LoginProvider
    {
        public const string JWT = "JWT";
        public const string CodeLogin = "CodeLogin";
        public const string Registration = "Registration";

        /// <summary>Passkey（WebAuthn）。也是 passkey 注册令牌在 <c>AuthToken</c> 里的归属标记。</summary>
        public const string Passkey = "Passkey";
    }

    /// <summary>
    /// JWT claim 类型常量（框架自管，非标准映射名，读写两端一致）
    /// </summary>
    public static class ClaimTypeNames
    {
        /// <summary>
        /// 登录会话ID claim。写端由 <c>JwtTokenService</c> 写入，读端由 JWT Bearer 的
        /// <c>OnTokenValidated</c> 钩子据此校验会话有效性。刻意用不参与 inbound 映射的
        /// 自定义名（同 <c>tenant_id</c>），读写两端按同名取用，避免被 <c>MapInboundClaims</c> 改写。
        /// </summary>
        public const string SessionId = "session_id";
    }

    /// <summary>
    /// 2FA类型名称常量
    /// </summary>
    public static class TwoFactorTypeName
    {
        public const string Sms = "Sms";
        public const string Email = "Email";
        public const string Totp = "Totp";
    }

    /// <summary>
    /// UserDetail字段名常量
    /// </summary>
    public static class UserDetailField
    {
        public const string Nickname = "Nickname";
        public const string Avatar = "Avatar";
    }
}
