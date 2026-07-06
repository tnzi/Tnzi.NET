namespace Tnzi.Finance.Dtos;

/// <summary>
/// 汇率 DTO
/// </summary>
public class ExchangeRateDto
{
    public Guid Id { get; set; }
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime RateDate { get; set; }
    public string? Source { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 录入/更新汇率请求（按 币种对 + 日期 幂等更新）
/// </summary>
public class UpsertExchangeRateDto
{
    public string FromCurrency { get; set; } = null!;
    public string ToCurrency { get; set; } = null!;
    public decimal Rate { get; set; }
    public DateTime RateDate { get; set; }
    public string? Source { get; set; }
}

/// <summary>
/// 汇率查询请求
/// </summary>
public class ExchangeRateQueryDto : PagedQueryDto
{
    public string? FromCurrency { get; set; }
    public string? ToCurrency { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}
