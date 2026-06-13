

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
    /// 用户名 — 全局会话列表（GetSessionsAsync）批量关联用户表填充；
    /// 按用户查询的旧路径（GetUserSessionsAsync）不填充（调用方已知用户）。
    /// </summary>
    public string? UserName { get; set; }

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

/// <summary>
/// 会话分页查询 DTO — userId 可选；不传时分页列出全部会话（按最后活动时间倒序）
/// </summary>
public class SessionQueryDto : PagedQueryDto
{
    /// <summary>
    /// 用户ID（可选）— 传入时仅返回该用户的会话
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 是否包含已撤销的会话（默认 false，仅活跃会话）
    /// </summary>
    public bool IncludeRevoked { get; set; }
}

/// <summary>
/// 会话统计DTO
/// </summary>
public class SessionStatisticsDto
{
    /// <summary>
    /// 活跃会话数（未撤销的会话）
    /// </summary>
    public int ActiveSessionCount { get; set; }

    /// <summary>
    /// 在线用户数（有活跃会话的不同用户数）
    /// </summary>
    public int OnlineUserCount { get; set; }

    /// <summary>
    /// 设备分布 Top 5
    /// </summary>
    public IEnumerable<DeviceStatItem> TopDevices { get; set; } = new List<DeviceStatItem>();
}

/// <summary>
/// 设备统计项
/// </summary>
public class DeviceStatItem
{
    /// <summary>
    /// 设备信息
    /// </summary>
    public string DeviceInfo { get; set; } = string.Empty;

    /// <summary>
    /// 该设备的会话数量
    /// </summary>
    public int Count { get; set; }
}

/// <summary>
/// 活跃用户摘要 DTO — 用于会话管理页面的"活跃用户"下拉，避免暴露完整 user list
/// </summary>
public class ActiveUserSummaryDto
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 该用户的活跃会话数量（未撤销）
    /// </summary>
    public int SessionCount { get; set; }

    /// <summary>
    /// 该用户最后一次活动时间（取所有活跃会话的最大值）
    /// </summary>
    public DateTime LastActivityTime { get; set; }
}
