namespace Tnzi.Finance.Entities;

/// <summary>
/// 银行流水行（从对账单/银行 feed 导入的单条交易）
/// </summary>
/// <remarks>
/// 匹配引擎的输入：与该科目已过账、未 cleared 的总账行按金额/日期/参考号匹配。
/// 确认匹配即在当前 Draft 对账下生成 <see cref="ReconciliationLine"/> 并回写
/// <see cref="ReconciliationLineId"/>/<see cref="MatchedJournalLineId"/>。
/// 去重键 <see cref="ExternalId"/>（OFX FITID / provider id / CSV hash）在
/// (租户, 科目, ExternalId) 上唯一，逐行去重不整批失败，并发由唯一索引兜底。
/// <see cref="Amount"/> 带符号：正 = 存入 = GL 借方，负 = 支出 = GL 贷方。
/// </remarks>
public class BankTransaction : MultiTenantAuditedEntity<Guid>
{
    /// <summary>对应银行账户档案挂载的资金科目</summary>
    public Guid AccountId { get; set; }

    /// <summary>所属导入批次</summary>
    public Guid ImportBatchId { get; set; }

    /// <summary>交易日期</summary>
    public DateTime TxnDate { get; set; }

    /// <summary>金额（带符号：正 = 存入 = GL 借方）</summary>
    public decimal Amount { get; set; }

    /// <summary>交易币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>摘要</summary>
    public string? Description { get; set; }

    /// <summary>收/付款方</summary>
    public string? Payee { get; set; }

    /// <summary>参考号（支票号/交易参考）</summary>
    public string? Reference { get; set; }

    /// <summary>去重键（OFX FITID / provider id / CSV hash）</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>来源</summary>
    public BankTransactionSource Source { get; set; }

    /// <summary>匹配状态</summary>
    public BankTransactionStatus Status { get; set; } = BankTransactionStatus.Pending;

    /// <summary>匹配到的总账行</summary>
    public Guid? MatchedJournalLineId { get; set; }

    /// <summary>生成的对账勾选行</summary>
    public Guid? ReconciliationLineId { get; set; }

    /// <summary>引擎建议的总账行（未确认）</summary>
    public Guid? SuggestedJournalLineId { get; set; }

    /// <summary>匹配置信度（1.0 精确参考 / 0.8 金额+日期窗口）</summary>
    public decimal? MatchConfidence { get; set; }

    /// <summary>命中规则标识</summary>
    public string? MatchRule { get; set; }

    /// <summary>交易后余额（对账单提供时）</summary>
    public decimal? BalanceAfter { get; set; }

    /// <summary>由本行创建的单据类型</summary>
    public string? CreatedDocType { get; set; }

    /// <summary>由本行创建的单据 ID</summary>
    public Guid? CreatedDocId { get; set; }
}
