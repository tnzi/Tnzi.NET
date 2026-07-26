namespace Tnzi.Finance.Dtos;

/// <summary>
/// 报价单 DTO
/// </summary>
public class EstimateDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public FinanceOfferStatus Status { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public DateTime DocDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public string? Memo { get; set; }
    public string? InternalNote { get; set; }

    /// <summary>转换目标单据类型（wire 令牌，见 FinanceSourceTypes）</summary>
    public string? ConvertedToDocType { get; set; }

    /// <summary>转换目标单据 Id（呈现端据此提供"打开那张发票"的钻取）</summary>
    public Guid? ConvertedToDocId { get; set; }

    public DateTime CreationTime { get; set; }
    public List<OfferLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 采购订单 DTO
/// </summary>
public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public FinanceOfferStatus Status { get; set; }
    public Guid VendorId { get; set; }
    public string? VendorName { get; set; }
    public DateTime DocDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public string? Memo { get; set; }
    public string? InternalNote { get; set; }
    public string? ShipTo { get; set; }
    public string? ConvertedToDocType { get; set; }
    public Guid? ConvertedToDocId { get; set; }
    public DateTime CreationTime { get; set; }
    public List<OfferLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 报价单 / 采购订单行 DTO（两侧行结构一致，共用一个形状）
/// </summary>
public class OfferLineDto
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
/// 创建/更新报价单请求
/// </summary>
public class CreateEstimateDto
{
    public Guid CustomerId { get; set; }
    public DateTime DocDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    /// <summary>交易币种（null 表示本位币）</summary>
    public string? Currency { get; set; }

    public string? Memo { get; set; }
    public string? InternalNote { get; set; }
    public List<CreateOfferLineDto> Lines { get; set; } = null!;
}

/// <summary>
/// 创建/更新采购订单请求
/// </summary>
public class CreatePurchaseOrderDto
{
    public Guid VendorId { get; set; }
    public DateTime DocDate { get; set; }
    public DateTime? ExpectedDate { get; set; }

    /// <summary>交易币种（null 表示本位币）</summary>
    public string? Currency { get; set; }

    public string? Memo { get; set; }
    public string? InternalNote { get; set; }
    public string? ShipTo { get; set; }
    public List<CreateOfferLineDto> Lines { get; set; } = null!;
}

/// <summary>
/// 报价单 / 采购订单行请求
/// </summary>
public class CreateOfferLineDto
{
    public Guid? ItemId { get; set; }
    public string? Description { get; set; }

    /// <summary>科目覆盖（null 回退目录项默认科目；转换时原样带到目标单据）</summary>
    public Guid? AccountId { get; set; }

    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public Guid? TaxCodeId { get; set; }
}

/// <summary>
/// 转换请求（报价单 → 发票 / 采购订单 → 账单）
/// </summary>
public class ConvertOfferDto
{
    /// <summary>目标单据日期（null = 今天）</summary>
    public DateTime? DocDate { get; set; }

    /// <summary>目标单据到期日（null 时由目标服务按账期推算）</summary>
    public DateTime? DueDate { get; set; }
}

/// <summary>
/// 转换结果
/// </summary>
public class ConvertOfferResultDto
{
    /// <summary>来源单据 Id</summary>
    public Guid SourceId { get; set; }

    /// <summary>来源单据编号</summary>
    public string? SourceNumber { get; set; }

    /// <summary>目标单据类型（wire 令牌）</summary>
    public string DocType { get; set; } = string.Empty;

    /// <summary>目标单据 Id（草稿，是否过账由人决定）</summary>
    public Guid DocId { get; set; }
}

/// <summary>
/// 报价单查询请求
/// </summary>
public class EstimateQueryDto : PagedQueryDto
{
    /// <summary>关键字（编号/摘要模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>按状态过滤</summary>
    public FinanceOfferStatus? Status { get; set; }

    /// <summary>按客户过滤</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>单据日期起</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>单据日期止</summary>
    public DateTime? DateTo { get; set; }

    /// <summary>只看仍在流转中的（草稿 / 已发出 / 已接受）——待办视图</summary>
    public bool? OpenOnly { get; set; }
}

/// <summary>
/// 采购订单查询请求
/// </summary>
public class PurchaseOrderQueryDto : PagedQueryDto
{
    /// <summary>关键字（编号/摘要模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>按状态过滤</summary>
    public FinanceOfferStatus? Status { get; set; }

    /// <summary>按供应商过滤</summary>
    public Guid? VendorId { get; set; }

    /// <summary>单据日期起</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>单据日期止</summary>
    public DateTime? DateTo { get; set; }

    /// <summary>只看仍在流转中的（草稿 / 已发出 / 已确认）</summary>
    public bool? OpenOnly { get; set; }
}
