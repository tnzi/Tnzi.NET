namespace Tnzi.Finance.Dtos;

/// <summary>
/// 银行对账
/// </summary>
public class ReconciliationDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>对账科目名称（服务层补齐）</summary>
    public string? AccountName { get; set; }

    public DateTime StatementDate { get; set; }
    public decimal StatementEndingBalance { get; set; }
    public ReconciliationStatus Status { get; set; }
    public DateTime? CompletedTime { get; set; }
    public string? Note { get; set; }

    /// <summary>对账币种（派生：科目限定币种 ?? 本位币；期末余额与差额均以此币种计）</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>本对账勾选行数</summary>
    public int LineCount { get; set; }

    /// <summary>累计已勾选净额（该科目全部对账勾选行，本位币借方为正）</summary>
    public decimal ClearedBalance { get; set; }

    /// <summary>差额（对账单期末余额 - 累计已勾选净额；0 才能完成）</summary>
    public decimal Difference { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建/更新银行对账草稿
/// </summary>
public class CreateReconciliationDto
{
    public Guid AccountId { get; set; }
    public DateTime StatementDate { get; set; }
    public decimal StatementEndingBalance { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// 银行对账查询
/// </summary>
public class ReconciliationQueryDto : PagedQueryDto
{
    public Guid? AccountId { get; set; }
    public ReconciliationStatus? Status { get; set; }
}

/// <summary>
/// 对账候选/已勾选行（该科目的已过账总账行）
/// </summary>
public class ReconciliationCandidateLineDto
{
    public Guid JournalLineId { get; set; }
    public Guid JournalEntryId { get; set; }
    public string? EntryNumber { get; set; }
    public DateTime PostingDate { get; set; }
    public string? Memo { get; set; }

    /// <summary>借方金额（对账币种：本位币科目 = 本位币金额；外币限定科目 = 交易币金额）</summary>
    public decimal Debit { get; set; }

    /// <summary>贷方金额（对账币种：本位币科目 = 本位币金额；外币限定科目 = 交易币金额）</summary>
    public decimal Credit { get; set; }

    /// <summary>是否已被本对账勾选</summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// 是否被已导入的银行流水行持有（该行是某笔 Matched <see cref="Entities.BankTransaction"/> 的清算记录）。
    /// 为真时勾选不可取消——解除须走银行流水页的 unmatch（那里会原子地同时释放流水与勾选行）；
    /// 直接从工作区丢弃会让流水指向一条不存在的勾选行。呈现端据此禁用复选框
    /// </summary>
    public bool IsStatementMatched { get; set; }
}

/// <summary>
/// 对账勾选行工作区（已勾选 + 未勾选候选 + 实时差额）
/// </summary>
public class ReconciliationWorksheetDto
{
    public Guid ReconciliationId { get; set; }

    /// <summary>对账币种（派生：科目限定币种 ?? 本位币）</summary>
    public string Currency { get; set; } = string.Empty;

    public decimal StatementEndingBalance { get; set; }

    /// <summary>累计已勾选净额（含历史已完成对账 + 本对账当前勾选）</summary>
    public decimal ClearedBalance { get; set; }

    /// <summary>差额（0 才能完成）</summary>
    public decimal Difference { get; set; }

    /// <summary>候选与已勾选行（IsSelected 区分；候选 = 该科目未被任何对账勾选的已过账行）</summary>
    public List<ReconciliationCandidateLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 全量替换对账勾选行
/// </summary>
public class SetReconciliationLinesDto
{
    public List<Guid> JournalLineIds { get; set; } = null!;
}
