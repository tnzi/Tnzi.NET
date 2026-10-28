namespace Tnzi.Payment.Services;

/// <summary>
/// 把一笔支付描述成发票 / 收据上的明细行：这笔钱买的是什么、覆盖哪一段服务期。
/// </summary>
/// <remarks>
/// <para>
/// 开票的模块（<c>Tnzi.Payment.Billing</c>）只认识支付，不认识支付背后的业务；业务模块从自己那侧实现它
/// （订阅模块给出「计划名 + 服务期」）。依赖方向恒为业务 → 支付 ← 开票，开票与业务两个模块互不引用。
/// </para>
/// <para>
/// 多个实现按注册顺序询问，第一个答得上来的胜出；返回 <see langword="null"/> = 这笔支付不归我管。
/// 都答不上来时开票方用支付自己的描述生成一行，与没有这个扩展点时相同。
/// </para>
/// </remarks>
public interface IPaymentLineDescriber
{
    /// <summary>这笔支付的明细行；不认识它时返回 <see langword="null"/>。</summary>
    Task<IReadOnlyList<PaymentLineDescription>?> DescribeAsync(PaymentLineContext payment, CancellationToken cancellationToken = default);
}

/// <summary>描述一笔支付所需的事实（取自支付行本身）。</summary>
public sealed class PaymentLineContext
{
    /// <summary>支付 Id。</summary>
    public Guid PaymentId { get; init; }

    /// <summary>业务类型。</summary>
    public BusinessType BusinessType { get; init; }

    /// <summary>业务单号。</summary>
    public string BusinessOrderNo { get; init; } = string.Empty;

    /// <summary>建单时的描述。</summary>
    public string? Description { get; init; }

    /// <summary>业务方写进支付的扩展数据（订阅计费元数据就在这里）。</summary>
    public string? ExtraData { get; init; }

    /// <summary>折扣前金额。</summary>
    public decimal OriginalAmount { get; init; }

    /// <summary>折扣。</summary>
    public decimal DiscountAmount { get; init; }

    /// <summary>税额。</summary>
    public decimal TaxAmount { get; init; }

    /// <summary>币种。</summary>
    public string Currency { get; init; } = string.Empty;
}

/// <summary>一条明细行。</summary>
public sealed class PaymentLineDescription
{
    /// <summary>描述（计划名之类）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>数量。</summary>
    public decimal Quantity { get; init; } = 1;

    /// <summary>单价（折扣前、税前）。</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>行金额（折扣前、税前）。</summary>
    public decimal Amount { get; init; }

    /// <summary>行折扣。</summary>
    public decimal DiscountAmount { get; init; }

    /// <summary>行税额。</summary>
    public decimal TaxAmount { get; init; }

    /// <summary>服务期开始（含）。没有服务期概念时为空。</summary>
    public DateTime? PeriodStart { get; init; }

    /// <summary>服务期结束（不含）。</summary>
    public DateTime? PeriodEnd { get; init; }

    /// <summary>产品代码。</summary>
    public string? ProductCode { get; init; }

    /// <summary>
    /// 这一行是哪一类（描述方自己定义的短标记，例如订阅模块的超额存储行 <c>storage-overage</c>），供界面单独认出它；普通行为空。
    /// 最长 <see cref="KindMaxLength"/> 个字符。
    /// </summary>
    public string? Kind { get; init; }

    /// <summary><see cref="Kind"/> 的最大长度。</summary>
    public const int KindMaxLength = 32;
}
