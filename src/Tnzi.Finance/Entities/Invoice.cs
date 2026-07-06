namespace Tnzi.Finance.Entities;

/// <summary>
/// 销售发票（A/R 单据）
/// </summary>
/// <remarks>
/// 单据范式：编号过账时分配（草稿不占号）；Draft 可编辑删除，Posted 不可变，
/// 作废 = 冲销过账凭证 + Voided；结算状态（PartiallyPaid/Paid）由 P2c 的
/// PaymentApplication 派生（AppliedTotal 冗余）。实现 <see cref="IConcurrencyStamp"/>
/// 防并发双过账/双作废。
/// </remarks>
public class Invoice : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>单据编号（过账时分配，可空唯一过滤索引）</summary>
    public string? Number { get; set; }

    /// <summary>状态</summary>
    public FinanceDocumentStatus Status { get; set; } = FinanceDocumentStatus.Draft;

    /// <summary>客户</summary>
    public Guid CustomerId { get; set; }

    /// <summary>客户导航</summary>
    public virtual Customer? Customer { get; set; }

    /// <summary>单据日期（date-only，UTC 午夜）</summary>
    public DateTime DocDate { get; set; }

    /// <summary>到期日（null 时过账按账期推算）</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>交易币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>捕获汇率（过账时定格；本位币单据为 1）</summary>
    public decimal ExchangeRate { get; set; }

    /// <summary>行小计（交易币，不含税）</summary>
    public decimal SubTotal { get; set; }

    /// <summary>税额合计（交易币）</summary>
    public decimal TaxTotal { get; set; }

    /// <summary>价税合计（交易币）</summary>
    public decimal Total { get; set; }

    /// <summary>价税合计（本位币，过账时定格）</summary>
    public decimal BaseTotal { get; set; }

    /// <summary>已核销金额（交易币，P2c 结算维护）</summary>
    public decimal AppliedTotal { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>过账凭证</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>作废冲销凭证</summary>
    public Guid? VoidJournalEntryId { get; set; }

    /// <summary>单据行</summary>
    public virtual ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}
