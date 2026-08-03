

namespace Tnzi.Identity.Services;

/// <summary>
/// JWT令牌服务接口
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// 生成JWT令牌
    /// </summary>
    /// <param name="user">令牌主体用户</param>
    /// <param name="roles">用户角色名列表</param>
    /// <param name="extraClaims">可选的自定义 claim（如桥接登录的 ai_roles/user_type 等）；与框架保留类型（subject/name/jti/role/tenant_id/session_id）冲突的项会被忽略</param>
    /// <param name="sessionId">可选的登录会话ID；非空且非 <see cref="Guid.Empty"/> 时写入受保护的 <c>session_id</c> claim，供服务端每请求校验会话有效性（撤销即踢下线）</param>
    string GenerateToken(User user, IList<string> roles, IEnumerable<Claim>? extraClaims = null, Guid? sessionId = null);

    /// <summary>
    /// 生成刷新令牌
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// 从过期的令牌获取Principal
    /// </summary>
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);

    /// <summary>
    /// 生成令牌结果
    /// </summary>
    /// <param name="user">令牌主体用户</param>
    /// <param name="roles">用户角色名列表</param>
    /// <param name="extraClaims">可选的自定义 claim；与框架保留类型冲突的项会被忽略</param>
    /// <param name="sessionId">可选的登录会话ID；写入受保护的 <c>session_id</c> claim</param>
    TokenResult GenerateTokenResult(User user, IList<string> roles, IEnumerable<Claim>? extraClaims = null, Guid? sessionId = null);
}

/// <summary>
/// 令牌结果
/// </summary>
public class TokenResult
{
    /// <summary>
    /// 访问令牌
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// 刷新令牌
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 过期秒数
    /// </summary>
    public int ExpiresIn { get; set; }

    /// <summary>
    /// 刷新令牌过期秒数（如果未启用RefreshToken则为null）
    /// </summary>
    public int? RefreshTokenExpiresIn { get; set; }

    /// <summary>
    /// 是否需要邮箱确认（注册成功但需要确认邮箱时为 true）
    /// 当此值为 true 时，AccessToken 和 RefreshToken 为空
    /// </summary>
    public bool RequireEmailConfirmation { get; set; }

    /// <summary>
    /// 用户邮箱（用于前端显示提示信息，仅当 RequireEmailConfirmation=true 时有值）
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 用户ID（用于重发确认邮件等操作，仅当 RequireEmailConfirmation=true 时有值）
    /// </summary>
    public Guid? UserId { get; set; }
}
