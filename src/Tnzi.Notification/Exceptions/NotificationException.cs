namespace Tnzi.Notification.Exceptions;

/// <summary>
/// 通知异常基类
/// </summary>
public class NotificationException : BusinessException
{
    /// <summary>
    /// 通知类型
    /// </summary>
    public string? NotificationType { get; }
    
    /// <summary>
    /// 初始化一个<see cref="NotificationException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="notificationType">通知类型</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="httpStatusCode">HTTP状态码</param>
    public NotificationException(
        string message, 
        string? notificationType = null,
        string errorCode = ErrorCodes.NOTIFICATION_ERROR, 
        int httpStatusCode = 400)
        : base(message, errorCode, httpStatusCode)
    {
        NotificationType = notificationType;
        if (notificationType != null)
        {
            this.WithData("NotificationType", notificationType);
        }
    }
}

