namespace Tnzi.Identity.Dtos;

/// <summary>
/// 启用2FA请求DTO
/// </summary>
public class EnableTwoFactorDto
{
    /// <summary>
    /// 验证方式（SMS/Email）
    /// </summary>
    [Required]
    public TwoFactorType Type { get; set; }
}

/// <summary>
/// 携带单个 2FA 方式的请求 DTO（禁用某方式 / 设置首选方式）
/// </summary>
public class TwoFactorMethodRequestDto
{
    /// <summary>
    /// 验证方式（Sms/Email/Totp）
    /// </summary>
    [Required]
    public TwoFactorType Type { get; set; }
}

/// <summary>
/// 发送2FA验证码请求DTO
/// </summary>
public class SendTwoFactorCodeDto
{
    /// <summary>
    /// 临时Token（从登录接口获取）
    /// </summary>
    [Required]
    public string TempToken { get; set; } = null!;

    /// <summary>
    /// 验证方式（SMS/Email）
    /// </summary>
    [Required]
    public TwoFactorType Type { get; set; }
}

/// <summary>
/// 2FA挑战响应DTO
/// </summary>
public class TwoFactorChallengeDto
{
    /// <summary>
    /// 临时Token（用于后续验证）
    /// </summary>
    public string TempToken { get; set; } = string.Empty;

    /// <summary>
    /// 支持的验证方式
    /// </summary>
    public List<TwoFactorType> SupportedTypes { get; set; } = new();

    /// <summary>
    /// 验证码已发送
    /// </summary>
    public bool CodeSent { get; set; }

    /// <summary>
    /// 接收地址（手机号或邮箱，部分隐藏）
    /// </summary>
    public string? MaskedAddress { get; set; }

    /// <summary>
    /// 是否需要2FA（兼容字段）
    /// </summary>
    public bool RequiresTwoFactor { get; set; } = true;
}

/// <summary>
/// 用 passkey 完成两步验证：第一步，请求断言选项。
/// </summary>
/// <remarks>
/// 临时令牌指明是谁在登录，选项里的 <c>allowCredentials</c> 就是那个账号登记过的凭据，
/// 所以 YubiKey 这类不存可发现凭据的安全密钥也能用。
/// </remarks>
public class TwoFactorPasskeyBeginDto
{
    /// <summary>
    /// 登录挑战返回的临时令牌。
    /// </summary>
    [Required]
    public string TempToken { get; set; } = null!;
}

/// <summary>
/// 用 passkey 完成两步验证：第二步，把浏览器断言交回来换令牌。
/// </summary>
public class TwoFactorPasskeyCompleteDto : PasskeyCompleteDto
{
    /// <summary>
    /// 登录挑战返回的临时令牌（与第一步同一枚）。
    /// </summary>
    [Required]
    public string TempToken { get; set; } = null!;
}

/// <summary>
/// 验证2FA并登录请求DTO
/// </summary>
public class VerifyTwoFactorDto
{
    /// <summary>
    /// 临时Token
    /// </summary>
    [Required]
    public string TempToken { get; set; } = null!;

    /// <summary>
    /// 验证码
    /// </summary>
    [Required]
    public string Code { get; set; } = null!;

    /// <summary>
    /// 验证方式
    /// </summary>
    [Required]
    public TwoFactorType Type { get; set; }
}

/// <summary>
/// TOTP 设置信息 DTO
/// </summary>
public class TotpSetupDto
{
    /// <summary>
    /// Base32 编码的密钥（用于手动输入）
    /// </summary>
    public string SharedKey { get; set; } = null!;

    /// <summary>
    /// otpauth:// URI（用于生成二维码）
    /// </summary>
    public string AuthenticatorUri { get; set; } = null!;
}

/// <summary>
/// 启用 TOTP 请求 DTO
/// </summary>
public class EnableTotpDto
{
    /// <summary>
    /// 用户输入的验证码（6位数字）
    /// </summary>
    [Required]
    public string VerificationCode { get; set; } = null!;
}

#region 验证码登录 DTOs

/// <summary>
/// 发送验证码登录验证码请求DTO
/// </summary>
public class SendCodeLoginCodeDto : ICaptchaSubmission
{
    /// <summary>
    /// 图形验证码ID（当启用登录图形验证码时必填，发短信/邮箱验证码前先校验）
    /// </summary>
    public string? CaptchaId { get; set; }

    /// <summary>
    /// 图形验证码（当启用登录图形验证码时必填）
    /// </summary>
    public string? CaptchaCode { get; set; }

    /// <summary>
    /// 人机验证令牌（统一控件产出的不透明字符串，任意提供商）。启用登录验证码时必填，发送短信 / 邮件之前校验。
    /// 与 <see cref="CaptchaId"/> + <see cref="CaptchaCode"/> 二选一，两者都给时以本字段为准。
    /// </summary>
    public string? CaptchaToken { get; set; }

    /// <summary>
    /// 邮箱地址（邮箱登录时必填）
    /// </summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>
    /// 手机号（短信登录时必填）
    /// </summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 验证方式（Email/Sms）
    /// </summary>
    [Required]
    public TwoFactorType Type { get; set; }
}

/// <summary>
/// 验证码登录请求DTO
/// </summary>
public class CodeLoginDto
{
    /// <summary>
    /// 邮箱地址（邮箱登录时必填）
    /// </summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>
    /// 手机号（短信登录时必填）
    /// </summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 验证码
    /// </summary>
    [Required]
    public string Code { get; set; } = null!;

    /// <summary>
    /// 验证方式（Email/Sms）
    /// </summary>
    [Required]
    public TwoFactorType Type { get; set; }
}

/// <summary>
/// 验证码登录结果DTO
/// </summary>
public class CodeLoginResultDto : IRefreshTokenCarrier
{
    /// <summary>
    /// 访问令牌
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// 刷新令牌
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 访问令牌过期时间（秒）
    /// </summary>
    public int ExpiresIn { get; set; }

    /// <summary>
    /// 刷新令牌过期时间（秒，如果未启用RefreshToken则为null）
    /// </summary>
    public int? RefreshTokenExpiresIn { get; set; }

    /// <summary>
    /// 是否需要设置密码（首次验证码登录自动注册的用户需要设置密码）
    /// </summary>
    public bool RequirePasswordSetup { get; set; }

    /// <summary>
    /// 设置密码令牌（当 RequirePasswordSetup 为 true 时返回）
    /// </summary>
    public string? SetPasswordToken { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 是否为新注册用户
    /// </summary>
    public bool IsNewUser { get; set; }

    /// <inheritdoc />
    string? IRefreshTokenCarrier.ReadRefreshToken() => RefreshToken;

    /// <inheritdoc />
    int? IRefreshTokenCarrier.ReadRefreshTokenLifetimeSeconds() => RefreshTokenExpiresIn;

    /// <inheritdoc />
    void IRefreshTokenCarrier.ClearRefreshToken() => RefreshToken = null;
}

#endregion

#region 验证码找回密码 DTOs

/// <summary>
/// 发送密码找回验证码请求DTO
/// </summary>
public class SendPasswordRecoveryCodeDto : ICaptchaSubmission
{
    /// <summary>
    /// 邮箱地址（邮箱找回时必填）
    /// </summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>
    /// 手机号（短信找回时必填）
    /// </summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 验证方式（Email/Sms）
    /// </summary>
    [Required]
    public TwoFactorType Type { get; set; }

    /// <summary>
    /// 人机验证令牌。启用 <c>Identity:Captcha:EnableCaptchaOnPasswordRecovery</c> 时必填：
    /// 发码之前校验，这条路径每次调用都真的产生短信 / 邮件费用。
    /// </summary>
    public string? CaptchaToken { get; set; }

    /// <summary>图形验证码 ID（历史形式，与 <see cref="CaptchaToken"/> 二选一）。</summary>
    public string? CaptchaId { get; set; }

    /// <summary>图形验证码答案（历史形式）。</summary>
    public string? CaptchaCode { get; set; }
}

/// <summary>
/// 验证码重置密码请求DTO
/// </summary>
public class ResetPasswordByCodeDto
{
    /// <summary>
    /// 邮箱地址（邮箱找回时必填）
    /// </summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>
    /// 手机号（短信找回时必填）
    /// </summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 验证码
    /// </summary>
    [Required]
    public string Code { get; set; } = null!;

    /// <summary>
    /// 新密码
    /// </summary>
    [Required]
    public string NewPassword { get; set; } = null!;

    /// <summary>
    /// 验证方式（Email/Sms）
    /// </summary>
    [Required]
    public TwoFactorType Type { get; set; }
}

#endregion
