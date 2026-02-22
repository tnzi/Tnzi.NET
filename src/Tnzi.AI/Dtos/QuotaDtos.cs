namespace Tnzi.AI.Dtos;

/// <summary>
/// 用户配额 DTO
/// </summary>
public class UserQuotaDto
{
    /// <summary>
    /// 配额 ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 用户 ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 每日 Token 限额
    /// </summary>
    public long DailyTokenLimit { get; set; }

    /// <summary>
    /// 每月 Token 限额
    /// </summary>
    public long MonthlyTokenLimit { get; set; }

    /// <summary>
    /// 当前每日已使用 Token 数
    /// </summary>
    public long CurrentDailyUsage { get; set; }

    /// <summary>
    /// 当前每月已使用 Token 数
    /// </summary>
    public long CurrentMonthlyUsage { get; set; }

    /// <summary>
    /// 剩余每日配额
    /// </summary>
    public long RemainingDailyQuota => Math.Max(0, DailyTokenLimit - CurrentDailyUsage);

    /// <summary>
    /// 剩余每月配额
    /// </summary>
    public long RemainingMonthlyQuota => Math.Max(0, MonthlyTokenLimit - CurrentMonthlyUsage);

    /// <summary>
    /// 每日配额使用率（百分比）
    /// </summary>
    public double DailyUsagePercentage => DailyTokenLimit > 0 ? (double)CurrentDailyUsage / DailyTokenLimit * 100 : 0;

    /// <summary>
    /// 每月配额使用率（百分比）
    /// </summary>
    public double MonthlyUsagePercentage => MonthlyTokenLimit > 0 ? (double)CurrentMonthlyUsage / MonthlyTokenLimit * 100 : 0;

    /// <summary>
    /// 最后重置日期
    /// </summary>
    public DateTime LastResetDate { get; set; }

    /// <summary>
    /// 是否启用配额限制
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 设置配额请求 DTO
/// </summary>
public class SetQuotaDto
{
    /// <summary>
    /// 用户 ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 每日 Token 限额
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Daily token limit must be greater than 0")]
    public long DailyTokenLimit { get; set; }

    /// <summary>
    /// 每月 Token 限额
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Monthly token limit must be greater than 0")]
    public long MonthlyTokenLimit { get; set; }
}

/// <summary>
/// 重置配额请求 DTO
/// </summary>
public class ResetQuotaDto
{
    /// <summary>
    /// 用户 ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 是否重置每日配额
    /// </summary>
    public bool ResetDaily { get; set; }

    /// <summary>
    /// 是否重置每月配额
    /// </summary>
    public bool ResetMonthly { get; set; }
}
