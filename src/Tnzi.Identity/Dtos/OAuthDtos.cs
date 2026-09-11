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

    /// <summary>
    /// 业务错误码，供前端按码分支。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>没有它，OAuth 登录就不可能支持两步验证。</strong>
    /// 共享签发出口用<b>失败信封</b>承载挑战（403 + <c>2FA_REQUIRED</c> /
    /// <c>IDENTITY_PENDING_ACTIONS_REQUIRED</c> + 临时令牌），而回调页此前把任何失败
    /// 一律渲染成「OAuth callback failed」—— 于是开着 2FA 的账号在第三方登录这条路上
    /// 要么被绕过、要么走不通，没有第三种可能。
    /// </remarks>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 失败信封携带的细节，原样透传给前端（2FA 挑战的临时令牌与可选方式、待办义务清单等）。
    /// </summary>
    /// <remarks>
    /// 刻意保持 <c>object?</c> 与 <c>Result.ErrorDetails</c> 同型：这里的职责只是<b>不丢</b>，
    /// 每加一种挑战就在这里补一个强类型字段，等于给自己排了一条注定会漏的队。
    /// </remarks>
    public object? ErrorDetails { get; set; }
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
    public string Provider { get; set; } = null!;

    /// <summary>
    /// 提供者用户ID
    /// </summary>
    [Required]
    public string ProviderKey { get; set; } = null!;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string? DisplayName { get; set; }
}
