namespace Tnzi.Finance.Entities;

/// <summary>
/// 采购账单（A/P 单据；单据范式同 <see cref="Invoice"/>）
/// </summary>
public class Bill : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>单据编号（过账时分配）</summary>
    public string? Number { get; set; }

    /// <summary>状态</summary>
    public FinanceDocumentStatus Status { get; set; } = FinanceDocumentStatus.Draft;

    /// <summary>供应商</summary>
    public Guid VendorId { get; set; }

    /// <summary>供应商导航</summary>
    public virtual Vendor? Vendor { get; set; }

    /// <summary>单据日期</summary>
    public DateTime DocDate { get; set; }

    /// <summary>到期日</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>交易币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>捕获汇率</summary>
    public decimal ExchangeRate { get; set; }

    /// <summary>行小计（交易币）</summary>
    public decimal SubTotal { get; set; }

    /// <summary>税额合计（交易币）</summary>
    public decimal TaxTotal { get; set; }

    /// <summary>价税合计（交易币）</summary>
    public decimal Total { get; set; }

    /// <summary>价税合计（本位币）</summary>
    public decimal BaseTotal { get; set; }

    /// <summary>已核销金额（P2c 结算维护）</summary>
    public decimal AppliedTotal { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>过账凭证</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>作废冲销凭证</summary>
    public Guid? VoidJournalEntryId { get; set; }

    /// <summary>单据行</summary>
    public virtual ICollection<BillLine> Lines { get; set; } = new List<BillLine>();
}
