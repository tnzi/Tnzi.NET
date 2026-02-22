namespace Tnzi.Notification.Entities;

/// <summary>
/// 通知状态
/// </summary>
public enum NotificationStatus
{
    /// <summary>
    /// 待发送
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 发送中
    /// </summary>
    Sending = 1,

    /// <summary>
    /// 发送成功
    /// </summary>
    Sent = 2,

    /// <summary>
    /// 发送失败
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 部分发送成功
    /// </summary>
    PartiallySent = 4,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 5
}
