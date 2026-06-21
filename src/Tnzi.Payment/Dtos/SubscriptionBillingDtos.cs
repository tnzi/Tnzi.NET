namespace Tnzi.Payment.Dtos;

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
