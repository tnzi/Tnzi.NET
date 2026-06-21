namespace Tnzi.Payment.Options;

/// <summary>
/// 支付模块配置选项
/// 配置路径：Payment
/// </summary>
[ConfigSection("Payment")]
[RuntimeSettingGroup(Key = "payment-general", Module = "Payment", DisplayName = "Payment",
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
/// </summary>
public class SubscriptionOptions
{
    /// <summary>
    /// 自动续费提醒天数
    /// </summary>
    public int AutoRenewalReminderDays { get; set; } = 7;

    /// <summary>
    /// 宽限期天数
    /// </summary>
    public int GracePeriodDays { get; set; } = 3;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 默认试用天数
    /// </summary>
    public int DefaultTrialDays { get; set; } = 14;
}

/// <summary>
/// 发票配置选项
/// </summary>
public class InvoiceOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 默认模板
    /// </summary>
    public string DefaultTemplate { get; set; } = "InvoiceDefault";

    /// <summary>
    /// 支付成功后自动发送
    /// </summary>
    public bool AutoSendOnPayment { get; set; } = true;

    /// <summary>
    /// 公司名称
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// 公司地址
    /// </summary>
    public string? CompanyAddress { get; set; }

    /// <summary>
    /// 公司邮箱
    /// </summary>
    public string? CompanyEmail { get; set; }

    /// <summary>
    /// 税号
    /// </summary>
    public string? TaxId { get; set; }
}

/// <summary>
/// 税务配置选项
/// </summary>
public class TaxOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 税务提供商
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
/// </summary>
public class PromotionOptions
{
    /// <summary>
    /// 默认首次订阅折扣
    /// </summary>
    public decimal DefaultFirstSubscriptionDiscount { get; set; } = 0.2m;

    /// <summary>
    /// 每用户最大优惠券使用次数
    /// </summary>
    public int MaxCouponUsagePerUser { get; set; } = 5;

    /// <summary>
    /// 是否启用Stripe优惠券同步
    /// </summary>
    public bool EnableStripeCouponSync { get; set; } = true;
}
