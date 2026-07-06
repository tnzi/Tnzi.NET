namespace Tnzi.Finance.Dtos;

/// <summary>
/// 费用支出 DTO
/// </summary>
public class ExpenseDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public FinanceDocumentStatus Status { get; set; }
    public Guid? VendorId { get; set; }
    public string? VendorName { get; set; }
    public Guid PaidFromAccountId { get; set; }

    /// <summary>付款科目名称（仅详情解析；列表投影为 null）</summary>
    public string? PaidFromAccountName { get; set; }
    public DateTime DocDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public decimal BaseTotal { get; set; }
    public string? Memo { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? VoidJournalEntryId { get; set; }
    public DateTime CreationTime { get; set; }
    public List<ExpenseLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 费用支出行 DTO
/// </summary>
public class ExpenseLineDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public string? Description { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public Guid? TaxCodeId { get; set; }
}

/// <summary>
/// 创建/更新费用支出草稿请求
/// </summary>
public class CreateExpenseDto
{
    public Guid? VendorId { get; set; }

    /// <summary>付款科目（银行/现金/信用卡叶子科目）</summary>
    public Guid PaidFromAccountId { get; set; }
    public DateTime DocDate { get; set; }
    /// <summary>交易币种（null 表示本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>汇率（null 时过账按汇率表解析）</summary>
    public decimal? ExchangeRate { get; set; }

    public string? Memo { get; set; }
    public List<CreateExpenseLineDto> Lines { get; set; } = null!;
}

/// <summary>
/// 费用支出行请求
/// </summary>
public class CreateExpenseLineDto
{
    public string? Description { get; set; }

    /// <summary>费用科目（必填）</summary>
    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }
    public Guid? TaxCodeId { get; set; }
}

/// <summary>
/// 费用支出查询请求
/// </summary>
public class ExpenseQueryDto : PagedQueryDto
{
    /// <summary>关键字（编号/摘要模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>按状态过滤</summary>
    public FinanceDocumentStatus? Status { get; set; }

    /// <summary>按供应商过滤</summary>
    public Guid? VendorId { get; set; }

    /// <summary>单据日期起</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>单据日期止</summary>
    public DateTime? DateTo { get; set; }
}
