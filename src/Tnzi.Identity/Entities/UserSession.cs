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
    /// 获取或设置 是否已撤销
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// 获取或设置 撤销时间
    /// </summary>
    public DateTime? RevokedAt { get; set; }
}
