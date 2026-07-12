namespace Tnzi.Finance.Dtos;

/// <summary>
/// 收付款单 DTO
/// </summary>
public class PaymentEntryDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public FinanceDocumentStatus Status { get; set; }
    public PaymentDirection Direction { get; set; }
    public FinancePartyType PartyType { get; set; }
    public Guid PartyId { get; set; }
    public string? PartyName { get; set; }
    public DateTime DocDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal Amount { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal AppliedTotal { get; set; }
    public Guid? DepositToAccountId { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Reference { get; set; }
    public string? Memo { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? VoidJournalEntryId { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建/更新收付款单草稿请求
/// </summary>
public class CreatePaymentEntryDto
{
    public PaymentDirection Direction { get; set; }
    public FinancePartyType PartyType { get; set; }
    public Guid PartyId { get; set; }
    public DateTime DocDate { get; set; }

    /// <summary>交易币种（null 表示本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>汇率（null 时过账按汇率表解析）</summary>
    public decimal? ExchangeRate { get; set; }

    public decimal Amount { get; set; }

    /// <summary>存入/付出科目（Inbound 可空回退待存款项，见 FinanceOptions.PostToUndepositedFunds）</summary>
    public Guid? DepositToAccountId { get; set; }

    /// <summary>结算方式（推荐取值见 PaymentMethods 常量，可自定义）</summary>
    public string? PaymentMethod { get; set; }

    public string? Reference { get; set; }
    public string? Memo { get; set; }
}

/// <summary>
/// 收付款单查询请求
/// </summary>
public class PaymentEntryQueryDto : PagedQueryDto
{
    /// <summary>关键字（编号/参考号/摘要模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>按状态过滤</summary>
    public FinanceDocumentStatus? Status { get; set; }

    /// <summary>按方向过滤</summary>
    public PaymentDirection? Direction { get; set; }

    /// <summary>按结算方式过滤</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>按往来方过滤</summary>
    public Guid? PartyId { get; set; }

    /// <summary>单据日期起</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>单据日期止</summary>
    public DateTime? DateTo { get; set; }
}
