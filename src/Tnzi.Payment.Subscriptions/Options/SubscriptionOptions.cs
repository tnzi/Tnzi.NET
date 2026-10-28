namespace Tnzi.Payment.Subscriptions.Options;

/// <summary>
/// 订阅配置选项。配置路径：<c>Payment:Subscription</c>。
/// </summary>
/// <remarks>
/// 节路径与拆分前<b>一字不变</b>：这个类一直带着绝对路径的 <c>[ConfigSection]</c>，
/// 拆分前由父模块绑（那时它同时还是 <c>PaymentOptions.Subscription</c> 嵌套属性），
/// 现在由 <see cref="PaymentSubscriptionsModule"/> 自己绑。运维手里的 appsettings.json
/// 不需要改一个字符，改的只是「谁来绑、谁来校验」。
///
/// 随模块走还顺手修准了一件事：配置中心的分组是从<b>已加载模块的程序集</b>扫出来的，
/// 类留在父模块时，不做续费的宿主也会看到一个 "Subscription" 分组，
/// 改里面任何一个字段都不生效 —— 渲染出来却控制不了任何东西的设置项，比没有这一项更糟。
/// </remarks>
[ConfigSection("Payment:Subscription")]
[RuntimeSettingGroup(Key = "payment-subscription", Module = "Payment", DisplayName = "Subscription",
    I18nKey = "admin.modules.system.settings.groups.paymentSubscription",
    Icon = "mdi:autorenew", Order = 510)]
public class SubscriptionOptions
{
    /// <summary>
    /// 自动续费提醒天数
    /// </summary>
    [RuntimeSetting(Label = "Auto-Renewal Reminder Days", I18n = "admin.modules.system.settings.fields.paymentAutoRenewalReminderDays",
        Type = SettingFieldType.Int, Min = 0,
        Description = "Days before renewal to send a reminder")]
    public int AutoRenewalReminderDays { get; set; } = 7;

    /// <summary>
    /// 宽限期天数
    /// </summary>
    [RuntimeSetting(Label = "Grace Period Days", I18n = "admin.modules.system.settings.fields.paymentGracePeriodDays",
        Type = SettingFieldType.Int, Min = 0,
        Description = "Days a past-due subscription is retried before expiration")]
    public int GracePeriodDays { get; set; } = 3;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    [RuntimeSetting(Label = "Max Retry Count", I18n = "admin.modules.system.settings.fields.paymentMaxRetryCount",
        Type = SettingFieldType.Int, Min = 0,
        Description = "Maximum off-session billing retries before marking expired")]
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 默认试用天数：计划开启了试用但未设置天数时用它兜底
    /// </summary>
    [RuntimeSetting(Label = "Default Trial Days", I18n = "admin.modules.system.settings.fields.paymentDefaultTrialDays",
        Type = SettingFieldType.Int, Min = 0,
        Description = "Trial length used when a plan allows trials but does not specify days")]
    public int DefaultTrialDays { get; set; } = 14;

    /// <summary>
    /// 暂停订阅的最长天数（0 = 不限制）。超过上限的暂停请求会被拒绝。
    /// </summary>
    [RuntimeSetting(Label = "Max Pause Days", I18n = "admin.modules.system.settings.fields.paymentMaxPauseDays",
        Type = SettingFieldType.Int, Min = 0,
        Description = "Maximum number of days a subscription may stay paused (0 = unlimited)")]
    public int MaxPauseDays { get; set; } = 90;

    /// <summary>
    /// 宽限期满仍未收到款时怎么处理：<c>Expire</c>（默认，订阅过期）或 <c>Suspend</c>（暂停服务，补缴后原样恢复）。
    /// </summary>
    /// <remarks>
    /// 按月向机构收服务费、停服后要等管理者补缴的部署配 <c>Suspend</c>。只影响逾期欠费（PastDue）那一支；
    /// 关掉了自动续费、本期自然走完的订阅照旧过期：那是客户自己不续了，不是欠费。
    /// </remarks>
    [RuntimeSetting(Label = "Grace Period End Action", I18n = "admin.modules.system.settings.fields.paymentGracePeriodEndAction",
        Type = SettingFieldType.Select,
        Description = "What happens when a past-due subscription is still unpaid at the end of the grace period: Expire or Suspend")]
    public GracePeriodEndAction GracePeriodEndAction { get; set; } = GracePeriodEndAction.Expire;

    /// <summary>
    /// 订阅页地址（管理者在这里换卡、补缴），写进催款、提醒与暂停服务邮件里。默认空 = 邮件里不放链接。
    /// </summary>
    [RuntimeSetting(Label = "Subscription Page URL", I18n = "admin.modules.system.settings.fields.paymentSubscriptionManageUrl",
        Type = SettingFieldType.String,
        Description = "Where billing contacts update the card or pay an overdue amount; linked from billing emails")]
    public string? ManageUrl { get; set; }
}
