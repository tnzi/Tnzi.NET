namespace Tnzi.Finance.Dtos;

/// <summary>
/// EFT 批次 DTO
/// </summary>
public class EftBatchDto
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public EftBatchStatus Status { get; set; }
    public Guid BankAccountId { get; set; }

    /// <summary>出款银行账户名称（服务层补齐）</summary>
    public string? BankAccountName { get; set; }

    public EftFileFormat Format { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime EffectiveDate { get; set; }
    public int? FileCreationNumber { get; set; }
    public int TotalCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? FileName { get; set; }
    public DateTime? GeneratedTime { get; set; }
    public string? VoidReason { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }

    /// <summary>批次行（详情返回）</summary>
    public List<EftBatchLineDto> Lines { get; set; } = new();
}

/// <summary>
/// EFT 批次行 DTO
/// </summary>
public class EftBatchLineDto
{
    public Guid Id { get; set; }
    public Guid PaymentEntryId { get; set; }
    public string? PaymentNumber { get; set; }
    public Guid PartyBankAccountId { get; set; }
    public string? PartyBankAccountMasked { get; set; }
    public decimal Amount { get; set; }
    public string? PayeeName { get; set; }
}

/// <summary>
/// EFT 队列项（可入批的 Posted Outbound BankTransfer 付款）
/// </summary>
public class EftQueueItemDto
{
    public Guid PaymentEntryId { get; set; }
    public string? PaymentNumber { get; set; }
    public FinancePartyType PartyType { get; set; }
    public Guid PartyId { get; set; }
    public string? PayeeName { get; set; }
    public DateTime DocDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    /// <summary>收款方默认银行账户</summary>
    public Guid PartyBankAccountId { get; set; }
    public string? PartyBankAccountMasked { get; set; }
    public BankNumberScheme PartyScheme { get; set; }
}

/// <summary>
/// 创建 EFT 批次
/// </summary>
public class CreateEftBatchDto
{
    public Guid BankAccountId { get; set; }
    public EftFileFormat Format { get; set; }
    public DateTime EffectiveDate { get; set; }

    /// <summary>纳入批次的付款单（须均可入批且币种/scheme 与格式匹配）</summary>
    public List<Guid> PaymentEntryIds { get; set; } = null!;
}

/// <summary>
/// 作废 EFT 批次
/// </summary>
public class VoidEftBatchDto
{
    public string? Reason { get; set; }
}

/// <summary>
/// 生成的 EFT 文件（下载）
/// </summary>
public class EftFileDto
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// EFT 批次查询
/// </summary>
public class EftBatchQueryDto : PagedQueryDto
{
    public Guid? BankAccountId { get; set; }
    public EftBatchStatus? Status { get; set; }
    public EftFileFormat? Format { get; set; }
}
