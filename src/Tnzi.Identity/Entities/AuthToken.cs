namespace Tnzi.Identity.Entities;

/// <summary>
/// 认证令牌实体（用于业务Token管理）。
/// <para>
/// 刻意<b>不使用软删</b>（<see cref="AuditedEntity{TKey}"/> 而非 <see cref="FullAuditedEntity{TKey}"/>）：
/// 令牌是短暂凭证（刷新令牌、2FA 临时令牌），过期/轮换/登出即应物理消失，无审计留痕需求
/// （<see cref="Value"/> 已 [AuditIgnore]）。软删会让"逻辑已删"的行仍物理占用唯一索引
/// <c>(UserId, LoginProvider, Name, SessionId)</c>，而 <c>SaveTokenAsync</c> 的存在性查询走软删
/// 过滤器看不到它 → 再次插入同一把 key 直接撞唯一约束（尤其 2FA 临时令牌 key 恒为
/// <c>(user, TwoFactor, TempToken, Guid.Empty)</c>，过期被后台清扫软删后每次登录必冲突）。
/// 改硬删后不存在幽灵行，查询侧与约束侧口径一致。
/// </para>
/// </summary>
public class AuthToken : AuditedEntity<Guid>
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
    /// 获取或设置 所属登录会话ID（把令牌绑定到具体的登录设备/会话）。
    /// <see cref="Guid.Empty"/> 表示不与任何会话绑定（如 2FA 临时令牌、历史遗留刷新令牌）。
    /// 刷新令牌绑定会话后，撤销会话即令该设备的刷新令牌失效；且同一用户可为多个会话
    /// 各持有一条刷新令牌（唯一索引含 SessionId），而非旧的"每用户一行、后登录覆盖"。
    /// </summary>
    public Guid SessionId { get; set; }

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

