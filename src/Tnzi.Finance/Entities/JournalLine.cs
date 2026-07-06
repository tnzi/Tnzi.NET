namespace Tnzi.Finance.Entities;

/// <summary>
/// 会计分录行（总账 GL 的唯一事实表）
/// </summary>
/// <remarks>
/// 所有财务报表只读取本表（过滤 <see cref="IsPosted"/> = true）。
/// <see cref="IsPosted"/>、<see cref="PostingDate"/>、<see cref="Currency"/>、
/// <see cref="ExchangeRate"/> 从凭证头冗余，使报表聚合无需 JOIN 凭证表。
/// 金额同时以本位币（<see cref="Debit"/>/<see cref="Credit"/>）与
/// 交易币（<see cref="TxnDebit"/>/<see cref="TxnCredit"/>）存储。
/// </remarks>
public class JournalLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>
    /// 所属凭证ID
    /// </summary>
    public Guid JournalEntryId { get; set; }

    /// <summary>
    /// 所属凭证
    /// </summary>
    public virtual JournalEntry? JournalEntry { get; set; }

    /// <summary>
    /// 行号（凭证内从 1 递增）
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// 科目ID
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// 科目
    /// </summary>
    public virtual Account? Account { get; set; }

    /// <summary>
    /// 借方金额（本位币）
    /// </summary>
    public decimal Debit { get; set; }

    /// <summary>
    /// 贷方金额（本位币）
    /// </summary>
    public decimal Credit { get; set; }

    /// <summary>
    /// 借方金额（交易币种）
    /// </summary>
    public decimal TxnDebit { get; set; }

    /// <summary>
    /// 贷方金额（交易币种）
    /// </summary>
    public decimal TxnCredit { get; set; }

    /// <summary>
    /// 交易币种（冗余自凭证头）
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// 汇率（冗余自凭证头）
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>
    /// 行摘要
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// 往来方类型（如 "Customer" / "Vendor" / "Employee"，消费应用自定义）
    /// </summary>
    public string? PartyType { get; set; }

    /// <summary>
    /// 往来方ID（字符串形式，兼容任意主键类型）
    /// </summary>
    public string? PartyId { get; set; }

    /// <summary>
    /// 维度标签（JSON 对象，如 {"project":"...","costCenter":"..."}，预留可配置维度）
    /// </summary>
    public string? Dimensions { get; set; }

    /// <summary>
    /// 是否已过账（冗余自凭证头；报表只统计已过账行）
    /// </summary>
    public bool IsPosted { get; set; }

    /// <summary>
    /// 过账日期（冗余自凭证头）
    /// </summary>
    public DateTime PostingDate { get; set; }

    /// <summary>
    /// 租户ID
    /// </summary>
    public Guid? TenantId { get; set; }
}
