namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// EFT 文件组装器（按 <see cref="EftFileFormat"/> 分发；可整体替换以适配银行方言）
/// </summary>
/// <remarks>
/// 默认实现产出标准 NACHA（94 字符定长）/ CPA-005（1464 字符逻辑记录）文本。各银行落地差异
/// （字段占位、SEC/交易码取值、块填充）以可替换实现覆盖，落地前须核对银行样件（文档已声明）。
/// </remarks>
public interface IEftFileComposer
{
    /// <summary>组装 EFT 文件文本（明文，调用方负责加密固化，不落 Storage）。</summary>
    Result<EftComposeResult> Compose(EftComposeRequest request);
}

/// <summary>
/// EFT 文件组装请求（出款方 + 逐笔收款明细，账号为解密后的明文，仅内存栈）
/// </summary>
public class EftComposeRequest
{
    public EftFileFormat Format { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime EffectiveDate { get; set; }
    public DateTime CreationTime { get; set; }

    /// <summary>文件创建序号（NACHA File ID Modifier / CPA-005 File Creation Number）</summary>
    public int FileCreationNumber { get; set; }

    // ---- 出款方（取自银行账户档案）----
    public string? OriginatorId { get; set; }
    public string? OriginatorName { get; set; }
    public string? BankName { get; set; }
    public string? OriginatorRoutingNumber { get; set; }       // US ABA 9 位
    public string? OriginatorInstitutionNumber { get; set; }   // CA 3 位
    public string? OriginatorTransitNumber { get; set; }       // CA 5 位
    public string? OriginatorAccountNumber { get; set; }       // 明文

    public List<EftComposeEntry> Entries { get; set; } = new();
}

/// <summary>
/// 单笔 EFT 收款明细（账号为明文，仅内存栈）
/// </summary>
public class EftComposeEntry
{
    public string PayeeName { get; set; } = string.Empty;
    public string? RoutingNumber { get; set; }       // US
    public string? InstitutionNumber { get; set; }   // CA 3 位
    public string? TransitNumber { get; set; }       // CA 5 位
    public string AccountNumber { get; set; } = string.Empty; // 明文
    public BankAccountType AccountType { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
}

/// <summary>
/// EFT 文件组装结果
/// </summary>
public class EftComposeResult
{
    public string Content { get; set; } = string.Empty;
    public string FileExtension { get; set; } = "txt";
}
