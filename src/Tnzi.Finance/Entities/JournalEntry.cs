namespace Tnzi.Finance.Entities;

/// <summary>
/// 会计凭证（日记账分录头）
/// </summary>
/// <remarks>
/// 生命周期：Draft（可编辑/可删除）→ Posted（不可变）→ Reversed（被冲销凭证抵消）。
/// 凭证编号在过账时按租户连续分配（草稿不占号）。
/// 已过账凭证及其分录行永不修改、永不删除，修正一律通过冲销。
/// 实现 <see cref="IConcurrencyStamp"/>：状态迁移（过账/冲销/编辑草稿）依赖乐观并发控制，
/// 并发冲突方的整个事务回滚（连同其冲销凭证插入与已分配的凭证号），防止双过账/双冲销破坏账本。
/// </remarks>
public class JournalEntry : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>
    /// 并发标记（框架自动轮换；乐观并发控制）
    /// </summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>
    /// 凭证编号（过账时分配，草稿为 null）
    /// </summary>
    public string? Number { get; set; }

    /// <summary>
    /// 凭证状态
    /// </summary>
    public JournalEntryStatus Status { get; set; }

    /// <summary>
    /// 过账日期（记账日期，决定所属会计期间）
    /// </summary>
    public DateTime PostingDate { get; set; }

    /// <summary>
    /// 摘要
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// 交易币种（ISO 4217）
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// 交易币种对本位币的汇率（本位币凭证为 1）
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>
    /// 来源单据类型（多态引用，如 "Manual"、"Payment.Invoice" 或消费应用自定义类型）
    /// </summary>
    public string? SourceType { get; set; }

    /// <summary>
    /// 来源单据ID（字符串形式，兼容 Guid/long/string 主键）
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// 借方合计（本位币，过账时冗余）
    /// </summary>
    public decimal TotalDebit { get; set; }

    /// <summary>
    /// 贷方合计（本位币，过账时冗余）
    /// </summary>
    public decimal TotalCredit { get; set; }

    /// <summary>
    /// 过账时间
    /// </summary>
    public DateTime? PostedTime { get; set; }

    /// <summary>
    /// 过账人ID
    /// </summary>
    public Guid? PostedById { get; set; }

    /// <summary>
    /// 本凭证冲销的原凭证ID（仅冲销凭证有值）
    /// </summary>
    public Guid? ReversalOfEntryId { get; set; }

    /// <summary>
    /// 冲销本凭证的冲销凭证ID（仅被冲销的原凭证有值）
    /// </summary>
    public Guid? ReversedByEntryId { get; set; }

    /// <summary>
    /// 分录行集合
    /// </summary>
    public virtual ICollection<JournalLine> Lines { get; set; } = new List<JournalLine>();
}
