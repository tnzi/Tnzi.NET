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
    /// 每日配额使用率（0-1）
    /// </summary>
    public decimal DailyUsagePercentage => DailyTokenLimit > 0 ? (decimal)CurrentDailyUsage / DailyTokenLimit : 0;

    /// <summary>
    /// 每月配额使用率（0-1）
    /// </summary>
    public decimal MonthlyUsagePercentage => MonthlyTokenLimit > 0 ? (decimal)CurrentMonthlyUsage / MonthlyTokenLimit : 0;

    /// <summary>
    /// 最后重置日期
    /// </summary>
    public DateTime LastResetDate { get; set; }

    /// <summary>
    /// 是否启用配额限制
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 预警阈值（0-1）
    /// </summary>
    public decimal WarningThreshold { get; set; }

    /// <summary>
    /// 严重预警阈值（0-1）
    /// </summary>
    public decimal CriticalThreshold { get; set; }

    /// <summary>
    /// 当前预警级别
    /// </summary>
    public QuotaWarningLevel WarningLevel
    {
        get
        {
            var dailyPct = DailyTokenLimit > 0 ? (decimal)CurrentDailyUsage / DailyTokenLimit : 0;
            var monthlyPct = MonthlyTokenLimit > 0 ? (decimal)CurrentMonthlyUsage / MonthlyTokenLimit : 0;
            var maxPct = Math.Max(dailyPct, monthlyPct);
            return maxPct >= CriticalThreshold ? QuotaWarningLevel.Critical
                : maxPct >= WarningThreshold ? QuotaWarningLevel.Warning
                : QuotaWarningLevel.None;
        }
    }

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

    /// <summary>
    /// 预警阈值（0-1，达到该比例时触发 Warning，默认 0.8）
    /// </summary>
    [Range(0.0, 1.0)]
    public decimal? WarningThreshold { get; set; }

    /// <summary>
    /// 严重预警阈值（0-1，达到该比例时触发 Critical，默认 0.95）
    /// </summary>
    [Range(0.0, 1.0)]
    public decimal? CriticalThreshold { get; set; }
}

/// <summary>
/// 用户配额分页查询 DTO
/// </summary>
public class UserQuotaQueryDto : PagedQueryDto
{
    /// <summary>按用户 ID 过滤（可选）</summary>
    public Guid? UserId { get; set; }

    /// <summary>仅返回启用的配额（可选）</summary>
    public bool? IsEnabled { get; set; }
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
