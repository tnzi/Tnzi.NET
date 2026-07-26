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


/// <summary>
/// 报价单 / 采购订单已发出事件
/// </summary>
/// <remarks>
/// 编号在这一刻分配，因此这是"单据对外成为事实"的时点——消费应用挂邮件发送、
/// PDF 归档、CRM 同步等副作用应该订阅它，而不是订阅创建草稿。
/// </remarks>
public class FinanceOfferSentEvent : EventBase
{
    /// <summary>单据类型（编号作用域键，见 FinanceOfferScopes）</summary>
    public string DocType { get; set; } = string.Empty;

    public Guid DocId { get; set; }
    public string Number { get; set; } = string.Empty;

    /// <summary>往来方（客户或供应商）</summary>
    public Guid PartyId { get; set; }

    /// <summary>价税合计（交易币）</summary>
    public decimal Total { get; set; }

    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// 报价单 / 采购订单已转换为正式单据事件
/// </summary>
public class FinanceOfferConvertedEvent : EventBase
{
    /// <summary>来源单据类型（编号作用域键，见 FinanceOfferScopes）</summary>
    public string SourceType { get; set; } = string.Empty;

    public Guid SourceId { get; set; }
    public string? SourceNumber { get; set; }

    /// <summary>目标单据类型（总账来源令牌，见 FinanceSourceTypes）</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>目标单据 Id（草稿）</summary>
    public Guid TargetId { get; set; }
}
