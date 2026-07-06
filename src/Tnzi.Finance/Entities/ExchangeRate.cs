namespace Tnzi.Finance.Entities;

/// <summary>
/// 汇率（按日期版本化）
/// </summary>
/// <remarks>
/// 查询语义：取"不晚于目标日期的最近一条"生效汇率；
/// 无直接汇率时尝试反向汇率取倒数。
/// </remarks>
public class ExchangeRate : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 源币种（ISO 4217）
    /// </summary>
    public string FromCurrency { get; set; } = string.Empty;

    /// <summary>
    /// 目标币种（ISO 4217）
    /// </summary>
    public string ToCurrency { get; set; } = string.Empty;

    /// <summary>
    /// 汇率（1 源币种 = Rate 目标币种）
    /// </summary>
    public decimal Rate { get; set; }

    /// <summary>
    /// 生效日期
    /// </summary>
    public DateTime RateDate { get; set; }

    /// <summary>
    /// 来源（如 "Manual" 或外部提供者名称）
    /// </summary>
    public string? Source { get; set; }
}
