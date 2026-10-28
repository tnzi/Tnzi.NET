namespace Tnzi.Payment.Subscriptions.Dtos;

/// <summary>
/// 订阅计费元数据：随订阅相关支付（initial/renewal/trial_conversion/proration）写入 Payment.ExtraData，
/// 支付完成/失败时由 <see cref="SubscriptionPaymentContext"/> 解析回订阅状态机。
/// </summary>
public class SubscriptionBillingMetadata
{
    /// <summary>
    /// 标记此 ExtraData 为订阅计费元数据（区别于用户自定义 ExtraData）
    /// </summary>
    public bool IsSubscriptionBilling { get; set; } = true;

    /// <summary>
    /// 计费用途
    /// </summary>
    public SubscriptionBillingPurpose Purpose { get; set; }

    /// <summary>
    /// 订阅ID（已存在订阅时填写；首次开通时订阅尚未落库，可为空，按 SubscriptionNo 关联）
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// 关联的订阅变更ID（proration 时使用）
    /// </summary>
    public Guid? ChangeId { get; set; }

    /// <summary>
    /// 这笔钱覆盖的服务期开始（含）。印在发票 / 收据的明细行上；早于引入这一项的支付没有它。
    /// </summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>服务期结束（不含）。</summary>
    public DateTime? PeriodEnd { get; set; }

    /// <summary>计划名（明细行的描述）。</summary>
    public string? PlanName { get; set; }

    /// <summary>
    /// 这笔续费里一并收的上一期超额存储费（后付）；没有超额时为空。金额已含在支付的 <c>OriginalAmount</c> 里，
    /// 票据上拆成单独一行。
    /// </summary>
    public StorageOverageCharge? StorageOverage { get; set; }

    /// <summary>
    /// 序列化为 ExtraData JSON
    /// </summary>
    public string ToExtraData() => JsonSerializer.Serialize(this);

    /// <summary>
    /// 从 ExtraData JSON 解析（非订阅计费元数据或解析失败返回 null）
    /// </summary>
    public static SubscriptionBillingMetadata? TryParse(string? extraData)
    {
        if (string.IsNullOrWhiteSpace(extraData) || !extraData.Contains("IsSubscriptionBilling", StringComparison.Ordinal))
            return null;

        try
        {
            var meta = JsonSerializer.Deserialize<SubscriptionBillingMetadata>(extraData);
            return meta?.IsSubscriptionBilling == true ? meta : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// 一笔续费里后付的超额存储费（随计费元数据写进支付）。
/// </summary>
public class StorageOverageCharge
{
    /// <summary>被计量的那一期开始（含）。</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>被计量的那一期结束（不含）。</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>这一期采到样本的天数。</summary>
    public int SampleDays { get; set; }

    /// <summary>样本平均字节数。</summary>
    public long AverageBytes { get; set; }

    /// <summary>计划包含的字节数。</summary>
    public long IncludedBytes { get; set; }

    /// <summary>超出部分折合的 GB-月（1 GB = 2^30 字节）。</summary>
    public decimal Quantity { get; set; }

    /// <summary>每 GB-月单价。</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>金额（税前）。</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// 支付完成/失败回流到订阅状态机的上下文
/// </summary>
public class SubscriptionPaymentContext
{
    /// <summary>
    /// 计费用途
    /// </summary>
    public SubscriptionBillingPurpose Purpose { get; set; }

    /// <summary>
    /// 订阅ID（可空，按 SubscriptionNo 关联时为空）
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// 订阅流水号（= 支付 BusinessOrderNo，规范关联键）
    /// </summary>
    public string SubscriptionNo { get; set; } = string.Empty;

    /// <summary>
    /// 关联的订阅变更ID（proration 时使用）
    /// </summary>
    public Guid? ChangeId { get; set; }

    /// <summary>
    /// 付款人（<c>Payment.UserId</c>）。状态机只认订阅主自己的支付：元数据是路由键不是凭据，
    /// 缺了这一项等于任何人拿自己的支付单加一段自填的 ExtraData 就能推进别人的订阅。
    /// </summary>
    public Guid? PayerUserId { get; set; }

    /// <summary>
    /// 这笔支付记在哪个计费账户上。状态机只认记在订阅所属账户上的支付（见 <c>VerifyPaymentBelongsToSubscription</c>）。
    /// </summary>
    public BillingAccountRef? PayerAccount { get; set; }

    /// <summary>
    /// 这笔付款按要求留存下来的支付方式（首付 / 补缴时有值），回流时换成这条订阅续费用的卡。
    /// </summary>
    public Guid? SavedPaymentMethodId { get; set; }

    /// <summary>
    /// 支付交易流水号
    /// </summary>
    public string? PaymentTradeNo { get; set; }

    /// <summary>
    /// 支付金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// 失败原因（失败路径使用）
    /// </summary>
    public string? FailReason { get; set; }
}

/// <summary>
/// 当场补缴的结果：一张可付款的收银台单（或应收为 0 时的直接恢复）。
/// </summary>
public class SubscriptionPayNowResultDto
{
    /// <summary>订阅ID</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>补缴单流水号（<see cref="SettledWithoutPayment"/> 时为空）</summary>
    public string? TradeNo { get; set; }

    /// <summary>渠道收银参数：Stripe 是 PaymentIntent 的 client secret，交给 Stripe.js 的 <c>confirmPayment</c>（含 3-D Secure）</summary>
    public string? PayParams { get; set; }

    /// <summary>重定向式渠道的付款地址</summary>
    public string? PayUrl { get; set; }

    /// <summary>应付金额（含税）</summary>
    public decimal Amount { get; set; }

    /// <summary>其中的税额</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>补缴单的过期时间</summary>
    public DateTime? ExpireTime { get; set; }

    /// <summary>渠道代码（前端据此选收银组件）</summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>应收为 0：没有建单，订阅已直接恢复</summary>
    public bool SettledWithoutPayment { get; set; }
}
