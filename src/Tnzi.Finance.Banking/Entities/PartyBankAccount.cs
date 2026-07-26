namespace Tnzi.Finance.Banking.Entities;

/// <summary>
/// 往来方银行账户（remit-to：客户/供应商的结构化收款/付款账户）
/// </summary>
/// <remarks>
/// EFT 输出的收款方账户来源。账号明文单向入库（写入即加密），DTO 仅回
/// <see cref="AccountNumberMasked"/>，永不回明文。每个往来方至多一个默认账户
/// （<see cref="IsDefault"/>，服务层在同一事务内清除旧默认）。
/// </remarks>
public class PartyBankAccount : MultiTenantAuditedEntity<Guid>
{
    /// <summary>往来方类型</summary>
    public FinancePartyType PartyType { get; set; }

    /// <summary>往来方 ID（Customer/Vendor）</summary>
    public Guid PartyId { get; set; }

    /// <summary>标签（如 "Primary" / "USD Payroll"）</summary>
    public string? Label { get; set; }

    /// <summary>银行名称</summary>
    public string? BankName { get; set; }

    /// <summary>账号编码方案</summary>
    public BankNumberScheme Scheme { get; set; } = BankNumberScheme.UsAba;

    /// <summary>US ABA 路由号（9 位含 mod-10 校验位）</summary>
    public string? RoutingNumber { get; set; }

    /// <summary>CA 机构号（3 位）</summary>
    public string? InstitutionNumber { get; set; }

    /// <summary>CA 分行 transit 号（5 位）</summary>
    public string? TransitNumber { get; set; }

    /// <summary>加密后的账号密文（v1: 版本前缀）</summary>
    public string? AccountNumberEncrypted { get; set; }

    /// <summary>账号掩码（尾 4 位，永不解密）</summary>
    public string? AccountNumberMasked { get; set; }

    /// <summary>账户类型（EFT 交易码派生）</summary>
    public BankAccountType AccountType { get; set; } = BankAccountType.Checking;

    /// <summary>账户币种（null = 不限币种）</summary>
    public string? Currency { get; set; }

    /// <summary>是否为该往来方的默认账户（每方至多一）</summary>
    public bool IsDefault { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>备注</summary>
    public string? Notes { get; set; }
}
