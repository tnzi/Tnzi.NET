namespace Tnzi.Finance.Dtos;

/// <summary>
/// 采购账单 DTO
/// </summary>
public class BillDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public FinanceDocumentStatus Status { get; set; }
    public Guid VendorId { get; set; }
    public string? VendorName { get; set; }
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
    public List<BillLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 采购账单行 DTO
/// </summary>
public class BillLineDto
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
/// 创建/更新采购账单草稿请求
/// </summary>
public class CreateBillDto
{
    public Guid VendorId { get; set; }
    public DateTime DocDate { get; set; }
    public DateTime? DueDate { get; set; }
    /// <summary>交易币种（null 表示本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>汇率（null 时过账按汇率表解析）</summary>
    public decimal? ExchangeRate { get; set; }

    public string? Memo { get; set; }
    public List<CreateBillLineDto> Lines { get; set; } = null!;
}

/// <summary>
/// 采购账单行请求
/// </summary>
public class CreateBillLineDto
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
/// 采购账单查询请求
/// </summary>
public class BillQueryDto : PagedQueryDto
{
    /// <summary>关键字（编号/摘要模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>按状态过滤</summary>
    public FinanceDocumentStatus? Status { get; set; }

    /// <summary>按供应商过滤</summary>
    public Guid? VendorId { get; set; }

    /// <summary>单据日期起</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>单据日期止</summary>
    public DateTime? DateTo { get; set; }
}
