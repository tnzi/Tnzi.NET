namespace Tnzi.Finance.Entities;

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
}
