namespace Tnzi.Identity.Dtos;

/// <summary>
/// 刷新Token请求DTO
/// </summary>
public class RefreshTokenDto
{
    /// <summary>
    /// 刷新令牌
    /// </summary>
    [Required]
    public string RefreshToken { get; set; } = null!;
}

public class LoginDto : ICaptchaSubmission
{
    /// <summary>
    /// 用户名/邮箱/手机号（根据配置支持不同登录方式）
    /// </summary>
    [Required]
    public string UserName { get; set; } = null!;

    /// <summary>
    /// 密码
    /// </summary>
    [Required]
    public string Password { get; set; } = null!;

    /// <summary>
    /// 验证码ID（当启用登录验证码时必填）
    /// </summary>
    public string? CaptchaId { get; set; }

    /// <summary>
    /// 验证码（当启用登录验证码时必填）
    /// </summary>
    public string? CaptchaCode { get; set; }

    /// <summary>
    /// 人机验证令牌（统一控件产出的不透明字符串，任意提供商）。自适应登录验证码被要求时必填。
    /// 与 <see cref="CaptchaId"/> + <see cref="CaptchaCode"/> 二选一，两者都给时以本字段为准。
    /// </summary>
    public string? CaptchaToken { get; set; }
}

public class RegisterDto : ICaptchaSubmission
{
    /// <summary>
    /// 用户名（可选，如果不提供且配置了 DefaultUserNameFromEmail，将使用邮箱作为用户名）
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 密码
    /// </summary>
    [Required]
    public string Password { get; set; } = null!;

    /// <summary>
    /// 邮箱（必填，用于注册和作为默认用户名）
    /// </summary>
    [Required]
    public string Email { get; set; } = null!;

    /// <summary>
    /// 验证码ID（当启用注册验证码时必填）
    /// </summary>
    public string? CaptchaId { get; set; }

    /// <summary>
    /// 验证码（当启用注册验证码时必填）
    /// </summary>
    public string? CaptchaCode { get; set; }

    /// <summary>
    /// 人机验证令牌（统一控件产出的不透明字符串，任意提供商）。启用注册验证码时必填。
    /// 与 <see cref="CaptchaId"/> + <see cref="CaptchaCode"/> 二选一，两者都给时以本字段为准。
    /// </summary>
    public string? CaptchaToken { get; set; }

    /// <summary>
    /// 名字（可选）
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// 姓氏（可选）
    /// </summary>
    public string? LastName { get; set; }
}

/// <summary>
/// 忘记密码请求DTO
/// </summary>
public class ForgotPasswordDto : ICaptchaSubmission
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    /// <summary>
    /// 人机验证令牌。启用 <c>Identity:Captcha:EnableCaptchaOnPasswordRecovery</c> 时必填：
    /// 这个端点每次调用都真的发一封邮件，与发码端点同属花钱的匿名入口。
    /// </summary>
    public string? CaptchaToken { get; set; }

    /// <summary>图形验证码 ID（历史形式，与 <see cref="CaptchaToken"/> 二选一）。</summary>
    public string? CaptchaId { get; set; }

    /// <summary>图形验证码答案（历史形式）。</summary>
    public string? CaptchaCode { get; set; }
}

/// <summary>
/// 重置密码DTO
/// </summary>
public class ResetPasswordDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    public string Token { get; set; } = null!;

    [Required]
    public string NewPassword { get; set; } = null!;
}

/// <summary>
/// 带人机验证的请求。两种提交形式：<see cref="ICaptchaProtectedRequest.CaptchaToken"/>（任意提供商的不透明令牌，
/// 前端统一控件产出）或 <see cref="CaptchaId"/> + <see cref="CaptchaCode"/>（内置图形验证码的历史形式，
/// 仍然接受，服务端拼成 <c>id:code</c> 令牌）。两者都给时以 <c>CaptchaToken</c> 为准。
/// </summary>
public interface ICaptchaSubmission : ICaptchaProtectedRequest
{
    /// <summary>图形验证码 ID（历史形式）。</summary>
    string? CaptchaId { get; }

    /// <summary>图形验证码答案（历史形式）。</summary>
    string? CaptchaCode { get; }
}

/// <summary>
/// 人机验证挑战 DTO。随 <see cref="ErrorCodes.IDENTITY_CAPTCHA_REQUIRED"/> 一并下发，也是 <c>GET /auth/captcha/{purpose}/json</c> 的响应。
/// </summary>
/// <remarks>
/// <see cref="Provider"/> 告诉前端该渲染什么：<c>image</c> 时带 <see cref="CaptchaId"/> + <see cref="ImageBase64"/>；
/// 其它提供商只有 <see cref="Provider"/>，前端按 <c>/auth/config</c> 里的 <c>captcha</c> 客户端配置渲染对应控件。
/// </remarks>
public class CaptchaDto
{
    /// <summary>
    /// 生效的提供商名（<c>image</c> / <c>turnstile</c> / <c>altcha</c> …）。
    /// </summary>
    public string Provider { get; set; } = IdentityConstants.ImageCaptchaProvider;

    /// <summary>
    /// 图形验证码 ID（提交时需要返回）。只有 <c>image</c> 提供商有值。
    /// </summary>
    public string? CaptchaId { get; set; }

    /// <summary>
    /// 图形验证码 PNG 的 Base64（不带 data-uri 前缀）。只有 <c>image</c> 提供商有值。
    /// </summary>
    public string? ImageBase64 { get; set; }

    /// <summary>
    /// 图形验证码过期时间（秒）。只有 <c>image</c> 提供商有值。
    /// </summary>
    public int? ExpirationSeconds { get; set; }
}

/// <summary>
/// 发送快速注册验证码请求DTO
/// </summary>
public class SendQuickRegisterCodeDto : ICaptchaSubmission
{
    /// <summary>
    /// 邮箱地址（邮箱快速注册时必填）
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 手机号（SMS快速注册时必填）
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 验证码ID（当启用注册图形验证码时必填，发送短信/邮箱验证码前先校验）
    /// </summary>
    public string? CaptchaId { get; set; }

    /// <summary>
    /// 验证码（当启用注册图形验证码时必填）
    /// </summary>
    public string? CaptchaCode { get; set; }

    /// <summary>
    /// 人机验证令牌（统一控件产出的不透明字符串，任意提供商）。启用注册验证码时必填，发送短信 / 邮件之前校验。
    /// 与 <see cref="CaptchaId"/> + <see cref="CaptchaCode"/> 二选一，两者都给时以本字段为准。
    /// </summary>
    public string? CaptchaToken { get; set; }
}

/// <summary>
/// 快速注册DTO（无需密码，仅验证码）
/// </summary>
public class QuickRegisterDto
{
    /// <summary>
    /// 邮箱地址（邮箱快速注册时必填）
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 手机号（SMS快速注册时必填）
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 验证码
    /// </summary>
    [Required]
    public string Code { get; set; } = null!;

    /// <summary>
    /// 用户名（可选，不提供则使用邮箱/手机号）
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 名字（可选）
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// 姓氏（可选）
    /// </summary>
    public string? LastName { get; set; }
}

/// <summary>
/// 快速注册结果DTO
/// </summary>
public class QuickRegisterResultDto
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 是否需要设置密码
    /// </summary>
    public bool RequirePasswordSetup { get; set; } = true;

    /// <summary>
    /// 临时Token（用于设置密码）
    /// </summary>
    public string? SetPasswordToken { get; set; }
}

/// <summary>
/// 设置密码DTO（快速注册后设置密码）
/// </summary>
public class SetPasswordDto
{
    /// <summary>
    /// 用户ID
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// 设置密码令牌
    /// </summary>
    [Required]
    public string Token { get; set; } = null!;

    /// <summary>
    /// 新密码
    /// </summary>
    [Required]
    public string Password { get; set; } = null!;
}

/// <summary>
/// 重发邮箱确认邮件请求DTO
/// </summary>
public class ResendEmailConfirmationDto : ICaptchaSubmission
{
    /// <summary>
    /// 用户ID（与 Email 二选一）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 邮箱地址（与 UserId 二选一）
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 人机验证令牌。启用 <c>Identity:Captcha:EnableCaptchaOnRegister</c> 时必填：
    /// 重发确认邮件与注册发码一样，每次调用都真的发一封邮件。
    /// </summary>
    public string? CaptchaToken { get; set; }

    /// <summary>图形验证码 ID（历史形式，与 <see cref="CaptchaToken"/> 二选一）。</summary>
    public string? CaptchaId { get; set; }

    /// <summary>图形验证码答案（历史形式）。</summary>
    public string? CaptchaCode { get; set; }
}


