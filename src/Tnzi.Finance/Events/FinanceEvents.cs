namespace Tnzi.Finance.Events;

/// <summary>
/// 凭证已过账事件
/// </summary>
public class JournalEntryPostedEvent : EventBase
{
    public Guid EntryId { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
}

/// <summary>
/// 业务单据已过账事件（Invoice/Bill/Expense/CreditMemo/PaymentEntry 通用）
/// </summary>
public class FinanceDocumentPostedEvent : EventBase
{
    /// <summary>单据类型（实体名，如 "Invoice"）</summary>
    public string DocType { get; set; } = string.Empty;

    public Guid DocId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid JournalEntryId { get; set; }
    public DateTime DocDate { get; set; }

    /// <summary>价税合计（交易币）</summary>
    public decimal Total { get; set; }
}

/// <summary>
/// 业务单据已作废事件
/// </summary>
public class FinanceDocumentVoidedEvent : EventBase
{
    /// <summary>单据类型（实体名）</summary>
    public string DocType { get; set; } = string.Empty;

    public Guid DocId { get; set; }
    public string? Number { get; set; }
    public Guid VoidJournalEntryId { get; set; }
}

/// <summary>
/// 凭证已冲销事件
/// </summary>
public class JournalEntryReversedEvent : EventBase
{
    public Guid OriginalEntryId { get; set; }
    public string? OriginalNumber { get; set; }
    public Guid ReversalEntryId { get; set; }
    public string ReversalNumber { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
}
