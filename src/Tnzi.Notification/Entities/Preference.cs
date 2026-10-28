namespace Tnzi.Notification.Entities;

/// <summary>
/// 用户通知偏好设置
/// 控制每个渠道的启用状态、静默时段和频率限制
/// </summary>
public class Preference : AuditedEntity<Guid>, IMultiTenant
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
    /// 静默时段开始时间（墙上时刻，按 <see cref="QuietHoursTimeZone"/> 解释）
    /// </summary>
    public TimeOnly? QuietHoursStart { get; set; }

    /// <summary>
    /// 静默时段结束时间（墙上时刻，不含；按 <see cref="QuietHoursTimeZone"/> 解释）
    /// </summary>
    public TimeOnly? QuietHoursEnd { get; set; }

    /// <summary>
    /// 解释静默时段的 IANA 时区标识（如 <c>America/Toronto</c>）；null 表示两个时刻是 UTC 时刻。
    /// </summary>
    /// <remarks>
    /// 按时区存而不是让写入方换算成 UTC：固定换算在每次夏令时切换后都偏一个小时，且毫无症状。
    /// 判定见 <see cref="Services.QuietHoursWindow"/>。
    /// </remarks>
    public string? QuietHoursTimeZone { get; set; }

    /// <summary>
    /// 每小时最大通知频率（null 表示不限制）
    /// </summary>
    public int? MaxFrequencyPerHour { get; set; }
}
