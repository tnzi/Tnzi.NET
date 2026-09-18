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

    /// <summary>
    /// 这次回调完成的是<b>账号绑定</b>而不是登录时，被绑定的提供商（小写）。绑定回调不签发任何令牌：
    /// <see cref="AccessToken"/> / <see cref="RefreshToken"/> 为空，回调页只跳回 <c>returnUrl</c>。
    /// </summary>
    public string? LinkedProvider { get; set; }
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
/// 个人中心「绑定第三方账号」签发的一次性绑定令牌。
/// </summary>
/// <remarks>
/// OAuth 的发起与回调都是匿名的整页跳转，不带 bearer；这枚令牌把「当前用户是谁」带到回调：
/// 前端先 <c>POST users/profile/linked-accounts/{provider}/link-token</c>，再把 <see cref="Token"/> 作为
/// <c>linkToken</c> 查询参数跳到 <c>GET auth/oauth/{provider}/login</c>。回调消费它并把外部登录挂到<b>签发它的账号</b>上，
/// 不签发令牌、不新建账号。
/// </remarks>
public class OAuthLinkTokenDto
{
    /// <summary>令牌明文（只在签发这次返回；库里只存哈希）</summary>
    public string Token { get; set; } = null!;

    /// <summary>它只能用来绑定的提供商（小写）</summary>
    public string Provider { get; set; } = null!;

    /// <summary>过期时间（UTC）</summary>
    public DateTime ExpiresAt { get; set; }
}
