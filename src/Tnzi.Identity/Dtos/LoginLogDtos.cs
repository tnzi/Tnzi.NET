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
/// Daily login trend data point
/// </summary>
public class LoginTrendItem
{
    /// <summary>
    /// Date (UTC, date only)
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Total login attempts on this day
    /// </summary>
    public int TotalLogins { get; set; }

    /// <summary>
    /// Successful logins on this day
    /// </summary>
    public int SuccessfulLogins { get; set; }

    /// <summary>
    /// Failed logins on this day
    /// </summary>
    public int FailedLogins { get; set; }

    /// <summary>
    /// Number of unique users who attempted login on this day
    /// </summary>
    public int UniqueUsers { get; set; }
}

/// <summary>
/// User export DTO (CSV export format)
/// </summary>
public class UserExportDto
{
    public Guid Id { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? NickName { get; set; }
    public bool IsActive { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// User import DTO (CSV import format)
/// </summary>
public class UserImportDto
{
    /// <summary>
    /// User name (required)
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string UserName { get; set; } = null!;

    /// <summary>
    /// Email (required)
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = null!;

    /// <summary>
    /// Phone number (optional)
    /// </summary>
    [MaxLength(32)]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Password (required for new users)
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string Password { get; set; } = null!;

    /// <summary>
    /// Nick name (optional)
    /// </summary>
    [MaxLength(128)]
    public string? NickName { get; set; }
}

/// <summary>
/// User import result
/// </summary>
public class UserImportResult
{
    /// <summary>
    /// Total rows processed
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// Successfully imported count
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Failed count
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Skipped (already exists) count
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Error details per row (row number -> error message)
    /// </summary>
    public Dictionary<int, string> Errors { get; set; } = new();
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
    public List<LoginLogDto> RecentLogins { get; set; } = new();
}
