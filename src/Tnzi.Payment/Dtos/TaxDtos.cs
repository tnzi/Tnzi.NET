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
}
