namespace Tnzi.Notification.Entities;

/// <summary>
/// 用户通知偏好设置
/// 控制每个渠道的启用状态、静默时段和频率限制
/// </summary>
public class NotificationPreference : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 租户ID
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 通知渠道 (Email, Sms, InApp, Webhook)
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// 通知分类（可选，null 表示该渠道的全局偏好）
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 是否启用该渠道
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 静默时段开始时间 (UTC)
    /// </summary>
    public TimeOnly? QuietHoursStart { get; set; }

    /// <summary>
    /// 静默时段结束时间 (UTC)
    /// </summary>
    public TimeOnly? QuietHoursEnd { get; set; }

    /// <summary>
    /// 每小时最大通知频率（null 表示不限制）
    /// </summary>
    public int? MaxFrequencyPerHour { get; set; }
}
