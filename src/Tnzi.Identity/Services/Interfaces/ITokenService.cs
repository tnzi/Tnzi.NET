

namespace Tnzi.Identity.Services;

/// <summary>
/// JWT令牌服务接口
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// 生成JWT令牌
    /// </summary>
    string GenerateToken(User user, IList<string> roles);

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
    TokenResult GenerateTokenResult(User user, IList<string> roles);
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
