namespace Tnzi.Finance.Banking.Dtos;

/// <summary>
/// 收据采集 DTO
/// </summary>
public class ReceiptDto
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }
    public string? OriginalFileName { get; set; }
    public ReceiptStatus Status { get; set; }
    public string? VendorName { get; set; }
    public DateTime? DocDate { get; set; }
    public string? Currency { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? Total { get; set; }
    public string? Reference { get; set; }
    public string? LineItemsJson { get; set; }
    public decimal? Confidence { get; set; }
    public Guid? MatchedVendorId { get; set; }
    public string? MatchedVendorName { get; set; }
    public string? ConvertedDocType { get; set; }
    public Guid? ConvertedDocId { get; set; }
    public string? FailReason { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建收据采集记录（上传后拿 fileId 登记）
/// </summary>
public class CreateReceiptDto
{
    public Guid FileId { get; set; }
    public string? FileName { get; set; }

    /// <summary>币种提示（可选）</summary>
    public string? Currency { get; set; }
}

/// <summary>
/// 人工修正提取字段
/// </summary>
public class UpdateReceiptExtractionDto
{
    public string? VendorName { get; set; }
    public DateTime? DocDate { get; set; }
    public string? Currency { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? Total { get; set; }
    public string? Reference { get; set; }

    /// <summary>指定供应商（覆盖匹配建议）</summary>
    public Guid? MatchedVendorId { get; set; }
}

/// <summary>
/// 转换收据为单据草稿
/// </summary>
public class ConvertReceiptDto
{
    /// <summary>目标单据类型（Expense | Bill）</summary>
    public ReceiptDocType DocType { get; set; }

    /// <summary>供应商（缺省用 MatchedVendorId；仍无则 400）</summary>
    public Guid? VendorId { get; set; }

    /// <summary>费用/成本科目（单行草稿的科目）</summary>
    public Guid? AccountId { get; set; }

    /// <summary>付款科目（仅费用支出转换需要）</summary>
    public Guid? PaidFromAccountId { get; set; }
}

/// <summary>
/// 收据转换结果
/// </summary>
public class ReceiptConvertResultDto
{
    public string DocType { get; set; } = string.Empty;
    public Guid DocId { get; set; }
}

/// <summary>
/// 收据查询
/// </summary>
public class ReceiptQueryDto : PagedQueryDto
{
    public ReceiptStatus? Status { get; set; }

    /// <summary>关键字（供应商/文件名/参考号模糊匹配）</summary>
    public string? Keyword { get; set; }
}
