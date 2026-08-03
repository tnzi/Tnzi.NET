namespace Tnzi.Finance.Dtos;

/// <summary>
/// 会计凭证 DTO
/// </summary>
public class JournalEntryDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public JournalEntryStatus Status { get; set; }
    public DateTime PostingDate { get; set; }
    public string? Memo { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }

    /// <summary>借方合计（本位币）。草稿为 0 —— 见 <see cref="TxnTotalDebit"/>。</summary>
    public decimal TotalDebit { get; set; }

    /// <summary>贷方合计（本位币）。草稿为 0 —— 见 <see cref="TxnTotalCredit"/>。</summary>
    public decimal TotalCredit { get; set; }

    /// <summary>
    /// 借方合计（交易币种）。与分录行的 <c>Debit</c>/<c>TxnDebit</c> 成对关系一致。
    /// </summary>
    /// <remarks>
    /// 草稿唯一可用的合计。<see cref="TotalDebit"/> 是本位币口径、按设计只在过账时冗余
    /// （草稿尚无汇率，本位币金额根本不存在），所以草稿读它恒为 0；而交易币金额建草稿时
    /// 就已写入，任何状态都取得到。不要把本值回填进 <see cref="TotalDebit"/>：两者币种口径
    /// 不同，混用会让外币凭证的合计悄悄变成另一种货币的数字。
    /// </remarks>
    public decimal TxnTotalDebit { get; set; }

    /// <summary>贷方合计（交易币种）。见 <see cref="TxnTotalDebit"/>。</summary>
    public decimal TxnTotalCredit { get; set; }

    public DateTime? PostedTime { get; set; }
    public Guid? PostedById { get; set; }
    public Guid? ReversalOfEntryId { get; set; }
    public Guid? ReversedByEntryId { get; set; }
    public DateTime CreationTime { get; set; }
    public List<JournalLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 会计分录行 DTO
/// </summary>
public class JournalLineDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public Guid AccountId { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal TxnDebit { get; set; }
    public decimal TxnCredit { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public string? PartyType { get; set; }
    public string? PartyId { get; set; }
    public string? Dimensions { get; set; }
    public Guid? TaxRateId { get; set; }
}

/// <summary>
/// 创建/更新凭证草稿请求
/// </summary>
public class CreateJournalEntryDto
{
    /// <summary>过账日期</summary>
    public DateTime PostingDate { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>交易币种（null 表示本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>汇率（null 表示过账时按汇率表解析；本位币凭证忽略）</summary>
    public decimal? ExchangeRate { get; set; }

    /// <summary>分录行</summary>
    public List<CreateJournalLineDto> Lines { get; set; } = null!;
}

/// <summary>
/// 创建凭证分录行请求（金额为交易币种）
/// </summary>
public class CreateJournalLineDto
{
    public Guid AccountId { get; set; }

    /// <summary>借方金额（交易币种；与贷方二选一）</summary>
    public decimal Debit { get; set; }

    /// <summary>贷方金额（交易币种；与借方二选一）</summary>
    public decimal Credit { get; set; }

    public string? Memo { get; set; }
    public string? PartyType { get; set; }
    public string? PartyId { get; set; }
    public string? Dimensions { get; set; }
}

/// <summary>
/// 冲销凭证请求
/// </summary>
public class ReverseJournalEntryDto
{
    /// <summary>冲销凭证过账日期（null 表示与原凭证同日）</summary>
    public DateTime? PostingDate { get; set; }

    /// <summary>冲销摘要（null 表示自动生成）</summary>
    public string? Memo { get; set; }
}

/// <summary>
/// 凭证可冲销性（<see cref="Services.ILedgerPostingService.GetReversibilityAsync"/> 的只读判定结果）
/// </summary>
/// <remarks>
/// 判定口径与冲销实际执行的校验同源，因此 <see cref="CanReverse"/> 为 true 时冲销不会因本判定覆盖的
/// 原因被拒。它<b>不是</b>预约：并发的过账/对账/封账仍可能在两次调用之间改变结论，冲销漏斗内的守卫才是权威。
/// </remarks>
public class ReversibilityDto
{
    /// <summary>被判定的凭证</summary>
    public Guid JournalEntryId { get; set; }

    /// <summary>现在能否冲销</summary>
    public bool CanReverse { get; set; }

    /// <summary>受阻原因代码（见 <see cref="ReversalBlockReasons"/>）；CanReverse 时为 null。</summary>
    public string? BlockedBy { get; set; }

    /// <summary>面向操作员的说明（含补救办法）；CanReverse 时为 null。</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// 凭证查询请求
/// </summary>
public class JournalEntryQueryDto : PagedQueryDto
{
    public JournalEntryStatus? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public string? Keyword { get; set; }
}

/// <summary>
/// 总账过账请求（面向框架内模块与消费应用的编程式过账入口）
/// </summary>
/// <remarks>
/// 任意业务单据（发票、账单、工资单或消费应用自定义单据）通过本请求投影到总账，
/// 无需修改财务核心。科目按 AccountId → AccountCode → AccountRole 优先级解析。
/// </remarks>
public class LedgerPostingRequest
{
    /// <summary>过账日期</summary>
    public DateTime PostingDate { get; set; }

    /// <summary>摘要</summary>
    public string? Memo { get; set; }

    /// <summary>交易币种（null 表示本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>汇率（null 表示按汇率表解析）</summary>
    public decimal? ExchangeRate { get; set; }

    /// <summary>来源单据类型（必填，如 "Payment.Invoice"）</summary>
    public string SourceType { get; set; } = null!;

    /// <summary>来源单据ID（必填）</summary>
    public string SourceId { get; set; } = null!;

    /// <summary>分录行</summary>
    public List<LedgerPostingLine> Lines { get; set; } = null!;
}

/// <summary>
/// 总账过账行（金额为交易币种；科目三选一：Id / Code / Role）
/// </summary>
public class LedgerPostingLine
{
    public Guid? AccountId { get; set; }
    public string? AccountCode { get; set; }
    public AccountSystemRole? AccountRole { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    public string? Memo { get; set; }
    public string? PartyType { get; set; }
    public string? PartyId { get; set; }
    public string? Dimensions { get; set; }

    /// <summary>
    /// 税率ID（可选的结构化税务维度；写入后 TaxSummary 报表按其聚合。
    /// 透传语义同 PartyType/PartyId，框架不校验其存在性）
    /// </summary>
    public Guid? TaxRateId { get; set; }
}
