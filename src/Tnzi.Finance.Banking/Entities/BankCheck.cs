namespace Tnzi.Finance.Banking.Entities;

/// <summary>
/// 支票记录（per-bank-account 支票登记簿）
/// </summary>
/// <remarks>
/// 类名刻意为 <c>BankCheck</c> 而非 <c>Check</c>（避免与 <see cref="Tnzi.Utilities.Check"/> 参数校验工具撞名）。
/// 号码按 <see cref="BankAccount.NextCheckNumber"/> per-account 原子递增分配（不承诺无缺口，跳号=换票本）；
/// <see cref="CheckNumber"/> 在同一银行账户内唯一。三种 <see cref="Metadata.CheckStatus"/>（Issued/Void/Spoiled）
/// 均占用号码留痕，无物理删除端点。<see cref="PayeeName"/>/<see cref="Amount"/>/<see cref="Currency"/> 是打印时刻的快照。
/// </remarks>
public class BankCheck : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>出款银行账户档案</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>支票号（同银行账户内唯一）</summary>
    public long CheckNumber { get; set; }

    /// <summary>状态</summary>
    public CheckStatus Status { get; set; } = CheckStatus.Issued;

    /// <summary>关联付款单（Spoiled 为 null）</summary>
    public Guid? PaymentEntryId { get; set; }

    /// <summary>收款人名称（打印快照）</summary>
    public string? PayeeName { get; set; }

    /// <summary>金额（打印快照，交易币）</summary>
    public decimal? Amount { get; set; }

    /// <summary>币种（打印快照）</summary>
    public string? Currency { get; set; }

    /// <summary>签发日期</summary>
    public DateTime IssueDate { get; set; }

    /// <summary>打印时间（登记的手工票可为 null）</summary>
    public DateTime? PrintedTime { get; set; }

    /// <summary>是否为手工登记（非系统打印）</summary>
    public bool IsManual { get; set; }

    /// <summary>作废原因</summary>
    public string? VoidReason { get; set; }

    /// <summary>重打后的替代支票（原票作废时回链新票，形成重打链）</summary>
    public Guid? ReplacedByCheckId { get; set; }

    // ---- 打印设置快照（开票时刻钉住，重新渲染据此还原当初那张纸）----
    //
    // 五列全部可空，null = 无快照 = 回退当前银行档案。存量行与手工登记的支票天然全 null，
    // 因此这组列**不改变任何既有行的渲染结果**，消费应用只加列不必回填。
    //
    // 为什么要钉：登记簿之外，票面还取决于银行档案上的模板 / 版式 / 票纸 / 两个偏移，
    // 而那些字段随时可改。不钉住的话，换过模板或调过偏移之后重新渲染一张历史支票，
    // 画出来的不是当初真的寄出去的那张纸 —— 且没有任何报错，只是纸变了。

    /// <summary>开票时实际生效的模板名（已解析：含版式默认模板的回退结果）</summary>
    public string? PrintTemplateName { get; set; }

    /// <summary>开票时的版式</summary>
    public CheckLayout? PrintLayout { get; set; }

    /// <summary>开票时的票纸类型</summary>
    public CheckStockType? PrintStockType { get; set; }

    /// <summary>开票时的水平偏移（毫米）</summary>
    public decimal? PrintOffsetXMm { get; set; }

    /// <summary>开票时的垂直偏移（毫米）</summary>
    public decimal? PrintOffsetYMm { get; set; }
}
