namespace Tnzi.Identity.Options;

/// <summary>
/// Tnzi Identity模块配置选项（统一入口）
/// 配置路径：Identity
/// 注意：使用 Tnzi 前缀避免与 Microsoft.AspNetCore.Identity.IdentityOptions 冲突
/// </summary>
public class IdentityOptions
{
    /// <summary>
    /// JWT配置
    /// </summary>
    public JwtOptions Jwt { get; set; } = new();

    /// <summary>
    /// 登录配置
    /// </summary>
    public TnziSignInOptions SignIn { get; set; } = new();

    /// <summary>
    /// 注册配置
    /// </summary>
    public RegistrationOptions Registration { get; set; } = new();

    /// <summary>
    /// 密码找回配置
    /// </summary>
    public RecoveryOptions Recovery { get; set; } = new();

    /// <summary>
    /// OTP/验证码配置
    /// </summary>
    public OtpOptions Otp { get; set; } = new();

    /// <summary>
    /// 图形验证码配置
    /// </summary>
    public CaptchaOptions Captcha { get; set; } = new();

    /// <summary>
    /// 多点登录配置
    /// </summary>
    public MultiLoginOptions MultiLogin { get; set; } = new();

    /// <summary>
    /// 密码策略配置
    /// </summary>
    public PasswordPolicyOptions PasswordPolicy { get; set; } = new();

    /// <summary>
    /// 账户安全配置
    /// </summary>
    public AccountSecurityOptions AccountSecurity { get; set; } = new();

    /// <summary>
    /// OAuth2第三方登录配置
    /// </summary>
    public OAuthOptions OAuth { get; set; } = new();

    /// <summary>
    /// 是否启用双因素认证
    /// </summary>
    public bool EnableTwoFactor { get; set; } = false;

    /// <summary>
    /// 会话配置
    /// 配置路径：Identity:Session
    /// </summary>
    public SessionOptions Session { get; set; } = new();
}

/// <summary>
/// JWT配置选项
/// </summary>
public class JwtOptions
{
    /// <summary>
    /// 密钥（至少32字符）
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// 颁发者
    /// </summary>
    public string Issuer { get; set; } = "Tnzi";

    /// <summary>
    /// 受众
    /// </summary>
    public string Audience { get; set; } = "Tnzi";

    /// <summary>
    /// 访问令牌过期时间（分钟）
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// 刷新令牌过期时间（天）
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 7;

    /// <summary>
    /// 是否启用刷新令牌
    /// </summary>
    public bool EnableRefreshToken { get; set; } = true;
}

/// <summary>
/// 登录配置选项
/// 注意：使用 Tnzi 前缀避免与 Microsoft.AspNetCore.Identity.SignInOptions 冲突
/// </summary>
public class TnziSignInOptions
{
    /// <summary>
    /// 是否使用邮箱作为用户名（默认 true）
    /// </summary>
    public bool UseEmailAsUserName { get; set; } = true;

    /// <summary>
    /// 是否允许用户名登录
    /// </summary>
    public bool AllowUserNameLogin { get; set; } = true;

    /// <summary>
    /// 是否允许邮箱登录
    /// </summary>
    public bool AllowEmailLogin { get; set; } = true;

    /// <summary>
    /// 是否允许SMS登录
    /// </summary>
    public bool AllowSmsLogin { get; set; } = false;

    /// <summary>
    /// 是否要求唯一邮箱
    /// </summary>
    public bool RequireUniqueEmail { get; set; } = true;
}

/// <summary>
/// 注册配置选项
/// </summary>
public class RegistrationOptions
{
    /// <summary>
    /// 是否启用邮箱快速注册
    /// </summary>
    public bool EnableQuickRegisterEmail { get; set; } = false;

    /// <summary>
    /// 是否启用SMS快速注册
    /// </summary>
    public bool EnableQuickRegisterSms { get; set; } = false;

    /// <summary>
    /// 是否默认使用邮箱作为用户名（当未提供用户名时）
    /// </summary>
    public bool DefaultUserNameFromEmail { get; set; } = true;

    /// <summary>
    /// 是否要求确认邮箱
    /// </summary>
    public bool RequireConfirmedEmail { get; set; } = false;

    /// <summary>
    /// 是否要求确认手机
    /// </summary>
    public bool RequireConfirmedPhone { get; set; } = false;

    /// <summary>
    /// 设置密码令牌过期时间（分钟），用于快速注册后设置密码
    /// </summary>
    public int SetPasswordTokenExpirationMinutes { get; set; } = 30;
}

/// <summary>
/// 密码找回配置选项
/// </summary>
public class RecoveryOptions
{
    /// <summary>
    /// 是否启用邮箱找回密码
    /// </summary>
    public bool EnablePasswordResetByEmail { get; set; } = true;

    /// <summary>
    /// 是否启用SMS找回密码
    /// </summary>
    public bool EnablePasswordResetBySms { get; set; } = false;

    /// <summary>
    /// 密码重置令牌过期时间（分钟）
    /// </summary>
    public int ResetTokenExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// 获取或设置 重置密码前端路由路径（默认：空字符串，使用后端兜底）
    /// 如果配置了此值（非空），邮件链接将使用 FrontendUrl + ResetPasswordRoute 指向前端
    /// 如果未配置此值（为空），邮件链接将指向后端 /auth/reset-password（框架内置兜底方案）
    /// 例如：/reset-password, /account/reset-password
    /// </summary>
    public string ResetPasswordRoute { get; set; } = string.Empty;
}

/// <summary>
/// OTP/验证码配置选项
/// </summary>
public class OtpOptions
{
    /// <summary>
    /// 验证码长度
    /// </summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>
    /// 验证码过期时间（分钟）
    /// </summary>
    public int ExpirationMinutes { get; set; } = 5;

    /// <summary>
    /// 重发间隔（秒）
    /// </summary>
    public int ResendIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// 最大验证失败次数
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// 是否启用SMS验证
    /// </summary>
    public bool EnableSms { get; set; } = false;

    /// <summary>
    /// 是否启用Email验证
    /// </summary>
    public bool EnableEmail { get; set; } = true;
}

/// <summary>
/// 图形验证码配置选项
/// </summary>
public class CaptchaOptions
{
    /// <summary>
    /// 是否在注册时启用验证码
    /// </summary>
    public bool EnableCaptchaOnRegister { get; set; } = false;

    /// <summary>
    /// 是否在登录时启用验证码
    /// </summary>
    public bool EnableCaptchaOnLogin { get; set; } = false;

    /// <summary>
    /// 登录失败多少次后需要验证码
    /// </summary>
    public int CaptchaFailThreshold { get; set; } = 3;

    /// <summary>
    /// 验证码提供者（预留：reCAPTCHA, hCaptcha, 自定义等）
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 站点密钥（用于客户端）
    /// </summary>
    public string? SiteKey { get; set; }

    /// <summary>
    /// 服务端密钥
    /// </summary>
    public string? SecretKey { get; set; }
}

/// <summary>
/// 多点登录配置选项
/// </summary>
public class MultiLoginOptions
{
    /// <summary>
    /// 是否允许多点登录（默认 true，同一账号可在多设备登录）
    /// </summary>
    public bool AllowMultiLogin { get; set; } = true;

    /// <summary>
    /// 并发登录冲突策略：Replace（替换旧会话）, Reject（拒绝新登录）
    /// </summary>
    public LoginConflictPolicy OnConflict { get; set; } = LoginConflictPolicy.Replace;

    /// <summary>
    /// 最大并发会话数（仅当 AllowMultiLogin=true 时生效，0表示不限制）
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 0;
}

/// <summary>
/// 登录冲突策略
/// </summary>
public enum LoginConflictPolicy
{
    /// <summary>
    /// 替换旧会话（踢掉之前的登录）
    /// </summary>
    Replace,

    /// <summary>
    /// 拒绝新登录
    /// </summary>
    Reject
}

/// <summary>
/// 密码策略配置选项
/// </summary>
public class PasswordPolicyOptions
{
    /// <summary>
    /// 最小密码长度
    /// </summary>
    public int MinLength { get; set; } = 6;

    /// <summary>
    /// 是否需要数字
    /// </summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>
    /// 是否需要小写字母
    /// </summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>
    /// 是否需要大写字母
    /// </summary>
    public bool RequireUppercase { get; set; } = false;

    /// <summary>
    /// 是否需要特殊字符
    /// </summary>
    public bool RequireNonAlphanumeric { get; set; } = false;

    /// <summary>
    /// 密码历史记录数量（0表示不检查历史）
    /// </summary>
    public int PasswordHistoryCount { get; set; } = 0;

    /// <summary>
    /// 密码过期天数（0表示不过期）
    /// </summary>
    public int PasswordExpirationDays { get; set; } = 0;
}

/// <summary>
/// 账户安全配置选项
/// </summary>
public class AccountSecurityOptions
{
    /// <summary>
    /// 最大登录失败次数（超过此次数将锁定账户）
    /// </summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>
    /// 账户锁定时间（分钟）
    /// </summary>
    public int LockoutDurationMinutes { get; set; } = 30;

    /// <summary>
    /// 是否启用账户锁定
    /// </summary>
    public bool EnableLockout { get; set; } = true;

    /// <summary>
    /// 会话超时时间（分钟，0表示不过期）
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// 是否启用异常登录检测
    /// </summary>
    public bool EnableAbnormalLoginDetection { get; set; } = false;

    /// <summary>
    /// 新IP地址风险等级（0-100）
    /// </summary>
    public int NewIpRiskLevel { get; set; } = 30;

    /// <summary>
    /// 新设备风险等级（0-100）
    /// </summary>
    public int NewDeviceRiskLevel { get; set; } = 40;

    /// <summary>
    /// 位置变化风险等级（0-100）
    /// </summary>
    public int LocationChangeRiskLevel { get; set; } = 60;

    /// <summary>
    /// 不可能旅行风险等级（0-100）
    /// </summary>
    public int ImpossibleTravelRiskLevel { get; set; } = 80;

    /// <summary>
    /// 频繁尝试风险等级（0-100）
    /// </summary>
    public int FrequentAttemptsRiskLevel { get; set; } = 50;

    /// <summary>
    /// 高风险阈值（达到此值将要求验证）
    /// </summary>
    public int HighRiskThreshold { get; set; } = 70;

    /// <summary>
    /// 中等风险阈值（达到此值将发送通知）
    /// </summary>
    public int MediumRiskThreshold { get; set; } = 30;
}

/// <summary>
/// OAuth2第三方登录配置选项
/// </summary>
public class OAuthOptions
{
    /// <summary>
    /// Google OAuth配置
    /// </summary>
    public OAuthProviderOptions Google { get; set; } = new();

    /// <summary>
    /// Microsoft OAuth配置
    /// </summary>
    public OAuthProviderOptions Microsoft { get; set; } = new();

    /// <summary>
    /// Facebook OAuth配置
    /// </summary>
    public OAuthProviderOptions Facebook { get; set; } = new();

    /// <summary>
    /// Twitter OAuth配置
    /// </summary>
    public OAuthProviderOptions Twitter { get; set; } = new();

    /// <summary>
    /// GitHub OAuth配置
    /// </summary>
    public OAuthProviderOptions GitHub { get; set; } = new();
}

/// <summary>
/// OAuth提供商配置选项
/// </summary>
public class OAuthProviderOptions
{
    /// <summary>
    /// 客户端ID（ClientId/AppId/ConsumerKey）
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 客户端密钥（ClientSecret/AppSecret/ConsumerSecret）
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用该提供商
    /// </summary>
    public bool Enabled => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}
