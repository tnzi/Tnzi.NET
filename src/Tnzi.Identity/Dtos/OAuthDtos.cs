namespace Tnzi.Identity.Dtos;

/// <summary>
/// OAuth回调结果DTO
/// </summary>
public class OAuthCallbackResultDto
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 访问Token（如果成功登录）
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// RefreshToken（如果成功登录）
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Token过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 是否需要注册（如果用户不存在）
    /// </summary>
    public bool RequiresRegistration { get; set; }

    /// <summary>
    /// 用户信息（如果需要注册）
    /// </summary>
    public OAuthUserInfoDto? UserInfo { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// OAuth用户信息DTO
/// </summary>
public class OAuthUserInfoDto
{
    /// <summary>
    /// 提供者名称（Google, Microsoft等）
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 提供者用户ID
    /// </summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 显示名称
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 头像URL
    /// </summary>
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// 关联OAuth账户请求DTO
/// </summary>
public class LinkOAuthDto
{
    /// <summary>
    /// 用户ID（当前登录用户）
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// 提供者名称
    /// </summary>
    [Required]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 提供者用户ID
    /// </summary>
    [Required]
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string? DisplayName { get; set; }
}
