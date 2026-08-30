namespace Tnzi.Finance.Dtos;

/// <summary>
/// 银行存款单 DTO
/// </summary>
public class DepositDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public FinanceDocumentStatus Status { get; set; }

    /// <summary>来源科目（待存款项 / 过渡科目）</summary>
    public Guid FromAccountId { get; set; }

    /// <summary>来源科目名称（服务层补齐）</summary>
    public string? FromAccountName { get; set; }

    /// <summary>目标银行科目</summary>
    public Guid ToAccountId { get; set; }

    /// <summary>目标银行科目名称（服务层补齐）</summary>
    public string? ToAccountName { get; set; }

    public DateTime DepositDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal Amount { get; set; }
    public decimal BaseAmount { get; set; }
    public string? Reference { get; set; }
    public string? Memo { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? VoidJournalEntryId { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }

    /// <summary>单据行（仅详情填充；列表投影为空）</summary>
    public List<DepositLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 银行存款单行 DTO
/// </summary>
public class DepositLineDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }

    /// <summary>收款单（收款行非空）</summary>
    public Guid? PaymentEntryId { get; set; }

    /// <summary>收款单编号（服务层补齐）</summary>
    public string? PaymentNumber { get; set; }

    /// <summary>收款往来方名称（服务层补齐）</summary>
    public string? PartyName { get; set; }

    /// <summary>其它款项行的贷记科目</summary>
    public Guid? AccountId { get; set; }

    /// <summary>贷记科目名称（服务层补齐）</summary>
    public string? AccountName { get; set; }

    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? Reference { get; set; }

    /// <summary>该行对收款单的独占声明是否仍然有效（作废后为 false）</summary>
    public bool IsClaimActive { get; set; }
}

/// <summary>
/// 创建/更新银行存款单草稿请求
/// </summary>
/// <remarks>
/// 至少一条行（收款行或其它款项行）；总额由行合计派生，不接受调用方传入 ——
/// 让调用方送一个可以与行不一致的合计，等于允许「凭证平、单据不平」的单据存在。
/// </remarks>
public class CreateDepositDto
{
    /// <summary>来源科目（收款当初落在哪；通常是 UndepositedFunds 角色科目）</summary>
    public Guid FromAccountId { get; set; }

    /// <summary>目标银行科目</summary>
    public Guid ToAccountId { get; set; }

    public DateTime DepositDate { get; set; }

    /// <summary>交易币种（null = 本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>汇率（null = 过账时按汇率表解析）</summary>
    public decimal? ExchangeRate { get; set; }

    public string? Reference { get; set; }
    public string? Memo { get; set; }

    /// <summary>收款行：要带去银行的收款单 Id 列表</summary>
    public List<Guid> PaymentEntryIds { get; set; } = null!;

    /// <summary>其它款项行（非收款单来源的钱：银行利息、供应商退款、股东投入……）</summary>
    public List<CreateDepositFundsLineDto> OtherFunds { get; set; } = null!;
}

/// <summary>
/// 存款单「其它款项」行请求（QuickBooks 存款界面的 "Add funds to this deposit"）
/// </summary>
public class CreateDepositFundsLineDto
{
    /// <summary>贷记科目（必填；不得是 A/R、A/P 控制科目，也不得是本单的来源/目标科目）</summary>
    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? Reference { get; set; }
}

/// <summary>
/// 银行存款单查询请求
/// </summary>
public class DepositQueryDto : PagedQueryDto
{
    /// <summary>关键字（编号/参考号/摘要模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>按状态过滤</summary>
    public FinanceDocumentStatus? Status { get; set; }

    /// <summary>按科目过滤（命中来源或目标任一侧）</summary>
    public Guid? AccountId { get; set; }

    /// <summary>存款日期起</summary>
    public DateTime? From { get; set; }

    /// <summary>存款日期止</summary>
    public DateTime? To { get; set; }
}

/// <summary>
/// 待存款收款查询（存款单编辑器的候选清单）
/// </summary>
public class UndepositedReceiptQueryDto
{
    /// <summary>收款所在科目（通常是 UndepositedFunds 角色科目）</summary>
    public Guid AccountId { get; set; }

    /// <summary>按币种过滤（一张存款单只能是一种币）</summary>
    public string? Currency { get; set; }

    /// <summary>单据日期起</summary>
    public DateTime? From { get; set; }

    /// <summary>单据日期止</summary>
    public DateTime? To { get; set; }
}

/// <summary>
/// 待存款收款项（已过账的 Inbound 收款，且尚未被任何存活的存款单收走）
/// </summary>
public class UndepositedReceiptDto
{
    public Guid PaymentEntryId { get; set; }
    public string? PaymentNumber { get; set; }
    public FinancePartyType PartyType { get; set; }
    public Guid PartyId { get; set; }

    /// <summary>往来方名称（服务层补齐）</summary>
    public string? PartyName { get; set; }

    public DateTime DocDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    /// <summary>结算方式（Cash/Check/BankTransfer…）</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>外部参考号（支票号/交易号）</summary>
    public string? Reference { get; set; }
}
