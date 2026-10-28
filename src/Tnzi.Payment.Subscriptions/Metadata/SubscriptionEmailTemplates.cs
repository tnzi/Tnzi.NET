namespace Tnzi.Payment.Subscriptions.Metadata;

/// <summary>
/// 订阅邮件的模板名（<c>Templates/Notification/Email/{名字}.cshtml</c>，随本包复制到应用输出目录）。
/// </summary>
/// <remarks>
/// 部署方在模板管理里建同名模板即可覆盖出厂正文；模板只有一种语言（模板存储没有语言维度），
/// 需要别的语言的部署直接改写这几份。
/// </remarks>
public static class SubscriptionEmailTemplates
{
    /// <summary>续费 / 转正扣款失败，进入或仍在宽限期</summary>
    public const string PaymentFailed = "SubscriptionPaymentFailed";

    /// <summary>扣款前的续费提醒</summary>
    public const string RenewalReminder = "SubscriptionRenewalReminder";

    /// <summary>宽限期满仍未付清，服务已暂停</summary>
    public const string Suspended = "SubscriptionSuspended";
}
