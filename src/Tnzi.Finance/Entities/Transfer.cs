namespace Tnzi.Finance.Entities;

/// <summary>
/// 资金划转单（银行/现金账户间转账）
/// </summary>
/// <remarks>
/// 替代以 SourceType="BankTransfer" 手工凭证包装转账的做法：原生单据可追溯、可作废（冲销）。
/// 遵循单据范式：编号过账时分配（scope 独立）、Posted 不可变、作废 = 引擎冲销、
/// 乐观并发 409。双方科目须为可过账的资金叶子科目（CashFlowActivity = CashEquivalent）。
/// 同币种模式：<see cref="Currency"/>/<see cref="Amount"/> 为唯一金额，Target* 字段全空。
/// 跨币种模式（路线 C）：转出侧记 <see cref="Currency"/>/<see cref="Amount"/>，转入侧记
/// <see cref="TargetCurrency"/>/<see cref="TargetAmount"/>，过账产出三张单币凭证（转出币/转入币/
/// 本位币 residual FX），两侧资金行经换汇过渡科目在同工作单元内精确归零。
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

    /// <summary>转入侧币种（null = 同币种模式；非空且 != Currency = 跨币种模式）</summary>
    public string? TargetCurrency { get; set; }

    /// <summary>转入侧金额（交易币，跨币种模式必填）</summary>
    public decimal? TargetAmount { get; set; }

    /// <summary>转入侧捕获汇率（过账时定格）</summary>
    public decimal TargetExchangeRate { get; set; }

    /// <summary>转入侧金额（本位币，过账时定格）</summary>
    public decimal TargetBaseAmount { get; set; }

    /// <summary>过账凭证（同币种模式 = 唯一凭证；跨币种模式 = 转出币凭证）</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>转入币凭证（跨币种模式）</summary>
    public Guid? TargetJournalEntryId { get; set; }

    /// <summary>本位币 residual 汇兑损益凭证（跨币种模式，residual != 0 时）</summary>
    public Guid? FxJournalEntryId { get; set; }

    /// <summary>作废冲销凭证（同币种模式 = 唯一冲销凭证；跨币种模式冲销经 SourceType 反查全部凭证）</summary>
    public Guid? VoidJournalEntryId { get; set; }
}
