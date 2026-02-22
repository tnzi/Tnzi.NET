namespace Tnzi.Identity.Dtos;

/// <summary>
/// 登录日志查询DTO
/// </summary>
public class LoginLogQueryDto : PagedQueryDto
{
    protected override int DefaultPageSize => 20;

    /// <summary>
    /// 用户ID（可选）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// IP地址（可选）
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// 开始时间（可选）
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// 结束时间（可选）
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// 登录状态（可选）
    /// </summary>
    public LoginStatus? Status { get; set; }

    /// <summary>
    /// 是否成功（可选，用于兼容Controller参数）
    /// </summary>
    public bool? IsSuccess { get; set; }
}

/// <summary>
/// 登录日志DTO
/// </summary>
public class LoginLogDto
{
    /// <summary>
    /// 日志ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// IP地址
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// 浏览器信息
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 失败原因
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 登录时间
    /// </summary>
    public DateTime LoginTime { get; set; }
}

/// <summary>
/// 登录统计DTO
/// </summary>
public class LoginStatisticsDto
{
    /// <summary>
    /// 总登录次数
    /// </summary>
    public int TotalLogins { get; set; }

    /// <summary>
    /// 成功登录次数
    /// </summary>
    public int SuccessfulLogins { get; set; }

    /// <summary>
    /// 失败登录次数
    /// </summary>
    public int FailedLogins { get; set; }

    /// <summary>
    /// 唯一IP数量
    /// </summary>
    public int UniqueIpCount { get; set; }

    /// <summary>
    /// 最近登录时间
    /// </summary>
    public DateTime? LastLoginTime { get; set; }

    /// <summary>
    /// 最近登录IP
    /// </summary>
    public string? LastLoginIp { get; set; }
}

/// <summary>
/// 用户登录统计DTO
/// </summary>
public class UserLoginStatisticsDto
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 总登录次数
    /// </summary>
    public int TotalLogins { get; set; }

    /// <summary>
    /// 成功登录次数
    /// </summary>
    public int SuccessfulLogins { get; set; }

    /// <summary>
    /// 失败登录次数
    /// </summary>
    public int FailedLogins { get; set; }

    /// <summary>
    /// 最后登录时间
    /// </summary>
    public DateTime? LastLoginTime { get; set; }

    /// <summary>
    /// 最后登录IP
    /// </summary>
    public string? LastLoginIp { get; set; }
}

/// <summary>
/// 异常登录检测结果DTO
/// </summary>
public class AbnormalLoginDto
{
    /// <summary>
    /// 是否异常
    /// </summary>
    public bool IsAbnormal { get; set; }

    /// <summary>
    /// 异常原因
    /// </summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>
    /// 最近登录记录
    /// </summary>
    public List<LoginLog> RecentLogins { get; set; } = new();
}
