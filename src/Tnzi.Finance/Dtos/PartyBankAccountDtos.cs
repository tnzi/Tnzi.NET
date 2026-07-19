namespace Tnzi.Finance.Dtos;

/// <summary>
/// 往来方银行账户（响应；账号仅回掩码，永不回明文/密文）
/// </summary>
public class PartyBankAccountDto
{
    public Guid Id { get; set; }
    public FinancePartyType PartyType { get; set; }
    public Guid PartyId { get; set; }
    public string? Label { get; set; }
    public string? BankName { get; set; }
    public BankNumberScheme Scheme { get; set; }
    public string? RoutingNumber { get; set; }
    public string? InstitutionNumber { get; set; }
    public string? TransitNumber { get; set; }

    /// <summary>账号掩码（尾 4 位）</summary>
    public string? AccountNumberMasked { get; set; }

    public BankAccountType AccountType { get; set; }
    public string? Currency { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建/更新往来方银行账户
/// </summary>
public class SavePartyBankAccountDto
{
    public FinancePartyType PartyType { get; set; }
    public Guid PartyId { get; set; }
    public string? Label { get; set; }
    public string? BankName { get; set; }
    public BankNumberScheme Scheme { get; set; } = BankNumberScheme.UsAba;
    public string? RoutingNumber { get; set; }
    public string? InstitutionNumber { get; set; }
    public string? TransitNumber { get; set; }

    /// <summary>账号明文（单向入库：加密存储，仅回掩码；更新时留空 = 保持不变）</summary>
    public string? AccountNumber { get; set; }

    public BankAccountType AccountType { get; set; } = BankAccountType.Checking;
    public string? Currency { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>
/// 往来方银行账户查询
/// </summary>
public class PartyBankAccountQueryDto : PagedQueryDto
{
    public FinancePartyType? PartyType { get; set; }
    public Guid? PartyId { get; set; }
    public bool? IsActive { get; set; }
}
