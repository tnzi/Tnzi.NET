namespace Tnzi.Finance.Banking.Entities;

/// <summary>
/// 银行账户档案（1:1 挂在资金科目上）
/// </summary>
/// <remarks>
/// 银行流水拉取、支票号段、EFT originator 三块共用的账户元数据；GL <see cref="Account"/>
/// 保持纯科目树节点。<see cref="AccountId"/> 唯一（每个资金科目至多一个银行档案）。
/// 账号明文单向入库：写入即加密到 <see cref="AccountNumberEncrypted"/>，列表/DTO 仅回
/// <see cref="AccountNumberMasked"/>（尾 4 位），永不解密回明文到 UI。
/// </remarks>
public class BankAccount : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>挂载的资金科目（CashFlowActivity = CashEquivalent 的可过账叶子）</summary>
    public Guid AccountId { get; set; }

    /// <summary>档案名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>银行名称</summary>
    public string? BankName { get; set; }

    /// <summary>账号编码方案（决定路由字段校验与 MICR 拼装）</summary>
    public BankNumberScheme Scheme { get; set; } = BankNumberScheme.UsAba;

    /// <summary>US ABA 路由号（9 位含 mod-10 校验位）</summary>
    public string? RoutingNumber { get; set; }

    /// <summary>CA 机构号（3 位）</summary>
    public string? InstitutionNumber { get; set; }

    /// <summary>CA 分行 transit 号（5 位）</summary>
    public string? TransitNumber { get; set; }

    /// <summary>加密后的账号密文（v1: 版本前缀）</summary>
    public string? AccountNumberEncrypted { get; set; }

    /// <summary>账号掩码（尾 4 位，列表展示用，永不解密）</summary>
    public string? AccountNumberMasked { get; set; }

    /// <summary>账户币种（null = 不限币种）</summary>
    public string? Currency { get; set; }

    // ---- 支票打印组 ----

    /// <summary>下一张支票号（per-account 原子递增，起始号可手工指定）</summary>
    public long NextCheckNumber { get; set; } = 1;

    /// <summary>支票票纸类型</summary>
    public CheckStockType CheckStockType { get; set; } = CheckStockType.PrePrinted;

    /// <summary>支票版式</summary>
    public CheckLayout CheckLayout { get; set; } = CheckLayout.Voucher;

    /// <summary>
    /// 支票版式模板名（模板驱动渲染器用；null = 渲染器的默认模板）
    /// </summary>
    /// <remarks>
    /// 指向 <c>Tnzi.Template</c> 中 Module=<c>Tnzi.Finance</c> 的一条模板记录，
    /// 让不同银行的票纸各用一套版式（预印票纸的坐标随票纸厂商而异），
    /// 且经模板管理端微调坐标无需改代码。
    /// </remarks>
    public string? CheckTemplateName { get; set; }

    /// <summary>打印水平偏移（毫米，全票面平移校准）</summary>
    public decimal OffsetXMm { get; set; }

    /// <summary>打印垂直偏移（毫米，全票面平移校准）</summary>
    public decimal OffsetYMm { get; set; }

    // ---- 银行流水组 ----

    /// <summary>银行 feed 提供者 Key（IBankFeedProvider.Key）</summary>
    public string? FeedProviderKey { get; set; }

    /// <summary>提供者侧外部账户标识</summary>
    public string? ExternalAccountId { get; set; }

    /// <summary>上次拉取的游标（增量续拉）</summary>
    public string? FeedCursor { get; set; }

    /// <summary>上次 feed 同步时间</summary>
    public DateTime? LastFeedSyncTime { get; set; }

    // ---- EFT 组 ----

    /// <summary>EFT originator 标识（NACHA/CPA-005 出款方 id）</summary>
    public string? EftOriginatorId { get; set; }

    /// <summary>EFT originator 名称</summary>
    public string? EftOriginatorName { get; set; }

    /// <summary>CPA-005 文件创建序号（0001-9999 循环，生成时原子递增）</summary>
    public int EftFileCreationNumber { get; set; }
}
