namespace Tnzi.AI.Dtos;

/// <summary>
/// 配额预警级别
/// </summary>
public enum QuotaWarningLevel
{
    /// <summary>配额充足</summary>
    None = 0,
    /// <summary>配额即将用尽（达到 WarningThreshold）</summary>
    Warning = 1,
    /// <summary>配额紧急（达到 CriticalThreshold）</summary>
    Critical = 2
}

/// <summary>
/// 配额检查结果
/// </summary>
public class QuotaCheckResult
{
    /// <summary>
    /// 是否允许继续（配额足够）
    /// </summary>
    public bool IsAllowed { get; set; }

    /// <summary>
    /// 拒绝原因
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 剩余每日配额
    /// </summary>
    public long RemainingDailyQuota { get; set; }

    /// <summary>
    /// 剩余每月配额
    /// </summary>
    public long RemainingMonthlyQuota { get; set; }

    /// <summary>
    /// 每日使用率（0-1）
    /// </summary>
    public decimal DailyUsagePercentage { get; set; }

    /// <summary>
    /// 每月使用率（0-1）
    /// </summary>
    public decimal MonthlyUsagePercentage { get; set; }

    /// <summary>
    /// 预警级别（基于使用率与阈值比较）
    /// </summary>
    public QuotaWarningLevel WarningLevel { get; set; }

    /// <summary>
    /// 创建允许结果
    /// </summary>
    public static QuotaCheckResult Allow(long remainingDaily, long remainingMonthly, decimal dailyUsagePct = 0, decimal monthlyUsagePct = 0, QuotaWarningLevel warningLevel = QuotaWarningLevel.None)
    {
        return new QuotaCheckResult
        {
            IsAllowed = true,
            RemainingDailyQuota = remainingDaily,
            RemainingMonthlyQuota = remainingMonthly,
            DailyUsagePercentage = dailyUsagePct,
            MonthlyUsagePercentage = monthlyUsagePct,
            WarningLevel = warningLevel
        };
    }

    /// <summary>
    /// 创建拒绝结果
    /// </summary>
    public static QuotaCheckResult Deny(string reason)
    {
        return new QuotaCheckResult
        {
            IsAllowed = false,
            Reason = reason,
            RemainingDailyQuota = 0,
            RemainingMonthlyQuota = 0,
            DailyUsagePercentage = 1,
            MonthlyUsagePercentage = 1,
            WarningLevel = QuotaWarningLevel.Critical
        };
    }
}

/// <summary>
/// 配额预留信息
/// </summary>
public class QuotaReservation
{
    /// <summary>预留的 Token 数量</summary>
    public long ReservedTokens { get; set; }

    /// <summary>预留时间</summary>
    public DateTime ReservedAt { get; set; }
}
