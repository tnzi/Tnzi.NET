namespace Tnzi.Notification.Metadata;

/// <summary>
/// 通知类型
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// 邮件
    /// </summary>
    Email = 1,

    /// <summary>
    /// 短信
    /// </summary>
    Sms = 2,

    /// <summary>
    /// 推送通知
    /// </summary>
    Push = 3,

    /// <summary>
    /// 传真
    /// </summary>
    /// <remarks>
    /// 收件人地址是**传真号码**（不是邮箱），内容是**恰好一个 PDF 附件**，正文不参与投递
    /// （网关要求正文为空，见 <see cref="Services.IFaxSender"/>）。因此以 Fax 建的消息，
    /// <c>Content</c> 只是留档，不会出现在对方收到的那一份上。
    /// </remarks>
    Fax = 4
}
