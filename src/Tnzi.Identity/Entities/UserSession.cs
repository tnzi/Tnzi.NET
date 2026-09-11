namespace Tnzi.Identity.Entities;

/// <summary>
/// 用户会话实体
/// </summary>
public class UserSession : EntityBase<Guid>, IHasCreationTime
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
    /// 获取或设置 设备信息
    /// </summary>
    public string? DeviceInfo { get; set; }

    /// <summary>
    /// 获取或设置 IP地址
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// 获取或设置 UserAgent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// 获取或设置 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 获取或设置 最后活动时间
    /// </summary>
    public DateTime LastActivityTime { get; set; }

    /// <summary>
    /// 获取或设置 会话硬过期时间（绑定到刷新令牌生命周期）。
    /// 到期后会话被判定为失效：不再计入并发数、令牌校验/刷新一律拒绝。
    /// null 表示不过期（历史遗留会话，向后兼容）。
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 获取或设置 会话绝对过期时间：从建立那一刻起算的生命周期上限，<b>续期不会推动它</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <see cref="ExpiresAt"/> 是滑动的（每次刷新令牌都会往后推），单独存在时
    /// 一条会话可以被无限续下去 —— 只要每 <c>RefreshTokenExpirationDays</c> 天刷一次，
    /// 它就永远不会到期。对拿到令牌的攻击者来说，这等于一次窃取换来永久访问。
    /// 本字段是那条滑动窗口的天花板（OWASP ASVS 7.3.2「absolute maximum session lifetime」）。
    /// </para>
    /// <para>
    /// <c>null</c> 表示无绝对上限：既是历史遗留会话的语义，也是
    /// <c>Identity:Session:AbsoluteLifetimeHours = 0</c> 时的显式选择。
    /// </para>
    /// </remarks>
    public DateTime? AbsoluteExpiresAt { get; set; }

    /// <summary>
    /// 获取或设置 是否已撤销
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// 获取或设置 撤销时间
    /// </summary>
    public DateTime? RevokedAt { get; set; }
}
