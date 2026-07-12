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
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    /// <summary>是否已被本对账勾选</summary>
    public bool IsSelected { get; set; }
}

/// <summary>
/// 对账勾选行工作区（已勾选 + 未勾选候选 + 实时差额）
/// </summary>
public class ReconciliationWorksheetDto
{
    public Guid ReconciliationId { get; set; }
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
