namespace Tnzi.Payment.Metadata;

/// <summary>
/// Payment 模块业务常量定义
/// </summary>
public static class PaymentConstants
{
    #region 支付配置常量

    /// <summary>
    /// 默认币种
    /// </summary>
    public const string DefaultCurrency = "USD";

    /// <summary>
    /// 默认支付渠道
    /// </summary>
    public const string DefaultPaymentChannel = "Stripe";

    /// <summary>
    /// 默认支付方式
    /// </summary>
    public const string DefaultPaymentMethod = "CreditCard";

    /// <summary>
    /// 支付过期时间（分钟）
    /// </summary>
    public const int DefaultPaymentExpireMinutes = 30;

    /// <summary>
    /// 默认发票到期天数
    /// </summary>
    public const int DefaultInvoiceDueDays = 30;

    /// <summary>
    /// 最大退款审批阈值
    /// </summary>
    public const decimal MaxRefundApprovalThreshold = 1000.00m;

    /// <summary>
    /// 每日最大退款限额
    /// </summary>
    public const decimal MaxRefundAmountPerDay = 10000.00m;

    #endregion

    #region 订阅配置常量

    /// <summary>
    /// 默认试用天数
    /// </summary>
    public const int DefaultTrialDays = 7;

    /// <summary>
    /// 最大试用折扣百分比
    /// </summary>
    public const int MaxTrialDiscountPercent = 100;

    /// <summary>
    /// 订阅续费提前提醒天数
    /// </summary>
    public const int SubscriptionRenewalReminderDays = 7;

    /// <summary>
    /// 订阅宽限期天数
    /// </summary>
    public const int SubscriptionGracePeriodDays = 3;

    #endregion

    #region 促销配置常量

    /// <summary>
    /// 最大折扣百分比
    /// </summary>
    public const int MaxDiscountPercent = 100;

    /// <summary>
    /// 最大固定折扣金额
    /// </summary>
    public const decimal MaxFixedDiscountAmount = 10000.00m;

    /// <summary>
    /// 默认促销优先级
    /// </summary>
    public const int DefaultPromotionPriority = 0;

    /// <summary>
    /// 最大使用次数限制
    /// </summary>
    public const int MaxUsageLimit = 999999;

    /// <summary>
    /// 默认每用户使用次数限制
    /// </summary>
    public const int DefaultPerUserUsageLimit = 1;

    #endregion

    #region 发票配置常量

    /// <summary>
    /// 默认发票模板
    /// </summary>
    public const string DefaultInvoiceTemplate = "Standard";

    /// <summary>
    /// 默认税率
    /// </summary>
    public const decimal DefaultTaxRate = 0m;

    /// <summary>
    /// 发票发送最大次数
    /// </summary>
    public const int MaxInvoiceSendCount = 5;

    #endregion

    #region 回调参数保留键

    /// <summary>
    /// 回调保留键前缀。回调控制器把 HTTP header 与请求体字段放进同一个字典，
    /// 带此前缀的键由框架写入（原始报文、签名头等），解析请求体时必须跳过同名字段，
    /// 否则渠道报文里出现同名字段就能顶掉签名验证所依赖的原始值。
    /// </summary>
    public const string CallbackReservedKeyPrefix = "__";

    /// <summary>
    /// 回调原始请求体（签名验证基准）
    /// </summary>
    public const string CallbackRawBodyKey = "__raw_body";

    /// <summary>
    /// Stripe 签名头
    /// </summary>
    public const string CallbackStripeSignatureKey = "__stripe_signature";

    /// <summary>
    /// PayPal 传输ID
    /// </summary>
    public const string CallbackPayPalTransmissionIdKey = "__paypal_transmission_id";

    /// <summary>
    /// PayPal 传输时间
    /// </summary>
    public const string CallbackPayPalTransmissionTimeKey = "__paypal_transmission_time";

    /// <summary>
    /// PayPal 传输签名
    /// </summary>
    public const string CallbackPayPalTransmissionSigKey = "__paypal_transmission_sig";

    /// <summary>
    /// PayPal 证书地址
    /// </summary>
    public const string CallbackPayPalCertUrlKey = "__paypal_cert_url";

    /// <summary>
    /// PayPal 签名算法
    /// </summary>
    public const string CallbackPayPalAuthAlgoKey = "__paypal_auth_algo";

    #endregion

    #region 渠道代码常量

    /// <summary>
    /// Stripe 渠道
    /// </summary>
    public const string StripeChannelCode = "Stripe";

    /// <summary>
    /// PayPal 渠道
    /// </summary>
    public const string PayPalChannelCode = "PayPal";

    /// <summary>
    /// 线下渠道（银行转账/现金/支票等由人工确认收款的方式）
    /// </summary>
    public const string OfflineChannelCode = "Offline";

    /// <summary>
    /// 测试渠道
    /// </summary>
    public const string NullChannelCode = "Null";

    #endregion

    #region 缓存键常量

    /// <summary>
    /// 支付记录缓存键前缀
    /// </summary>
    public const string PaymentCacheKeyPrefix = "payment:";

    /// <summary>
    /// 订阅计划缓存键前缀
    /// </summary>
    public const string SubscriptionPlanCacheKeyPrefix = "subscription:plan:";

    /// <summary>
    /// 促销代码缓存键前缀
    /// </summary>
    public const string PromotionCacheKeyPrefix = "promotion:";

    #endregion

    #region 业务规则常量

    /// <summary>
    /// 最小支付金额
    /// </summary>
    public const decimal MinimumPaymentAmount = 0.01m;

    /// <summary>
    /// 最大支付金额
    /// </summary>
    public const decimal MaximumPaymentAmount = 999999999.99m;

    /// <summary>
    /// 最小退款金额
    /// </summary>
    public const decimal MinimumRefundAmount = 0.01m;

    /// <summary>
    /// 支付流水号前缀
    /// </summary>
    public const string TradeNoPrefix = "PAY";

    /// <summary>
    /// 退款流水号前缀
    /// </summary>
    public const string RefundNoPrefix = "REF";

    /// <summary>
    /// 订阅流水号前缀
    /// </summary>
    public const string SubscriptionNoPrefix = "SUB";

    /// <summary>
    /// 发票流水号前缀
    /// </summary>
    public const string InvoiceNoPrefix = "INV";

    #endregion
}
