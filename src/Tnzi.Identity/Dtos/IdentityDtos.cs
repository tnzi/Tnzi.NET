namespace Tnzi.Identity.Dtos;

/// <summary>
/// 刷新Token请求DTO
/// </summary>
public class RefreshTokenDto
{
    /// <summary>
    /// 刷新令牌
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;
}

public class LoginDto
{
    /// <summary>
    /// 用户名/邮箱/手机号（根据配置支持不同登录方式）
    /// </summary>
    [Required]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 密码
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 验证码ID（当启用登录验证码时必填）
    /// </summary>
    public string? CaptchaId { get; set; }

    /// <summary>
    /// 验证码（当启用登录验证码时必填）
    /// </summary>
    public string? CaptchaCode { get; set; }
}

public class RegisterDto
{
    /// <summary>
    /// 用户名（可选，如果不提供且配置了 DefaultUserNameFromEmail，将使用邮箱作为用户名）
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 密码
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱（必填，用于注册和作为默认用户名）
    /// </summary>
    [Required]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 验证码ID（当启用注册验证码时必填）
    /// </summary>
    public string? CaptchaId { get; set; }

    /// <summary>
    /// 验证码（当启用注册验证码时必填）
    /// </summary>
    public string? CaptchaCode { get; set; }

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
public class ForgotPasswordDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// 重置密码DTO
/// </summary>
public class ResetPasswordDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// 验证码DTO
/// </summary>
public class CaptchaDto
{
    /// <summary>
    /// 验证码ID（提交时需要返回）
    /// </summary>
    public string CaptchaId { get; set; } = string.Empty;

    /// <summary>
    /// 验证码图片Base64编码
    /// </summary>
    public string ImageBase64 { get; set; } = string.Empty;

    /// <summary>
    /// 验证码过期时间（秒）
    /// </summary>
    public int ExpirationSeconds { get; set; }
}

/// <summary>
/// 发送快速注册验证码请求DTO
/// </summary>
public class SendQuickRegisterCodeDto
{
    /// <summary>
    /// 邮箱地址（邮箱快速注册时必填）
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 手机号（SMS快速注册时必填）
    /// </summary>
    public string? PhoneNumber { get; set; }
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
    public string Code { get; set; } = string.Empty;

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
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 新密码
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// 重发邮箱确认邮件请求DTO
/// </summary>
public class ResendEmailConfirmationDto
{
    /// <summary>
    /// 用户ID（与 Email 二选一）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 邮箱地址（与 UserId 二选一）
    /// </summary>
    public string? Email { get; set; }
}


