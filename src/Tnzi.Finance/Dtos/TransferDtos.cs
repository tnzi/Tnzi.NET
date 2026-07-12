namespace Tnzi.Finance.Dtos;

/// <summary>
/// 资金划转单
/// </summary>
public class TransferDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public FinanceDocumentStatus Status { get; set; }
    public Guid FromAccountId { get; set; }

    /// <summary>转出科目名称（服务层补齐）</summary>
    public string? FromAccountName { get; set; }

    public Guid ToAccountId { get; set; }

    /// <summary>转入科目名称（服务层补齐）</summary>
    public string? ToAccountName { get; set; }

    public DateTime TransferDate { get; set; }
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
}

/// <summary>
/// 创建/更新资金划转单草稿
/// </summary>
public class CreateTransferDto
{
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public DateTime TransferDate { get; set; }

    /// <summary>交易币种（null = 本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>汇率（null = 过账时按汇率表解析）</summary>
    public decimal? ExchangeRate { get; set; }

    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public string? Memo { get; set; }
}

/// <summary>
/// 资金划转单查询
/// </summary>
public class TransferQueryDto : PagedQueryDto
{
    public FinanceDocumentStatus? Status { get; set; }
    public Guid? AccountId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
