namespace Tnzi.Finance.Entities;

/// <summary>
/// 销售贷项单（A/R 贷项；GL 投影为发票的镜像，可核销到发票 — P2c）
/// </summary>
public class CreditMemo : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>单据编号（过账时分配）</summary>
    public string? Number { get; set; }

    /// <summary>状态</summary>
    public FinanceDocumentStatus Status { get; set; } = FinanceDocumentStatus.Draft;

    /// <summary>客户</summary>
    public Guid CustomerId { get; set; }

    /// <summary>客户导航</summary>
    public virtual Customer? Customer { get; set; }

    /// <summary>单据日期</summary>
    public DateTime DocDate { get; set; }

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

    /// <summary>已核销金额（作为结算源，P2c 维护）</summary>
    public decimal AppliedTotal { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>过账凭证</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>作废冲销凭证</summary>
    public Guid? VoidJournalEntryId { get; set; }

    /// <summary>单据行</summary>
    public virtual ICollection<CreditMemoLine> Lines { get; set; } = new List<CreditMemoLine>();
}
