namespace Tnzi.Identity.Entities;

/// <summary>
/// 认证令牌实体（用于业务Token管理）
/// </summary>
public class AuthToken : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置 用户
    /// </summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// 获取或设置 登录提供者（如：JWT, OAuth2等）
    /// </summary>
    public string LoginProvider { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 令牌名称（如：AccessToken, RefreshToken）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 令牌值。
    /// [AuditIgnore]：refresh token 以原值存储并按值等值校验（AuthService.RefreshTokenAsync），
    /// 属于可用凭证，绝不能随实体级审计把 old/new 值写进审计表。
    /// </summary>
    [AuditIgnore]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 获取或设置 是否已使用
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// 获取或设置 使用时间
    /// </summary>
    public DateTime? UsedAt { get; set; }
}

