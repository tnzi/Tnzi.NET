namespace Tnzi.Notification.Dtos;

/// <summary>
/// 用户通知收件箱列表项
/// </summary>
public class UserNotificationItem
{
    public Guid Id { get; set; }
    public NotificationType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public NotificationPriority Priority { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadTime { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 用户通知详情（含内容）
/// </summary>
public class UserNotificationDetail : UserNotificationItem
{
    public string Content { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
}

/// <summary>
/// 查询用户通知请求
/// </summary>
public class QueryUserNotificationRequest : PagedQueryDto
{
    public NotificationType? Type { get; set; }
    public bool? IsRead { get; set; }
    public string? Category { get; set; }
    public NotificationPriority? Priority { get; set; }
}

/// <summary>
/// 未读计数
/// </summary>
public class UnreadCountDto
{
    public int TotalUnread { get; set; }
    public Dictionary<string, int> UnreadByCategory { get; set; } = new();
}
