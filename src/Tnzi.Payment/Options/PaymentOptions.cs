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
    /// 只管付款人浏览器回到的页面。渠道服务端回调本系统的地址（webhook）不在这里配：
    /// 它在渠道后台登记（Stripe / PayPal 都是），指向 <c>/api/payments/callback/{渠道}</c>。
    /// </remarks>
    [RuntimeSetting(Label = "Default Return URL", I18n = "admin.modules.system.settings.fields.paymentDefaultReturnUrl",
        Type = SettingFieldType.String,
        Description = "Where the payer's browser lands after completing payment")]
    public string? DefaultReturnUrl { get; set; }

    /// <summary>
    /// 允许作为付款人回跳目标的主机名清单（精确匹配，大小写不敏感）。
    /// </summary>
    /// <remarks>
    /// 调用方在建单 / 绑卡时给的 <c>ReturnUrl</c> / <c>CancelUrl</c> 会被原样交给支付渠道，
    /// 作为付款完成后把**付款人的浏览器**送去的地方 —— 不限制就是一个开放重定向，
    /// 且发生在渠道支付页之后：付款人刚输完卡号，落在仿冒的「订单完成」页上不会有任何怀疑。
    /// <para>
    /// <b>未配置时失败关闭</b>：任何调用方指定的回跳地址都会被拒（400），只有
    /// <see cref="DefaultReturnUrl"/> 所在主机自动算作允许 —— 它已经是这台部署公开承认的落地页，
    /// 逼运维把同一个主机名写两遍只会换来一次配漏。不指定回跳地址的调用一字不差。
    /// </para>
    /// <b>不支持通配符子域</b>：<c>*.example.com</c> 会把一个被接管的子域
    /// （常见于过期的 CNAME）一起放进来，而多写几行主机名的成本是零。
    /// </remarks>
    public List<string> AllowedRedirectHosts { get; set; } = [];

    /// <summary>
    /// 对账导出单次最多导出的支付笔数，默认 50000。
    /// </summary>
    /// <remarks>
    /// 上界是闸门不是调优项：导出把整段时间窗内的支付**全部读进内存**再拼成一个字符串放进
    /// JSON 响应体，笔数没有上界，一次「导出全年」就能放倒一个进程。
    /// 超出上界时导出**截断并如实报告**（<c>Truncated</c> + <c>MatchedRecords</c>），
    /// 而不是安静地少给几行 —— 一份看起来完整的对账单少了一半，比一份明说被截断的糟得多。
    /// </remarks>
    [RuntimeSetting(Label = "Reconciliation Export Max Rows", I18n = "admin.modules.system.settings.fields.paymentReconciliationExportMaxRows",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Maximum payments a single reconciliation export may contain")]
    public int ReconciliationExportMaxRows { get; set; } = 50000;

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
    /// 付款方能否自己发起退款申请（用户面 <c>POST /refunds</c>）。默认 true，与引入这一项之前相同。
    /// </summary>
    /// <remarks>
    /// 关掉之后退款只能由运营方经管理端发起（<c>payment.refund.create</c>）：按月向客户收服务费的部署里，
    /// 「客户自己点一下就把这个月的服务费退回去」不是一个该存在的按钮，哪怕后面还有一道审批。
    /// 关着时用户面答 403 <c>PAYMENT_REFUND_REQUESTS_DISABLED</c>；查看自己的退款记录不受影响。
    /// </remarks>
    [RuntimeSetting(Label = "Allow Customer Refund Requests", I18n = "admin.modules.system.settings.fields.paymentAllowCustomerRefundRequests",
        Type = SettingFieldType.Boolean,
        Description = "Let payers request refunds themselves; when off, only administrators can raise refunds")]
    public bool AllowCustomerRefundRequests { get; set; } = true;

    /// <summary>
    /// 支付渠道配置：键是渠道代码（<c>Stripe</c> / <c>PayPal</c> / <c>Offline</c> …），
    /// 决定 <c>PaymentProviderFactory</c> 发不发这个渠道。
    /// </summary>
    /// <remarks>
    /// 这里管的是「发不发」，不是「怎么连」。各渠道的凭据配置类随实现住在可选子模块里：
    /// <c>Payment:Stripe</c> → <c>Tnzi.Payment.Stripe</c>，<c>Payment:PayPal</c> → <c>Tnzi.Payment.PayPal</c>。
    /// 拆包没有改动任何配置节路径，只是绑定与校验换了地方。
    /// 两处都要配：这里 <c>Enabled=true</c> 让渠道可被选中，那边的凭据让它真能连上。
    /// </remarks>
    public Dictionary<string, ChannelOptions> Channels { get; set; } = new();

    // 订阅配置随续费域搬进了可选子模块 Tnzi.Payment.Subscriptions（类 SubscriptionOptions，
    // 由它自己绑同一个绝对节 Payment:Subscription）。配置 JSON 的形状一字不变，
    // 少的只是这条嵌套属性 —— 拆分前本模块也从来没有读过它。

    // 发票配置随发票域搬进了可选子模块 Tnzi.Payment.Billing（类 InvoiceOptions，
    // 由它自己绑同一个绝对节 Payment:Invoice）。配置 JSON 的形状一字不变，
    // 少的只是这条嵌套属性 —— 本模块从来没有读过它。

    /// <summary>
    /// 税务配置
    /// </summary>
    public TaxOptions Tax { get; set; } = new();

    // 促销配置随促销域搬进了可选子模块 Tnzi.Payment.Promotions（类 PromotionOptions，
    // 由它自己绑同一个绝对节 Payment:Promotion）。配置 JSON 的形状一字不变，
    // 少的只是这条嵌套属性 —— 本模块从来没有读过它（读的一直是单独注入的 IOptionsMonitor<PromotionOptions>）。

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
/// 税务配置选项
/// 配置路径：Payment:Tax
/// 由 <see cref="Services.DefaultPaymentTaxCalculator"/> 消费，参与支付应付额与发票税额的计算。
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

    /// <summary>
    /// 税种名，印在收据与发票的税额行上（例如 <c>HST</c>，行文为「HST 13%」）。默认 <c>Tax</c>。
    /// </summary>
    [RuntimeSetting(Label = "Tax Name", I18n = "admin.modules.system.settings.fields.paymentTaxName",
        Type = SettingFieldType.String,
        Description = "Name printed on the tax line of receipts and invoices, e.g. HST")]
    public string Name { get; set; } = "Tax";

    /// <summary>
    /// 卖方在这个税种下的登记号（例如加拿大的 GST/HST 号），印在税额行上。默认空 = 不印。
    /// </summary>
    [RuntimeSetting(Label = "Tax Registration Number", I18n = "admin.modules.system.settings.fields.paymentTaxRegistrationNumber",
        Type = SettingFieldType.String,
        Description = "Your registration number for this tax, printed next to the tax line")]
    public string? RegistrationNumber { get; set; }
}
