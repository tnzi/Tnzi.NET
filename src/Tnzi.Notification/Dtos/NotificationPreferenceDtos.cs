namespace Tnzi.Notification.Dtos;

/// <summary>
/// 通知偏好输出 DTO
/// </summary>
public class NotificationPreferenceDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string? Category { get; set; }
    public bool IsEnabled { get; set; }
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
    public int? MaxFrequencyPerHour { get; set; }
}

/// <summary>
/// 设置通知偏好输入 DTO
/// </summary>
public class SetNotificationPreferenceDto
{
    /// <summary>
    /// 通知渠道 (Email, Sms, InApp, Webhook)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Channel { get; set; } = null!;

    /// <summary>
    /// 通知分类（可选，null 表示该渠道的全局偏好）
    /// </summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>
    /// 是否启用
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
    /// 每小时最大通知频率
    /// </summary>
    public int? MaxFrequencyPerHour { get; set; }
}
