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
    public string DefaultCurrency { get; set; } = PaymentConstants.DefaultCurrency;

    /// <summary>
    /// 默认支付渠道代码
    /// </summary>
    [RuntimeSetting(Label = "Default Payment Channel", I18n = "admin.modules.system.settings.fields.paymentDefaultChannelCode",
        Type = SettingFieldType.String,
        Description = "Channel code used when a request does not specify one")]
    public string DefaultChannelCode { get; set; } = PaymentConstants.DefaultPaymentChannel;

    /// <summary>
    /// 自动关闭过期支付（分钟）
    /// </summary>
    [RuntimeSetting(Label = "Auto-Close Expired Payment (minutes)", I18n = "admin.modules.system.settings.fields.paymentAutoCloseExpireMinutes",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Background",
        Description = "Minutes after which an unpaid pending payment is automatically closed")]
    public int AutoCloseExpireMinutes { get; set; } = 30;

    /// <summary>
    /// 线下支付的有效期（天）。
    /// </summary>
    /// <remarks>
    /// 线下渠道必须与在线渠道分开计时：银行转账 / 汇款要几天才到账，
    /// 套用在线渠道的分钟级过期，等于在钱到账之前就把订单关掉了。
    /// </remarks>
    [RuntimeSetting(Label = "Offline Payment Validity (days)", I18n = "admin.modules.system.settings.fields.paymentOfflineExpireDays",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Background",
        Description = "Days an offline payment (bank transfer, wire, cheque) stays open awaiting manual confirmation")]
    public int OfflineExpireDays { get; set; } = 7;

    /// <summary>
    /// 默认支付完成后的浏览器跳转地址。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="DefaultNotifyUrl"/> 是两回事：return 是付款人浏览器回到的页面，
    /// notify 是渠道服务端回调本系统的地址。此前二者混用会把 webhook 地址塞进 ReturnUrl。
    /// </remarks>
    [RuntimeSetting(Label = "Default Return URL", I18n = "admin.modules.system.settings.fields.paymentDefaultReturnUrl",
        Type = SettingFieldType.String,
        Description = "Where the payer's browser lands after completing payment")]
    public string? DefaultReturnUrl { get; set; }

    /// <summary>
    /// 默认异步通知（webhook）地址：渠道服务端回调本系统的地址，仅部分渠道（如 PayPal）需要在建单时告知。
    /// </summary>
    [RuntimeSetting(Label = "Default Notify URL", I18n = "admin.modules.system.settings.fields.defaultNotifyUrl",
        Type = SettingFieldType.String,
        Description = "Server-to-server webhook address reported to channels that require it")]
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

    /// <summary>
    /// 退款对账扫描回溯天数：只回查这段时间内仍未终结的退款，避免全表扫描。默认 30 天
    /// </summary>
    [RuntimeSetting(Label = "Refund Reconcile Lookback (days)", I18n = "admin.modules.system.settings.fields.paymentRefundReconcileLookbackDays",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Background",
        Description = "How far back the background scan re-queries refunds that are still in progress")]
    public int RefundReconcileLookbackDays { get; set; } = 30;
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
    /// 该渠道的默认币种：请求未指定币种时优先于全局 <see cref="PaymentOptions.DefaultCurrency"/> 生效
    /// </summary>
    public string? Currency { get; set; }
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
/// 由 <see cref="Services.DefaultTaxCalculator"/> 消费，参与支付应付额与发票税额的计算。
/// </summary>
[ConfigSection("Payment:Tax")]
[RuntimeSettingGroup(Key = "payment-tax", Module = "Payment", DisplayName = "Tax",
    I18nKey = "admin.modules.system.settings.groups.paymentTax",
    Icon = "mdi:percent-outline", Order = 540)]
public class TaxOptions
{
    /// <summary>
    /// 是否启用计税
    /// </summary>
    [RuntimeSetting(Label = "Tax Enabled", I18n = "admin.modules.system.settings.fields.paymentTaxEnabled",
        Type = SettingFieldType.Boolean,
        Description = "Apply tax to payments and invoices")]
    public bool Enabled { get; set; }

    /// <summary>
    /// 默认税率（百分数，如 13 表示 13%）
    /// </summary>
    [RuntimeSetting(Label = "Default Tax Rate (%)", I18n = "admin.modules.system.settings.fields.paymentDefaultTaxRate",
        Type = SettingFieldType.Decimal, Min = 0, Max = 100,
        Description = "Flat tax rate as a percentage, e.g. 13 means 13%")]
    public decimal DefaultTaxRate { get; set; }

    /// <summary>
    /// 税额是否含在价格中（价内税）。true 时标价即应付额，税额仅在发票上列示。
    /// </summary>
    [RuntimeSetting(Label = "Tax Included In Price", I18n = "admin.modules.system.settings.fields.paymentTaxIncluded",
        Type = SettingFieldType.Boolean,
        Description = "When enabled the listed price already contains tax; tax is only itemised on the invoice")]
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
    /// 每用户单张优惠券的默认使用次数上限：促销未单独设置 PerUserUsageLimit 时用它兜底。
    /// </summary>
    [RuntimeSetting(Label = "Max Coupon Usage Per User", I18n = "admin.modules.system.settings.fields.paymentMaxCouponUsagePerUser",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Fallback per-user usage cap for promotions that do not set their own limit")]
    public int MaxCouponUsagePerUser { get; set; } = 5;

    /// <summary>
    /// 是否启用Stripe优惠券同步
    /// </summary>
    [RuntimeSetting(Label = "Enable Stripe Coupon Sync", I18n = "admin.modules.system.settings.fields.paymentEnableStripeCouponSync",
        Type = SettingFieldType.Boolean,
        Description = "Sync promotions to Stripe as coupons")]
    public bool EnableStripeCouponSync { get; set; } = true;
}
