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
[ConfigSection("Identity:SignIn")]
[RuntimeSettingGroup(
    Key = "identity-registration",
    Module = "Identity",
    DisplayName = "Registration & Sign-in",
    I18nKey = "admin.modules.system.settings.groups.identityRegistration",
    Icon = "mdi:account-plus-outline",
    Order = 200)]
public class TnziSignInOptions
{
    /// <summary>
    /// 是否使用邮箱作为用户名（默认 true）
    /// </summary>
    [RuntimeSetting(Label = "Use Email As Username", I18n = "admin.modules.system.settings.fields.signInUseEmailAsUserName", Type = SettingFieldType.Boolean, Subsection = "Sign-in",
        Description = "Treat the email address as the username during sign-in and self-registration")]
    public bool UseEmailAsUserName { get; set; } = true;

    /// <summary>
    /// 是否允许用户名登录
    /// </summary>
    [RuntimeSetting(Label = "Allow Username Login", I18n = "admin.modules.system.settings.fields.allowUserNameLogin", Type = SettingFieldType.Boolean, Subsection = "Sign-in",
        Description = "Accept the username as the account identifier in the password form. With 'Use Email As Username' on, an email is stored as the username and still resolves here even when 'Allow Email Login' is off.")]
    public bool AllowUserNameLogin { get; set; } = true;

    /// <summary>
    /// 是否允许邮箱登录
    /// </summary>
    [RuntimeSetting(Label = "Allow Email Login", I18n = "admin.modules.system.settings.fields.allowEmailLogin", Type = SettingFieldType.Boolean, Subsection = "Sign-in",
        Description = "Accept the email address as the account identifier in the password form. Independent of verification codes: passwordless email code-login and email two-factor are controlled by 'Enable Email Codes' in the OTP / Verification Codes group.")]
    public bool AllowEmailLogin { get; set; } = true;

    /// <summary>
    /// 是否允许SMS登录
    /// </summary>
    [RuntimeSetting(Label = "Allow SMS Login", I18n = "admin.modules.system.settings.fields.allowSmsLogin", Type = SettingFieldType.Boolean, Subsection = "Sign-in",
        Description = "Accept the phone number as the account identifier in the password form. Independent of verification codes: passwordless SMS code-login and SMS two-factor are controlled by 'Enable SMS Codes' in the OTP / Verification Codes group.")]
    public bool AllowSmsLogin { get; set; } = false;

    /// <summary>
    /// 是否要求唯一邮箱
    /// </summary>
    public bool RequireUniqueEmail { get; set; } = true;
}

/// <summary>
/// 注册配置选项
/// </summary>
[ConfigSection("Identity:Registration")]
[RuntimeSettingGroup(
    Key = "identity-registration",
    Module = "Identity",
    DisplayName = "Registration & Sign-in",
    I18nKey = "admin.modules.system.settings.groups.identityRegistration",
    Icon = "mdi:account-plus-outline",
    Order = 200)]
public class RegistrationOptions
{
    /// <summary>
    /// 是否启用邮箱快速注册
    /// </summary>
    [RuntimeSetting(Label = "Enable Quick Register (Email)", I18n = "admin.modules.system.settings.fields.enableQuickRegisterEmail", Type = SettingFieldType.Boolean, Subsection = "Registration")]
    public bool EnableQuickRegisterEmail { get; set; } = false;

    /// <summary>
    /// 是否启用SMS快速注册
    /// </summary>
    [RuntimeSetting(Label = "Enable Quick Register (SMS)", I18n = "admin.modules.system.settings.fields.enableQuickRegisterSms", Type = SettingFieldType.Boolean, Subsection = "Registration")]
    public bool EnableQuickRegisterSms { get; set; } = false;

    /// <summary>
    /// 是否默认使用邮箱作为用户名（当未提供用户名时）
    /// </summary>
    [RuntimeSetting(Label = "Default Username From Email", I18n = "admin.modules.system.settings.fields.registrationDefaultUserNameFromEmail", Type = SettingFieldType.Boolean, Subsection = "Registration",
        Description = "When no username is supplied, derive it from the email address")]
    public bool DefaultUserNameFromEmail { get; set; } = true;

    /// <summary>
    /// 是否要求确认邮箱
    /// </summary>
    [RuntimeSetting(Label = "Require Email Confirmation", I18n = "admin.modules.system.settings.fields.requireConfirmedEmail", Type = SettingFieldType.Boolean, Subsection = "Registration")]
    public bool RequireConfirmedEmail { get; set; } = false;

    /// <summary>
    /// 是否要求确认手机
    /// </summary>
    [RuntimeSetting(Label = "Require Phone Confirmation", I18n = "admin.modules.system.settings.fields.requireConfirmedPhone", Type = SettingFieldType.Boolean, Subsection = "Registration")]
    public bool RequireConfirmedPhone { get; set; } = false;

    /// <summary>
    /// 设置密码令牌过期时间（分钟），用于快速注册后设置密码
    /// </summary>
    [RuntimeSetting(Label = "Set-Password Token Expiration (min)", I18n = "admin.modules.system.settings.fields.registrationSetPasswordTokenExpiration", Type = SettingFieldType.Int, Min = 1, Subsection = "Registration",
        Description = "Validity window (minutes) of the set-password token issued after quick registration")]
    public int SetPasswordTokenExpirationMinutes { get; set; } = 30;
}

/// <summary>
/// 密码找回配置选项
/// </summary>
[ConfigSection("Identity:Recovery")]
[RuntimeSettingGroup(
    Key = "identity-recovery",
    Module = "Identity",
    DisplayName = "Password Recovery",
    I18nKey = "admin.modules.system.settings.groups.identityRecovery",
    Icon = "mdi:lock-reset",
    Order = 230)]
public class RecoveryOptions
{
    /// <summary>
    /// 是否启用邮箱找回密码
    /// </summary>
    [RuntimeSetting(Label = "Enable Password Reset By Email", I18n = "admin.modules.system.settings.fields.recoveryEnableResetByEmail", Type = SettingFieldType.Boolean)]
    public bool EnablePasswordResetByEmail { get; set; } = true;

    /// <summary>
    /// 是否启用SMS找回密码
    /// </summary>
    [RuntimeSetting(Label = "Enable Password Reset By SMS", I18n = "admin.modules.system.settings.fields.recoveryEnableResetBySms", Type = SettingFieldType.Boolean)]
    public bool EnablePasswordResetBySms { get; set; } = false;

    /// <summary>
    /// 密码重置令牌过期时间（分钟）
    /// </summary>
    [RuntimeSetting(Label = "Reset Token Expiration (min)", I18n = "admin.modules.system.settings.fields.recoveryResetTokenExpiration", Type = SettingFieldType.Int, Min = 1,
        Description = "Validity window (minutes) of the password-reset token")]
    public int ResetTokenExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// 获取或设置 重置密码前端路由路径（默认：空字符串，使用后端兜底）
    /// 如果配置了此值（非空），邮件链接将使用 FrontendUrl + ResetPasswordRoute 指向前端
    /// 如果未配置此值（为空），邮件链接将指向后端 /auth/reset-password（框架内置兜底方案）
    /// 例如：/reset-password, /account/reset-password
    /// </summary>
    [RuntimeSetting(Label = "Reset Password Route", I18n = "admin.modules.system.settings.fields.recoveryResetPasswordRoute", Type = SettingFieldType.String,
        Description = "Frontend route for the reset-password page (empty = use the built-in backend page)")]
    public string ResetPasswordRoute { get; set; } = string.Empty;
}

/// <summary>
/// OTP/验证码配置选项
/// </summary>
[ConfigSection("Identity:Otp")]
[RuntimeSettingGroup(
    Key = "identity-otp",
    Module = "Identity",
    DisplayName = "OTP / Verification Codes",
    I18nKey = "admin.modules.system.settings.groups.identityOtp",
    Icon = "mdi:message-badge-outline",
    Order = 220)]
public class OtpOptions
{
    /// <summary>
    /// 验证码长度
    /// </summary>
    [RuntimeSetting(Label = "Code Length", I18n = "admin.modules.system.settings.fields.otpCodeLength", Type = SettingFieldType.Int, Min = 4, Max = 8,
        Description = "Number of digits in the one-time code")]
    public int CodeLength { get; set; } = 6;

    /// <summary>
    /// 验证码过期时间（分钟）
    /// </summary>
    [RuntimeSetting(Label = "Expiration (min)", I18n = "admin.modules.system.settings.fields.otpExpirationMinutes", Type = SettingFieldType.Int, Min = 1,
        Description = "How long (minutes) a code stays valid")]
    public int ExpirationMinutes { get; set; } = 5;

    /// <summary>
    /// 重发间隔（秒）
    /// </summary>
    [RuntimeSetting(Label = "Resend Interval (sec)", I18n = "admin.modules.system.settings.fields.otpResendInterval", Type = SettingFieldType.Int, Min = 0,
        Description = "Minimum wait (seconds) before a code can be resent")]
    public int ResendIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// 最大验证失败次数
    /// </summary>
    [RuntimeSetting(Label = "Max Attempts", I18n = "admin.modules.system.settings.fields.otpMaxAttempts", Type = SettingFieldType.Int, Min = 1,
        Description = "Maximum verification attempts before a code is rejected")]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// 是否启用SMS验证
    /// </summary>
    [RuntimeSetting(Label = "Enable SMS Codes", I18n = "admin.modules.system.settings.fields.otpEnableSms", Type = SettingFieldType.Boolean,
        Description = "Enable the SMS verification-code channel: passwordless SMS code-login and SMS two-factor. This does not accept the phone number as the account in the password form (that is 'Allow SMS Login' in Registration & Sign-in).")]
    public bool EnableSms { get; set; } = false;

    /// <summary>
    /// 是否启用Email验证
    /// </summary>
    [RuntimeSetting(Label = "Enable Email Codes", I18n = "admin.modules.system.settings.fields.otpEnableEmail", Type = SettingFieldType.Boolean,
        Description = "Enable the email verification-code channel: passwordless email code-login and email two-factor. This does not accept the email as the account in the password form (that is 'Allow Email Login' in Registration & Sign-in).")]
    public bool EnableEmail { get; set; } = true;

    /// <summary>
    /// 是否启用身份验证器(TOTP)两步验证。默认启用;关闭后个人中心不再展示 TOTP，用户也无法设置/启用 TOTP。
    /// 与 <see cref="EnableSms"/> / <see cref="EnableEmail"/> 对称，供不需要验证器方式的消费应用整体关闭。
    /// </summary>
    [RuntimeSetting(Label = "Enable Authenticator (TOTP)", I18n = "admin.modules.system.settings.fields.otpEnableTotp", Type = SettingFieldType.Boolean,
        Description = "Enable authenticator app (TOTP) two-factor. Turn off for deployments that do not use TOTP: the User Center hides it and setup is rejected. Unlike SMS/email, TOTP has no passwordless code-login, it is a second factor only.")]
    public bool EnableTotp { get; set; } = true;
}

/// <summary>
/// 图形验证码配置选项
/// GROUP MERGE：与 <see cref="MultiLoginOptions"/> 共享 identity-login 配置组。
/// </summary>
[ConfigSection("Identity:Captcha")]
[RuntimeSettingGroup(
    Key = "identity-login",
    Module = "Identity",
    DisplayName = "Login & Sessions",
    I18nKey = "admin.modules.system.settings.groups.identityLogin",
    Icon = "mdi:login-variant",
    Order = 205)]
public class CaptchaOptions
{
    /// <summary>
    /// 是否在注册时启用验证码
    /// </summary>
    [RuntimeSetting(Label = "Captcha On Register", I18n = "admin.modules.system.settings.fields.captchaEnableOnRegister", Type = SettingFieldType.Boolean, Subsection = "Captcha")]
    public bool EnableCaptchaOnRegister { get; set; } = false;

    /// <summary>
    /// 是否在登录时启用验证码
    /// </summary>
    [RuntimeSetting(Label = "Captcha On Login", I18n = "admin.modules.system.settings.fields.captchaEnableOnLogin", Type = SettingFieldType.Boolean, Subsection = "Captcha")]
    public bool EnableCaptchaOnLogin { get; set; } = false;

    /// <summary>
    /// 登录失败多少次后需要验证码
    /// </summary>
    [RuntimeSetting(Label = "Captcha Fail Threshold", I18n = "admin.modules.system.settings.fields.captchaFailThreshold", Type = SettingFieldType.Int, Min = 0, Subsection = "Captcha",
        Description = "Number of failed logins before a captcha is required (0 = disabled)")]
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
/// GROUP MERGE：与 <see cref="CaptchaOptions"/> 共享 identity-login 配置组。
/// </summary>
[ConfigSection("Identity:MultiLogin")]
[RuntimeSettingGroup(
    Key = "identity-login",
    Module = "Identity",
    DisplayName = "Login & Sessions",
    I18nKey = "admin.modules.system.settings.groups.identityLogin",
    Icon = "mdi:login-variant",
    Order = 205)]
public class MultiLoginOptions
{
    /// <summary>
    /// 是否允许多点登录（默认 true，同一账号可在多设备登录）
    /// </summary>
    [RuntimeSetting(Label = "Allow Multi-Login", I18n = "admin.modules.system.settings.fields.multiLoginAllow", Type = SettingFieldType.Boolean, Subsection = "Multi-Login",
        Description = "Allow the same account to be signed in on multiple devices")]
    public bool AllowMultiLogin { get; set; } = true;

    /// <summary>
    /// 并发登录冲突策略：Replace（替换旧会话）, Reject（拒绝新登录）
    /// </summary>
    [RuntimeSetting(Label = "On Conflict", I18n = "admin.modules.system.settings.fields.multiLoginOnConflict", Type = SettingFieldType.Select, Subsection = "Multi-Login",
        Description = "Behavior when the session limit is reached: replace the oldest session or reject the new login")]
    public LoginConflictPolicy OnConflict { get; set; } = LoginConflictPolicy.Replace;

    /// <summary>
    /// 最大并发会话数（仅当 AllowMultiLogin=true 时生效，0表示不限制）
    /// </summary>
    [RuntimeSetting(Label = "Max Concurrent Sessions", I18n = "admin.modules.system.settings.fields.multiLoginMaxConcurrentSessions", Type = SettingFieldType.Int, Min = 0, Subsection = "Multi-Login",
        Description = "Maximum concurrent sessions per account (0 = unlimited)")]
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
// 双轨冻结（DUAL-TRACK FREEZE）：MinLength / RequireDigit / RequireLowercase / RequireUppercase /
// RequireNonAlphanumeric 的真正强制路径在 ASP.NET Identity —— 启动期由 AddTnziIdentity 灌入
// options.Password.* 并 baked，UserManager.CreateAsync/AddPasswordAsync/ResetPasswordAsync 用这份
// 冷快照校验（注册流仅走此路径）。Tnzi 的 PasswordPolicyService（IOptionsMonitor，热）只在改密/重置
// 流做前置校验，无法覆盖注册流，且放宽方向会被 baked 的 UserManager 拒绝。因此这 5 个字段热改会产生
// 不一致行为（假热配），暂不暴露；真正热化需自定义 IPasswordValidator<User> 读 Monitor + 中和 baked
// options.Password.*（专项重构，见返回报告）。
// 仅暴露 PasswordHistoryCount / PasswordExpirationDays —— 二者只由 PasswordPolicyService
// (IOptionsMonitor) 强制，不进 ASP.NET Identity，真热。
[ConfigSection("Identity:PasswordPolicy")]
[RuntimeSettingGroup(
    Key = "identity-password",
    Module = "Identity",
    DisplayName = "Password Policy",
    I18nKey = "admin.modules.system.settings.groups.identityPassword",
    Icon = "mdi:form-textbox-password",
    Order = 225)]
public class PasswordPolicyOptions
{
    /// <summary>
    /// 最小密码长度（双轨冻结，见类注释）
    /// </summary>
    public int MinLength { get; set; } = 6;

    /// <summary>
    /// 是否需要数字（双轨冻结，见类注释）
    /// </summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>
    /// 是否需要小写字母（双轨冻结，见类注释）
    /// </summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>
    /// 是否需要大写字母（双轨冻结，见类注释）
    /// </summary>
    public bool RequireUppercase { get; set; } = false;

    /// <summary>
    /// 是否需要特殊字符（双轨冻结，见类注释）
    /// </summary>
    public bool RequireNonAlphanumeric { get; set; } = false;

    /// <summary>
    /// 密码历史记录数量（0表示不检查历史）
    /// </summary>
    [RuntimeSetting(Label = "Password History Count", I18n = "admin.modules.system.settings.fields.passwordPolicyHistoryCount", Type = SettingFieldType.Int, Min = 0,
        Description = "Number of previous passwords that cannot be reused (0 = no history check)")]
    public int PasswordHistoryCount { get; set; } = 0;

    /// <summary>
    /// 密码过期天数（0表示不过期）
    /// </summary>
    [RuntimeSetting(Label = "Password Expiration (days)", I18n = "admin.modules.system.settings.fields.passwordPolicyExpirationDays", Type = SettingFieldType.Int, Min = 0,
        Description = "Force a password change after this many days (0 = never expires)")]
    public int PasswordExpirationDays { get; set; } = 0;
}

/// <summary>
/// 账户安全配置选项
/// </summary>
[ConfigSection("Identity:AccountSecurity")]
[RuntimeSettingGroup(
    Key = "identity-security",
    Module = "Identity",
    DisplayName = "Account Security",
    I18nKey = "admin.modules.system.settings.groups.identitySecurity",
    Icon = "mdi:shield-account-outline",
    Order = 210)]
public class AccountSecurityOptions
{
    /// <summary>
    /// 最大登录失败次数（超过此次数将锁定账户）
    /// 双轨冻结：真正强制走 ASP.NET Identity options.Lockout.MaxFailedAccessAttempts（启动期 baked），
    /// SignInManager.CheckPasswordSignInAsync 用冷快照判锁定，Tnzi 侧不重算 —— 热改无效，暂不暴露。
    /// </summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>
    /// 账户锁定时间（分钟）
    /// 双轨冻结：真正强制走 ASP.NET Identity options.Lockout.DefaultLockoutTimeSpan（启动期 baked）—— 同上。
    /// </summary>
    public int LockoutDurationMinutes { get; set; } = 30;

    /// <summary>
    /// 是否启用账户锁定
    /// </summary>
    [RuntimeSetting(Label = "Enable Account Lockout", I18n = "admin.modules.system.settings.fields.enableLockout", Type = SettingFieldType.Boolean, Subsection = "Lockout")]
    public bool EnableLockout { get; set; } = true;

    /// <summary>
    /// 会话超时时间（分钟，0表示不过期）
    /// </summary>
    [RuntimeSetting(Label = "Session Timeout (min)", I18n = "admin.modules.system.settings.fields.accountSecuritySessionTimeout", Type = SettingFieldType.Int, Min = 0, Subsection = "Session",
        Description = "Inactivity timeout (minutes) for Redis-backed sessions when Session.ExpirationMinutes is 0 (0 = never expires)")]
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// 是否启用异常登录检测
    /// </summary>
    [RuntimeSetting(Label = "Enable Abnormal Login Detection", I18n = "admin.modules.system.settings.fields.accountSecurityEnableAbnormalDetection", Type = SettingFieldType.Boolean, Subsection = "Risk Scoring",
        Description = "Score each login for new IP / device / impossible travel and act on the risk level")]
    public bool EnableAbnormalLoginDetection { get; set; } = false;

    /// <summary>
    /// 新IP地址风险等级（0-100）
    /// </summary>
    [RuntimeSetting(Label = "New IP Risk Level", I18n = "admin.modules.system.settings.fields.accountSecurityNewIpRisk", Type = SettingFieldType.Int, Min = 0, Max = 100, Subsection = "Risk Scoring")]
    public int NewIpRiskLevel { get; set; } = 30;

    /// <summary>
    /// 新设备风险等级（0-100）
    /// </summary>
    [RuntimeSetting(Label = "New Device Risk Level", I18n = "admin.modules.system.settings.fields.accountSecurityNewDeviceRisk", Type = SettingFieldType.Int, Min = 0, Max = 100, Subsection = "Risk Scoring")]
    public int NewDeviceRiskLevel { get; set; } = 40;

    /// <summary>
    /// 位置变化风险等级（0-100）
    /// </summary>
    [RuntimeSetting(Label = "Location Change Risk Level", I18n = "admin.modules.system.settings.fields.accountSecurityLocationChangeRisk", Type = SettingFieldType.Int, Min = 0, Max = 100, Subsection = "Risk Scoring")]
    public int LocationChangeRiskLevel { get; set; } = 60;

    /// <summary>
    /// 不可能旅行风险等级（0-100）
    /// </summary>
    [RuntimeSetting(Label = "Impossible Travel Risk Level", I18n = "admin.modules.system.settings.fields.accountSecurityImpossibleTravelRisk", Type = SettingFieldType.Int, Min = 0, Max = 100, Subsection = "Risk Scoring")]
    public int ImpossibleTravelRiskLevel { get; set; } = 80;

    /// <summary>
    /// 频繁尝试风险等级（0-100）
    /// </summary>
    [RuntimeSetting(Label = "Frequent Attempts Risk Level", I18n = "admin.modules.system.settings.fields.accountSecurityFrequentAttemptsRisk", Type = SettingFieldType.Int, Min = 0, Max = 100, Subsection = "Risk Scoring")]
    public int FrequentAttemptsRiskLevel { get; set; } = 50;

    /// <summary>
    /// 高风险阈值（达到此值将要求验证）
    /// </summary>
    [RuntimeSetting(Label = "High Risk Threshold", I18n = "admin.modules.system.settings.fields.accountSecurityHighRiskThreshold", Type = SettingFieldType.Int, Min = 0, Max = 100, Subsection = "Risk Scoring",
        Description = "Risk score at or above which extra verification is required")]
    public int HighRiskThreshold { get; set; } = 70;

    /// <summary>
    /// 中等风险阈值（达到此值将发送通知）
    /// </summary>
    [RuntimeSetting(Label = "Medium Risk Threshold", I18n = "admin.modules.system.settings.fields.accountSecurityMediumRiskThreshold", Type = SettingFieldType.Int, Min = 0, Max = 100, Subsection = "Risk Scoring",
        Description = "Risk score at or above which a notification is sent")]
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
