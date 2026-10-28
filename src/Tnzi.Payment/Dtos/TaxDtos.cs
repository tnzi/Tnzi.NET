namespace Tnzi.Payment.Dtos;

/// <summary>
/// 税额计算请求
/// </summary>
public class TaxCalculationRequest
{
    /// <summary>
    /// 计税基数：已扣减折扣后的净额
    /// </summary>
    public decimal NetAmount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = PaymentConstants.DefaultCurrency;

    /// <summary>
    /// 业务类型（不同业务可能适用不同税目）
    /// </summary>
    public BusinessType BusinessType { get; set; }

    /// <summary>
    /// 客户所在国家/地区代码（ISO 3166-1 alpha-2）
    /// </summary>
    public string? CountryCode { get; set; }

    /// <summary>
    /// 客户所在省/州代码
    /// </summary>
    public string? RegionCode { get; set; }

    /// <summary>
    /// 客户税号（B2B 反向征收判定用）
    /// </summary>
    public string? CustomerTaxId { get; set; }
}

/// <summary>
/// 税额计算结果
/// </summary>
public class TaxCalculationResult
{
    /// <summary>
    /// 税额
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// 应付总额：价外税 = 净额 + 税额；价内税 = 净额（税已含在内）
    /// </summary>
    public decimal PayableAmount { get; set; }

    /// <summary>
    /// 适用税率（百分数，如 13 表示 13%）
    /// </summary>
    public decimal TaxRate { get; set; }

    /// <summary>
    /// 税额是否已包含在价格中
    /// </summary>
    public bool TaxIncluded { get; set; }

    /// <summary>
    /// 按税种拆开的税额（收据与发票上逐行列示：「HST 13%」带登记号）。内置计算器给一行；
    /// 自定义计算器可以给多行（GST + PST），各行金额之和应等于 <see cref="TaxAmount"/>。不计税时为空。
    /// </summary>
    public List<TaxLineDto> TaxLines { get; set; } = [];
}

/// <summary>
/// 一个税种的税额。随支付存下（<c>Payment.TaxLinesJson</c>），开票时原样抄到发票上。
/// </summary>
public class TaxLineDto
{
    /// <summary>税种名（<c>HST</c> / <c>GST</c> / <c>VAT</c>）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>税率（百分数，13 表示 13%）。</summary>
    public decimal Rate { get; set; }

    /// <summary>税额。</summary>
    public decimal Amount { get; set; }

    /// <summary>卖方在这个税种下的登记号（加拿大的 GST/HST 号），印在收据上；可为空。</summary>
    public string? RegistrationNumber { get; set; }
}
