

namespace Tnzi.Identity.Dtos;

/// <summary>
/// 用户会话DTO
/// </summary>
public class UserSessionDto
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 设备信息
    /// </summary>
    public string? DeviceInfo { get; set; }

    /// <summary>
    /// IP地址
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// UserAgent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityTime { get; set; }

    /// <summary>
    /// 是否已撤销
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// 撤销时间
    /// </summary>
    public DateTime? RevokedAt { get; set; }
}
