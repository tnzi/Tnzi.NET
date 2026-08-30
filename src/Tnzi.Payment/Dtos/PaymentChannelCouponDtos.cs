namespace Tnzi.Payment.Dtos;

/// <summary>
/// 待同步到支付渠道的优惠券快照（<see cref="Providers.IPaymentChannelCouponSync"/> 的输入）。
/// </summary>
/// <remarks>
/// <para>
/// 刻意不直接传 <c>Promotion</c> 实体：渠道包不该拿到一个可写、带导航属性、还挂着 EF 跟踪状态的
/// 聚合根，它只需要这几项事实。快照同时把「同步的是哪一刻的促销」固定下来。
/// </para>
/// <para>
/// <see cref="Currency"/> 始终取促销自身的币种而不是渠道默认币种：固定金额折扣与币种强相关，
/// 写死一个币种会让 10 EUR 的券在渠道侧变成 10 USD。
/// </para>
/// </remarks>
public class PaymentChannelCouponDto
{
    /// <summary>本地促销 ID，随同步写进渠道侧的元数据，便于两边对账。</summary>
    public Guid PromotionId { get; set; }

    /// <summary>促销代码，同时用作渠道侧优惠券的标识。</summary>
    public string PromotionCode { get; set; } = string.Empty;

    /// <summary>促销名称（渠道侧的展示名）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>折扣类型：按百分比还是按固定金额。</summary>
    public DiscountType DiscountType { get; set; }

    /// <summary>折扣数值：百分比折扣时是百分数，固定金额折扣时是 <see cref="Currency"/> 计价的金额。</summary>
    public decimal DiscountValue { get; set; }

    /// <summary>固定金额折扣的币种（ISO 4217）。</summary>
    public string Currency { get; set; } = PaymentConstants.DefaultCurrency;

    /// <summary>可兑换截止时间；为 null 表示不限。</summary>
    public DateTime? RedeemBy { get; set; }

    /// <summary>总兑换次数上限；为 null 表示不限。</summary>
    public int? MaxRedemptions { get; set; }
}
