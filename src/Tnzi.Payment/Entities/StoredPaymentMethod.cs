namespace Tnzi.Payment.Entities;

/// <summary>
/// 用户已保存的支付方式（绑卡结果）。
/// 渠道侧真正持有卡片信息，这里只存可复用的引用（customer + payment method token）与展示快照，
/// 供后台 off-session 续费/试用转正/升级补差在无人值守时发起扣款。
/// </summary>
/// <remarks>
/// 做成用户级而非订阅级：同一用户可以有多条订阅（不同产品），绑一次卡即可全部复用。
/// 订阅上仍保留 token 快照，后台计费直读，不必每次 join。
/// </remarks>
public class StoredPaymentMethod : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 持有用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 支付渠道代码
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 渠道侧客户标识（如 Stripe Customer ID）
    /// </summary>
    public string? ProviderCustomerId { get; set; }

    /// <summary>
    /// 渠道侧支付方式标识（如 Stripe PaymentMethod ID）。绝不存卡号本身。
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 支付方式类型
    /// </summary>
    public PaymentMethod MethodType { get; set; } = PaymentMethod.CreditCard;

    /// <summary>
    /// 卡组织/钱包类型（展示用，如 visa / mastercard）
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>
    /// 卡号尾四位（展示用）
    /// </summary>
    public string? Last4 { get; set; }

    /// <summary>
    /// 钱包账户标识（展示用，已脱敏，如 PayPal 付款人邮箱 a***@example.com）。
    /// 钱包类支付方式没有卡号尾四位；同一用户绑了两个 PayPal 账户时，
    /// 不存这个就只能看到两行一模一样的"PayPal"，分不出该删哪一个。
    /// </summary>
    public string? AccountLabel { get; set; }

    /// <summary>
    /// 有效期月份
    /// </summary>
    public int? ExpiryMonth { get; set; }

    /// <summary>
    /// 有效期年份
    /// </summary>
    public int? ExpiryYear { get; set; }

    /// <summary>
    /// 是否为该用户在该渠道下的默认支付方式
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 是否仍可用（渠道侧解绑或卡片失效时置 false，不物理删除以保留历史扣款溯源）
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 最近一次成功扣款时间
    /// </summary>
    public DateTime? LastUsedTime { get; set; }

    /// <summary>
    /// 是否已过有效期（按年月判断，缺省信息时视为未过期）
    /// </summary>
    public bool IsExpired(DateTime utcNow)
    {
        if (ExpiryYear is not { } year || ExpiryMonth is not { } month)
            return false;

        if (month is < 1 or > 12)
            return false;

        // 卡片在有效期最后一个月的月末仍可用
        var expiresAfter = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return utcNow >= expiresAfter;
    }
}
