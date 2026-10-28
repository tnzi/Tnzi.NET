namespace Tnzi.Finance.Events;

/// <summary>
/// 凭证已过账事件
/// </summary>
/// <remarks>
/// ★ <b>不是</b>「每一张进入总账的凭证」的事件，订阅它不等于镜像了总账。发布方只有两条路径：
/// 手工凭证过账（<c>IJournalEntryService.PostAsync</c>）与 <see cref="ILedgerPostingService.PostAsync"/>
/// （消费应用与子模块的自定义来源，例如薪资批次）。核心单据（发票 / 账单 / 费用 / 贷项 / 收付款 / 转账 / 存款）
/// 过账只发 <see cref="FinanceDocumentPostedEvent"/>；期末重估与核销产生的已实现汇兑凭证不发任何事件。
/// 需要逐张凭证镜像总账的消费方应按来源（<c>SourceType</c> / <c>SourceId</c>）或凭证号区间读取，
/// 而不是只靠事件。
/// </remarks>
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
/// <remarks>
/// 发布方只有凭证冲销这一条路径（<c>IJournalEntryService.ReverseAsync</c>，以及委托给它的
/// <see cref="ILedgerPostingService.ReverseAsync"/> / <see cref="ILedgerPostingService.ReverseOnBehalfOfDocumentAsync"/>）。
/// 核心单据作废产生的冲销凭证只发 <see cref="FinanceDocumentVoidedEvent"/>；撤销核销时冲回的已实现汇兑凭证不发任何事件。
/// </remarks>
public class JournalEntryReversedEvent : EventBase
{
    public Guid OriginalEntryId { get; set; }
    public string? OriginalNumber { get; set; }
    public Guid ReversalEntryId { get; set; }
    public string ReversalNumber { get; set; } = string.Empty;
    public DateTime PostingDate { get; set; }
}
