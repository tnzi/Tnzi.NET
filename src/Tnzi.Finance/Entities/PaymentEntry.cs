namespace Tnzi.Finance.Entities;

/// <summary>
/// 收付款单（Inbound = 客户收款，Outbound = 供应商付款）
/// </summary>
/// <remarks>
/// 过账进 AR/AP 控制科目与银行/待存款项科目；与单据的核销（PaymentApplication）
/// 独立于 GL（P2c）。<see cref="SourceType"/> + <see cref="SourceId"/> 供外部收款
/// 摄取幂等回链（唯一过滤索引防重复摄取）。
/// </remarks>
public class PaymentEntry : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>单据编号（过账时分配）</summary>
    public string? Number { get; set; }

    /// <summary>状态（Draft/Posted/Voided；核销程度由 AppliedTotal 派生）</summary>
    public FinanceDocumentStatus Status { get; set; } = FinanceDocumentStatus.Draft;

    /// <summary>方向</summary>
    public PaymentDirection Direction { get; set; }

    /// <summary>往来方类型</summary>
    public FinancePartyType PartyType { get; set; }

    /// <summary>往来方ID（客户或供应商）</summary>
    public Guid PartyId { get; set; }

    /// <summary>单据日期</summary>
    public DateTime DocDate { get; set; }

    /// <summary>交易币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>捕获汇率</summary>
    public decimal ExchangeRate { get; set; }

    /// <summary>金额（交易币）</summary>
    public decimal Amount { get; set; }

    /// <summary>金额（本位币，过账时定格）</summary>
    public decimal BaseAmount { get; set; }

    /// <summary>已核销金额（交易币，P2c 结算维护）</summary>
    public decimal AppliedTotal { get; set; }

    /// <summary>
    /// 存入/付出科目（Inbound：银行或待存款项；Outbound：付款来源银行）。
    /// null 时 Inbound 按 <see cref="Options.FinanceOptions.PostToUndepositedFunds"/> 回退角色解析
    /// </summary>
    public Guid? DepositToAccountId { get; set; }

    /// <summary>外部参考号（支票号/交易号）</summary>
    public string? Reference { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>外部来源类型（如 "Payment.Order"，幂等摄取）</summary>
    public string? SourceType { get; set; }

    /// <summary>外部来源ID</summary>
    public string? SourceId { get; set; }

    /// <summary>过账凭证</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>作废冲销凭证</summary>
    public Guid? VoidJournalEntryId { get; set; }
}
