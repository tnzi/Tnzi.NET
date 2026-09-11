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

    /// <summary>
    /// Passkey（WebAuthn）配置
    /// 配置路径：Identity:Passkey
    /// </summary>
    public PasskeyOptions Passkey { get; set; } = new();

    /// <summary>
    /// 二次确认（step-up）配置
    /// 配置路径：Identity:StepUp
    /// </summary>
    public StepUpOptions StepUp { get; set; } = new();

    /// <summary>
    /// 令牌交付方式配置（刷新令牌走响应体还是 HttpOnly cookie）
    /// 配置路径：Identity:TokenDelivery
    /// </summary>
    public TokenDeliveryOptions TokenDelivery { get; set; } = new();

    /// <summary>
    /// 邀请注册配置
    /// 配置路径：Identity:Invitation
    /// </summary>
    public InvitationOptions Invitation { get; set; } = new();
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

    /// <summary>
    /// 刷新令牌轮换的宽限窗（秒）。默认 10；0 表示不留宽限（上一代令牌一出现即判重放）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 轮换之后，上一代令牌在本窗口内再次出现按<b>并发刷新</b>处理（返回当前这一代，不再轮换）；
    /// 超出窗口才判定为<b>重放</b>并撤销整条会话。
    /// </para>
    /// <para>
    /// ★ <b>没有这个窗口，重放检测会把正常用户当成攻击者。</b>多标签页的 SPA、请求重试、
    /// 移动端断线重连都会让同一枚旧令牌在极短时间内被交换两次：前端的刷新互斥锁是
    /// <b>单实例内</b>的，两个标签页各有各的实例却共享同一份存储。没有宽限窗，
    /// 用户开两个标签页就会被整条会话踢掉，而现象是「随机掉线」，极难归因。
    /// 这也是 Auth0 把 rotation overlap period 做成一等配置项的原因。
    /// </para>
    /// <para>
    /// 上限不宜太大：窗口有多长，一枚被盗令牌就有多长时间可以安静地跟着用而不触发检测。
    /// 十秒足够覆盖并发与重试，也短到攻击者无法据此规划。
    /// </para>
    /// </remarks>
    public int RefreshTokenRotationOverlapSeconds { get; set; } = 10;
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
    /// 是否允许免密的验证码登录（<c>POST /auth/code-login</c>）。默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// ★ 这个开关存在的理由是<strong>解耦</strong>：在它之前，想关掉免密验证码登录只能把
    /// <c>Otp.EnableEmail</c> / <c>Otp.EnableSms</c> 关掉，而那两个是**渠道总闸** ——
    /// 会连带关掉邮箱/短信两步验证与邮箱/短信找回密码。想少一种登录方式的部署，
    /// 不该被迫连 2FA 一起放弃。
    /// <para>
    /// 与渠道开关是**与**的关系：关掉任一边，验证码登录都不可用。默认 <c>true</c> 因此不改变
    /// 既有部署的行为（此前的实际可用性完全由渠道开关决定）。
    /// </para>
    /// <para>
    /// ⚠ 判定落在<strong>服务层</strong>（发码与登录两个入口都拒绝），不是只在
    /// <c>/auth/config</c> 里报告一声 —— 前端据它隐藏入口，但端点必须自己挡得住。
    /// </para>
    /// </remarks>
    [RuntimeSetting(Label = "Allow Code Login", I18n = "admin.modules.system.settings.fields.allowCodeLogin", Type = SettingFieldType.Boolean, Subsection = "Sign-in",
        Description = "Allow passwordless sign-in with an emailed / texted verification code. Turning this off leaves email & SMS two-factor and code-based password recovery working - those are governed by the OTP channel switches, which this one is independent of. The delivery channel must also be on ('Enable Email Codes' / 'Enable SMS Codes').")]
    public bool AllowCodeLogin { get; set; } = true;

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
    /// 是否允许自助注册（<c>POST auth/register</c>：用户名 + 密码 + 邮箱）。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <strong>此前这条路径根本没有开关。</strong><c>RegistrationOptions</c> 只有两个
    /// quick-register 标志（都默认 false），而 <c>RegisterAsync</c> 校验完图形验证码就建账号 ——
    /// 于是 <c>GET auth/config</c> 出厂报 <c>enableRegistration: false</c>、登录页把注册入口藏起来，
    /// 而 <c>POST auth/register</c> 照样给任何人开户。这正是本模块在验证码登录上写过的那句话：
    /// <b>隐藏的是入口，端点仍然可达</b>。
    /// </para>
    /// <para>
    /// ★ <strong>默认取 <c>false</c></strong>，与旁边两个 quick-register 开关一致（deny-by-default）。
    /// 框架自带完整的邀请流程，「开好账号请人进来」是内部系统的常态；开放注册应当是一个
    /// 有人明确按下的决定。⚠ 这是<b>运行时行为变更</b>：依赖开放注册的部署升级后
    /// <c>auth/register</c> 会返回 400，必须在发版说明里点名。
    /// </para>
    /// </remarks>
    [RuntimeSetting(Label = "Enable Self Registration", I18n = "admin.modules.system.settings.fields.enableSelfRegistration", Type = SettingFieldType.Boolean, Subsection = "Registration",
        Description = "Allow anonymous visitors to create an account with username + password")]
    public bool EnableSelfRegistration { get; set; } = false;

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
    /// 闲置超时（分钟，0 = 不启用）。超过这么久没有任何请求，会话即失效，需要重新登录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <b>这个开关此前是假的。</b>它只在 Redis 会话且「没有显式过期时间」的那条分支里被读过一次，
    /// 而调用方一直传显式过期时间 —— 那条分支进不去；数据库会话则一处都不读。
    /// 于是管理员在设置中心把它从 60 改成 15，界面提示保存成功，行为零变化。
    /// 现在它是两种会话存储共同的闲置超时判据（OWASP ASVS 7.3.1）。
    /// </para>
    /// <para>
    /// ★ 与之配套的是「活动时间要有人更新」：此前 <c>LastActivityTime</c> 只在刷新令牌轮换
    /// 和一个管理端点里更新，普通业务请求根本不更新 —— 就算这个判据当时接上了，
    /// 它也只会把正常使用的人按时踢下线。现在每请求校验时按分钟粒度续期。
    /// </para>
    /// <para>
    /// ★★ <b>默认 0（不启用），这是刻意的。</b>把它接上之前，这个开关从未生效过；
    /// 如果同时把默认值留在 60，每一个现有部署都会在升级当天凭空多出一条
    /// 「离开一小时就要重新登录」的规则 —— 而用户会把它当 bug 报，且归因不到这个设置项上。
    /// 一个从来没生效过的开关，不该在开始生效的同一刻改变所有人的行为。
    /// </para>
    /// <para>
    /// 它因此是**按需开启的加固项**：内部管理系统、涉敏数据的后台，设 15-30 分钟是常见取值；
    /// 面向消费者的应用通常不设。此前显式配过非 0 值的部署，升级后那个值会第一次真的生效。
    /// </para>
    /// <para>
    /// 它与 <c>Identity:Session:AbsoluteLifetimeHours</c> 是两件事：
    /// 这条看「多久没动」，那条看「从登录起过了多久」。不启用本项时，
    /// 结束一条会话的就只剩滑动硬过期（随刷新令牌续）与那条绝对上限。
    /// </para>
    /// <para>
    /// ⚠ 精度受两个粒度限制：活动时间的写入按 60 秒节流、校验结果按
    /// <c>Session:ValidationCacheSeconds</c>（默认 30 秒）缓存，实际到期落在
    /// 「N 分钟 −1 分钟 ~ +30 秒」。设成 1-2 分钟不可靠，实用下限约 5 分钟。
    /// </para>
    /// </remarks>
    [RuntimeSetting(Label = "Session Idle Timeout (min)", I18n = "admin.modules.system.settings.fields.accountSecuritySessionTimeout", Type = SettingFieldType.Int, Min = 0, Subsection = "Session",
        Description = "Sign the user out after this many minutes with no requests. 0 (default) disables it; 15-30 is typical for internal or sensitive back-office systems.")]
    public int SessionTimeoutMinutes { get; set; }

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

    /// <summary>
    /// 允许作为 <c>returnUrl</c> 跳转目标的绝对地址来源（如 <c>https://app.example.com</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <strong>OAuth 回调会把访问令牌与刷新令牌放进目标地址的 fragment。</strong>
    /// 不设白名单时<b>只放行站内相对路径</b>，绝对地址一律拒绝 —— 这一项没配不等于放行一切。
    /// </para>
    /// <para>
    /// 留空时回退到 <c>App:FrontendUrl</c>（前端所在的源，也就是 OAuth 唯一要回到的地方），
    /// 所以绝大多数部署不需要单独配它。前端与 API 不同源、或者有多个前端入口时才需要列举。
    /// </para>
    /// <para>
    /// 比较的是 scheme + host + port 三者，路径不参与 —— 白名单管的是「回到谁那里」，
    /// 具体回到哪个页面由前端自己决定。
    /// </para>
    /// </remarks>
    public List<string> AllowedReturnOrigins { get; set; } = [];
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
