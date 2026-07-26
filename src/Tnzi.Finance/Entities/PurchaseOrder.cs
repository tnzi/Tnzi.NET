namespace Tnzi.Finance.Entities;

/// <summary>
/// 采购订单（Purchase Order）——发给供应商的**不过账**单据
/// </summary>
/// <remarks>
/// <see cref="Estimate"/> 的镜像：报价是我方对客户的承诺，采购订单是我方对供应商
/// 的承诺。同样不投影总账（下单不是费用，也不是应付），同样在"发出"时分配编号，
/// 同样转换成一张**草稿**账单而不是直接过账。
///
/// <see cref="FinanceOfferStatus.Accepted"/> 在这一侧读作"供应商确认了订单"。
/// </remarks>
public class PurchaseOrder : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>单据编号（发出时分配，可空唯一过滤索引）</summary>
    public string? Number { get; set; }

    /// <summary>状态</summary>
    public FinanceOfferStatus Status { get; set; } = FinanceOfferStatus.Draft;

    /// <summary>供应商</summary>
    public Guid VendorId { get; set; }

    /// <summary>供应商导航</summary>
    public virtual Vendor? Vendor { get; set; }

    /// <summary>单据日期（date-only，UTC 午夜）</summary>
    public DateTime DocDate { get; set; }

    /// <summary>期望交付日（date-only）</summary>
    public DateTime? ExpectedDate { get; set; }

    /// <summary>交易币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>行小计（交易币，不含税）</summary>
    public decimal SubTotal { get; set; }

    /// <summary>税额合计（交易币）</summary>
    public decimal TaxTotal { get; set; }

    /// <summary>价税合计（交易币）</summary>
    public decimal Total { get; set; }

    /// <summary>供应商可见的摘要</summary>
    public string? Memo { get; set; }

    /// <summary>内部备注（不随单据发给供应商）</summary>
    public string? InternalNote { get; set; }

    /// <summary>送货地址（自由文本；结构化地址是消费应用的扩展点，框架不臆造字段）</summary>
    public string? ShipTo { get; set; }

    /// <summary>转换目标单据类型（wire 令牌，见 FinanceSourceTypes）</summary>
    public string? ConvertedToDocType { get; set; }

    /// <summary>转换目标单据 Id</summary>
    public Guid? ConvertedToDocId { get; set; }

    /// <summary>单据行</summary>
    public virtual ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
}
