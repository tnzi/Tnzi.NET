namespace Tnzi.Finance.Dtos;

/// <summary>
/// 销售发票 DTO
/// </summary>
public class InvoiceDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public FinanceDocumentStatus Status { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime DocDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public decimal BaseTotal { get; set; }
    public decimal AppliedTotal { get; set; }
    public string? Memo { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? VoidJournalEntryId { get; set; }
    public DateTime CreationTime { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 销售发票行 DTO
/// </summary>
public class InvoiceLineDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public Guid? ItemId { get; set; }
    public string? Description { get; set; }
    public Guid? AccountId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public Guid? TaxCodeId { get; set; }
}

/// <summary>
/// 创建/更新销售发票草稿请求
/// </summary>
public class CreateInvoiceDto
{
    public Guid CustomerId { get; set; }
    public DateTime DocDate { get; set; }
    public DateTime? DueDate { get; set; }
    /// <summary>交易币种（null 表示本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>汇率（null 时过账按汇率表解析）</summary>
    public decimal? ExchangeRate { get; set; }

    public string? Memo { get; set; }
    public List<CreateInvoiceLineDto> Lines { get; set; } = null!;
}

/// <summary>
/// 销售发票行请求
/// </summary>
public class CreateInvoiceLineDto
{
    public Guid? ItemId { get; set; }
    public string? Description { get; set; }

    /// <summary>收入/费用科目覆盖（null 回退目录项默认科目）</summary>
    public Guid? AccountId { get; set; }

    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public Guid? TaxCodeId { get; set; }
}

/// <summary>
/// 销售发票查询请求
/// </summary>
public class InvoiceQueryDto : PagedQueryDto
{
    /// <summary>关键字（编号/摘要模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>按状态过滤</summary>
    public FinanceDocumentStatus? Status { get; set; }

    /// <summary>按客户过滤</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>单据日期起</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>单据日期止</summary>
    public DateTime? DateTo { get; set; }
}
