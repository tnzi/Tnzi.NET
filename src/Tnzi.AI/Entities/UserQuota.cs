namespace Tnzi.AI.Entities;

/// <summary>
/// 用户配额实体 - 管理用户的 Token 使用配额
/// </summary>
public class UserQuota : MultiTenantAuditedEntity<Guid>
{
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
    /// 最后重置日期（用于判断是否需要重置每日/每月配额）
    /// </summary>
    public DateTime LastResetDate { get; set; }

    /// <summary>
    /// 是否启用配额限制
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 乐观并发令牌（用于防止并发更新竞态）
    /// </summary>
    public uint Version { get; set; }
}
