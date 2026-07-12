namespace Tnzi.Payment.Options;

/// <summary>
/// 支付模块配置选项
/// 配置路径：Payment
/// </summary>
[ConfigSection("Payment")]
[RuntimeSettingGroup(Key = "payment-general", Module = "Payment", DisplayName = "General",
    I18nKey = "admin.modules.system.settings.groups.paymentGeneral",
    Icon = "mdi:credit-card-settings-outline", Order = 500)]
public class PaymentOptions
{
    /// <summary>
    /// 默认币种
    /// </summary>
    [RuntimeSetting(Label = "Default Currency", I18n = "admin.modules.system.settings.fields.defaultCurrency",
        Type = SettingFieldType.String)]
    public string DefaultCurrency { get; set; } = "USD";

    /// <summary>
    /// 自动关闭过期支付（分钟）
    /// </summary>
    [RuntimeSetting(Label = "Auto-Close Expired Payment (minutes)", I18n = "admin.modules.system.settings.fields.paymentAutoCloseExpireMinutes",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Background",
        Description = "Minutes after which an unpaid pending payment is automatically closed")]
    public int AutoCloseExpireMinutes { get; set; } = 30;

    /// <summary>
    /// 默认回调URL
    /// </summary>
    [RuntimeSetting(Label = "Default Notify URL", I18n = "admin.modules.system.settings.fields.defaultNotifyUrl",
        Type = SettingFieldType.String)]
    public string? DefaultNotifyUrl { get; set; }

    /// <summary>
    /// 每日最大退款金额
    /// </summary>
    [RuntimeSetting(Label = "Max Refund Amount Per Day", I18n = "admin.modules.system.settings.fields.maxRefundAmountPerDay",
        Type = SettingFieldType.Decimal, Min = 0)]
    public decimal MaxRefundAmountPerDay { get; set; } = 10000m;

    /// <summary>
    /// 退款审批阈值
    /// </summary>
    [RuntimeSetting(Label = "Refund Approval Threshold", I18n = "admin.modules.system.settings.fields.refundApprovalThreshold",
        Type = SettingFieldType.Decimal, Min = 0)]
    public decimal RefundApprovalThreshold { get; set; } = 1000m;

    /// <summary>
    /// 是否启用退款审批
    /// </summary>
    [RuntimeSetting(Label = "Enable Refund Approval", I18n = "admin.modules.system.settings.fields.enableRefundApproval",
        Type = SettingFieldType.Boolean)]
    public bool EnableRefundApproval { get; set; } = true;

    /// <summary>
    /// 支付渠道配置
    /// </summary>
    public Dictionary<string, ChannelOptions> Channels { get; set; } = new();

    /// <summary>
    /// 订阅配置
    /// </summary>
    public SubscriptionOptions Subscription { get; set; } = new();

    /// <summary>
    /// 发票配置
    /// </summary>
    public InvoiceOptions Invoice { get; set; } = new();

    /// <summary>
    /// 税务配置
    /// </summary>
    public TaxOptions Tax { get; set; } = new();

    /// <summary>
    /// 促销配置
    /// </summary>
    public PromotionOptions Promotion { get; set; } = new();

    /// <summary>
    /// 后台任务执行间隔（分钟），默认 5 分钟
    /// </summary>
    [RuntimeSetting(Label = "Background Task Interval (minutes)", I18n = "admin.modules.system.settings.fields.paymentBackgroundTaskIntervalMinutes",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Background",
        Description = "Interval between background billing scans (renewal, trial conversion, expiration)")]
    public int BackgroundTaskIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// 是否允许使用测试渠道（NullProvider）。默认 false，生产环境务必保持关闭，
    /// 否则调用方可选 "Null" 渠道在不实际收款的情况下让支付/退款"成功"。
    /// </summary>
    public bool AllowTestProvider { get; set; }

    /// <summary>
    /// 后台计费抢占锁时长（分钟），多实例下避免重复扣款，默认 10 分钟
    /// </summary>
    public int BillingLockMinutes { get; set; } = 10;
}

/// <summary>
/// 渠道配置基类
/// </summary>
public class ChannelOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";
}

/// <summary>
/// 订阅配置选项
/// 配置路径：Payment:Subscription（作为 PaymentOptions 嵌套属性，经父 IOptionsMonitor&lt;PaymentOptions&gt; 热消费）
/// </summary>
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
    /// 默认试用天数
    /// </summary>
    [RuntimeSetting(Label = "Default Trial Days", I18n = "admin.modules.system.settings.fields.paymentDefaultTrialDays",
        Type = SettingFieldType.Int, Min = 0,
        Description = "Default trial length when a plan does not specify one")]
    public int DefaultTrialDays { get; set; } = 14;
}

/// <summary>
/// 发票配置选项
/// 配置路径：Payment:Invoice
/// </summary>
[ConfigSection("Payment:Invoice")]
[RuntimeSettingGroup(Key = "payment-invoice", Module = "Payment", DisplayName = "Invoice",
    I18nKey = "admin.modules.system.settings.groups.paymentInvoice",
    Icon = "mdi:file-document-outline", Order = 520)]
public class InvoiceOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    [RuntimeSetting(Label = "Invoice Enabled", I18n = "admin.modules.system.settings.fields.paymentInvoiceEnabled",
        Type = SettingFieldType.Boolean,
        Description = "Enable invoice generation")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 默认模板
    /// </summary>
    [RuntimeSetting(Label = "Default Invoice Template", I18n = "admin.modules.system.settings.fields.paymentInvoiceDefaultTemplate",
        Type = SettingFieldType.String,
        Description = "Template name used when none is specified")]
    public string DefaultTemplate { get; set; } = "InvoiceDefault";

    /// <summary>
    /// 支付成功后自动发送
    /// </summary>
    [RuntimeSetting(Label = "Auto-Send On Payment", I18n = "admin.modules.system.settings.fields.paymentInvoiceAutoSendOnPayment",
        Type = SettingFieldType.Boolean,
        Description = "Automatically generate and send an invoice when a payment succeeds")]
    public bool AutoSendOnPayment { get; set; } = true;

    /// <summary>
    /// 公司名称
    /// </summary>
    [RuntimeSetting(Label = "Company Name", I18n = "admin.modules.system.settings.fields.paymentInvoiceCompanyName",
        Type = SettingFieldType.String, Subsection = "Company")]
    public string? CompanyName { get; set; }

    /// <summary>
    /// 公司地址
    /// </summary>
    [RuntimeSetting(Label = "Company Address", I18n = "admin.modules.system.settings.fields.paymentInvoiceCompanyAddress",
        Type = SettingFieldType.String, Subsection = "Company")]
    public string? CompanyAddress { get; set; }

    /// <summary>
    /// 公司邮箱
    /// </summary>
    [RuntimeSetting(Label = "Company Email", I18n = "admin.modules.system.settings.fields.paymentInvoiceCompanyEmail",
        Type = SettingFieldType.String, Subsection = "Company")]
    public string? CompanyEmail { get; set; }

    /// <summary>
    /// 税号
    /// </summary>
    [RuntimeSetting(Label = "Tax ID", I18n = "admin.modules.system.settings.fields.paymentInvoiceTaxId",
        Type = SettingFieldType.String, Subsection = "Company")]
    public string? TaxId { get; set; }
}

/// <summary>
/// 税务配置选项
/// 配置路径：Payment:Tax
/// KEEP-STATIC：当前无运行时消费者（仅 AddTnziOptions 注册、无服务注入读取），
/// 暴露到配置中心会造成"假热配"（改了不生效）。待税务计算链路接线后再评估暴露。
/// </summary>
public class TaxOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 税务提供商
    /// KEEP-STATIC：provider 选择 = 装配门（决定加载哪个税务计算装配），不做热改。
    /// </summary>
    public string Provider { get; set; } = "StripeTax";

    /// <summary>
    /// 默认税率
    /// </summary>
    public decimal DefaultTaxRate { get; set; }

    /// <summary>
    /// 税额是否含在价格中
    /// </summary>
    public bool TaxIncluded { get; set; }
}

/// <summary>
/// 促销配置选项
/// 配置路径：Payment:Promotion
/// </summary>
[ConfigSection("Payment:Promotion")]
[RuntimeSettingGroup(Key = "payment-promotion", Module = "Payment", DisplayName = "Promotion",
    I18nKey = "admin.modules.system.settings.groups.paymentPromotion",
    Icon = "mdi:tag-outline", Order = 530)]
public class PromotionOptions
{
    /// <summary>
    /// 默认首次订阅折扣
    /// </summary>
    [RuntimeSetting(Label = "Default First-Subscription Discount", I18n = "admin.modules.system.settings.fields.paymentDefaultFirstSubscriptionDiscount",
        Type = SettingFieldType.Decimal, Min = 0, Max = 1,
        Description = "Discount fraction for a user's first subscription (0-1, e.g. 0.2 = 20%)")]
    public decimal DefaultFirstSubscriptionDiscount { get; set; } = 0.2m;

    /// <summary>
    /// 每用户最大优惠券使用次数
    /// </summary>
    [RuntimeSetting(Label = "Max Coupon Usage Per User", I18n = "admin.modules.system.settings.fields.paymentMaxCouponUsagePerUser",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Maximum number of coupon redemptions allowed per user")]
    public int MaxCouponUsagePerUser { get; set; } = 5;

    /// <summary>
    /// 是否启用Stripe优惠券同步
    /// </summary>
    [RuntimeSetting(Label = "Enable Stripe Coupon Sync", I18n = "admin.modules.system.settings.fields.paymentEnableStripeCouponSync",
        Type = SettingFieldType.Boolean,
        Description = "Sync promotions to Stripe as coupons")]
    public bool EnableStripeCouponSync { get; set; } = true;
}
