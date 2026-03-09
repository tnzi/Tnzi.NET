using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Events;

/// <summary>
/// 通知创建事件
/// </summary>
public class NotificationCreatedEvent : EventBase
{
    public Guid MessageId { get; set; }
    public NotificationType Type { get; set; }
    public int RecipientCount { get; set; }
    public NotificationPriority Priority { get; set; }
    public string Category { get; set; } = "General";
}

/// <summary>
/// 通知发送成功事件
/// </summary>
public class NotificationSentEvent : EventBase
{
    public Guid MessageId { get; set; }
    public NotificationType Type { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public DateTime SentTime { get; set; }
}

/// <summary>
/// 通知发送失败事件
/// </summary>
public class NotificationFailedEvent : EventBase
{
    public Guid MessageId { get; set; }
    public NotificationType Type { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; }
}
