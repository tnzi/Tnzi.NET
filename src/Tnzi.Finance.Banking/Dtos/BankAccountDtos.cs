namespace Tnzi.Finance.Banking.Dtos;

/// <summary>
/// 银行账户档案（响应；账号仅回掩码，永不回明文/密文）
/// </summary>
public class BankAccountDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>挂载科目名称（服务层补齐）</summary>
    public string? AccountName { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public BankNumberScheme Scheme { get; set; }
    public string? RoutingNumber { get; set; }
    public string? InstitutionNumber { get; set; }
    public string? TransitNumber { get; set; }

    /// <summary>账号掩码（尾 4 位）</summary>
    public string? AccountNumberMasked { get; set; }

    public string? Currency { get; set; }

    public long NextCheckNumber { get; set; }
    public CheckStockType CheckStockType { get; set; }
    public CheckLayout CheckLayout { get; set; }

    /// <summary>支票版式模板名（null = 渲染器默认模板）</summary>
    public string? CheckTemplateName { get; set; }

    public decimal OffsetXMm { get; set; }
    public decimal OffsetYMm { get; set; }

    public string? FeedProviderKey { get; set; }
    public string? ExternalAccountId { get; set; }
    public DateTime? LastFeedSyncTime { get; set; }

    public string? EftOriginatorId { get; set; }
    public string? EftOriginatorName { get; set; }
    public int EftFileCreationNumber { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建银行账户档案
/// </summary>
public class CreateBankAccountDto
{
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public BankNumberScheme Scheme { get; set; } = BankNumberScheme.UsAba;
    public string? RoutingNumber { get; set; }
    public string? InstitutionNumber { get; set; }
    public string? TransitNumber { get; set; }

    /// <summary>账号明文（单向入库：加密存储，仅回掩码）</summary>
    public string? AccountNumber { get; set; }

    public string? Currency { get; set; }

    /// <summary>起始支票号（可手工指定，默认 1）</summary>
    public long NextCheckNumber { get; set; } = 1;

    public CheckStockType CheckStockType { get; set; } = CheckStockType.PrePrinted;
    public CheckLayout CheckLayout { get; set; } = CheckLayout.Voucher;

    /// <summary>支票版式模板名（留空 = 渲染器默认模板）</summary>
    public string? CheckTemplateName { get; set; }

    public decimal OffsetXMm { get; set; }
    public decimal OffsetYMm { get; set; }

    public string? FeedProviderKey { get; set; }
    public string? ExternalAccountId { get; set; }
    public string? EftOriginatorId { get; set; }
    public string? EftOriginatorName { get; set; }
}

/// <summary>
/// 更新银行账户档案（挂载科目与支票号不可经此变更）
/// </summary>
public class UpdateBankAccountDto
{
    public string Name { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public BankNumberScheme Scheme { get; set; } = BankNumberScheme.UsAba;
    public string? RoutingNumber { get; set; }
    public string? InstitutionNumber { get; set; }
    public string? TransitNumber { get; set; }

    /// <summary>账号明文（留空 = 保持现有账号不变）</summary>
    public string? AccountNumber { get; set; }

    public string? Currency { get; set; }

    public CheckStockType CheckStockType { get; set; } = CheckStockType.PrePrinted;
    public CheckLayout CheckLayout { get; set; } = CheckLayout.Voucher;

    /// <summary>支票版式模板名（留空 = 渲染器默认模板）</summary>
    public string? CheckTemplateName { get; set; }

    public decimal OffsetXMm { get; set; }
    public decimal OffsetYMm { get; set; }

    public string? FeedProviderKey { get; set; }
    public string? ExternalAccountId { get; set; }
    public string? EftOriginatorId { get; set; }
    public string? EftOriginatorName { get; set; }
}

/// <summary>
/// 设置下一张支票号（跳号=换票本，不承诺无缺口）
/// </summary>
public class SetNextCheckNumberDto
{
    public long NextCheckNumber { get; set; }
}

/// <summary>
/// 银行账户档案面的部署能力（与具体档案无关，取决于本次部署的配置）
/// </summary>
public class BankAccountCapabilitiesDto
{
    /// <summary>
    /// 能否存储账号明文（<c>Finance:Encryption:EncryptionKey</c> 已配置）。
    /// 为 false 时写入账号会被拒（400）——呈现端据此禁用账号字段并说明，
    /// 而不是让用户填完再报错。预印支票纸打印不需要账号，EFT 需要
    /// </summary>
    public bool CanStoreAccountNumber { get; set; }
}

/// <summary>
/// 银行账户档案查询
/// </summary>
public class BankAccountQueryDto : PagedQueryDto
{
    public Guid? AccountId { get; set; }
    public string? Keyword { get; set; }
}
