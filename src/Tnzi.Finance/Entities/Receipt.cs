namespace Tnzi.Finance.Entities;

/// <summary>
/// 收据采集记录（上传 → 提取 → 人工修正 → 转换为费用/账单草稿）
/// </summary>
/// <remarks>
/// <see cref="FileId"/> 以 <c>[FileField]</c> 标注，交由框架文件引用追踪器管理生命周期（核心零 Storage 引用）。
/// 提取字段直列以便人工修正；<see cref="MatchedVendorId"/> 由 VendorName 精确 + 前缀匹配建议。
/// 转换止步草稿（委托既有 <c>CreateDraftAsync</c>），Converted 后不可删；并发双 convert 由 IConcurrencyStamp 挡 409。
/// </remarks>
public class Receipt : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>存储文件ID（框架自动引用追踪）</summary>
    [FileField]
    public Guid FileId { get; set; }

    /// <summary>原始文件名</summary>
    public string? OriginalFileName { get; set; }

    /// <summary>状态</summary>
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Uploaded;

    // ---- 提取字段（可人工修正）----
    public string? VendorName { get; set; }
    public DateTime? DocDate { get; set; }
    public string? Currency { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? Total { get; set; }
    public string? Reference { get; set; }

    /// <summary>行项 JSON（提取原样保留，供人工核对）</summary>
    public string? LineItemsJson { get; set; }

    /// <summary>提取置信度（0-1）</summary>
    public decimal? Confidence { get; set; }

    /// <summary>匹配到的供应商（VendorName 精确/前缀匹配建议）</summary>
    public Guid? MatchedVendorId { get; set; }

    /// <summary>转换目标单据类型（实体名）</summary>
    public string? ConvertedDocType { get; set; }

    /// <summary>转换目标单据ID</summary>
    public Guid? ConvertedDocId { get; set; }

    /// <summary>提取失败原因（可重试）</summary>
    public string? FailReason { get; set; }
}
