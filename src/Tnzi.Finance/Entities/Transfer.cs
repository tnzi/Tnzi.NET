namespace Tnzi.Finance.Entities;

/// <summary>
/// 资金划转单（银行/现金账户间转账）
/// </summary>
/// <remarks>
/// 替代以 SourceType="BankTransfer" 手工凭证包装转账的做法：原生单据可追溯、可作废（冲销）。
/// 遵循单据范式：编号过账时分配（scope 独立）、Posted 不可变、作废 = 引擎冲销、
/// 乐观并发 409。双方科目须为可过账的资金叶子科目（CashFlowActivity = CashEquivalent）。
/// 首版两科目须兼容同一交易币种（科目限定币种时须相等）；跨币种换汇划转
/// 与多币种校验一体设计，后续版本经汇率换算落地。
/// </remarks>
public class Transfer : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>单据编号（过账时分配）</summary>
    public string? Number { get; set; }

    /// <summary>状态（Draft/Posted/Voided）</summary>
    public FinanceDocumentStatus Status { get; set; } = FinanceDocumentStatus.Draft;

    /// <summary>转出科目</summary>
    public Guid FromAccountId { get; set; }

    /// <summary>转入科目</summary>
    public Guid ToAccountId { get; set; }

    /// <summary>划转日期</summary>
    public DateTime TransferDate { get; set; }

    /// <summary>交易币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>捕获汇率（过账时定格）</summary>
    public decimal ExchangeRate { get; set; }

    /// <summary>金额（交易币）</summary>
    public decimal Amount { get; set; }

    /// <summary>金额（本位币，过账时定格）</summary>
    public decimal BaseAmount { get; set; }

    /// <summary>外部参考号</summary>
    public string? Reference { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>过账凭证</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>作废冲销凭证</summary>
    public Guid? VoidJournalEntryId { get; set; }
}
