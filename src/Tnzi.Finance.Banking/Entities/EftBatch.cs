namespace Tnzi.Finance.Banking.Entities;

/// <summary>
/// EFT 批次（电子资金转账付款批次，出款方向 = credit）
/// </summary>
/// <remarks>
/// 单据范式：Draft 可增删行/编辑/作废；Generate 组文件（含明文账号，加密固化不落 Storage）→ 分配
/// <see cref="Number"/>（<see cref="IDocumentNumberService"/> scope，前缀 <c>EftNumberPrefix</c>）+ 原子递增
/// <see cref="BankAccount.EftFileCreationNumber"/> → Generated 后不可改（要改须作废重建）。
/// 作废硬删关联 <see cref="EftBatchLine"/> 释放付款重入批（文件已交出去过则须显式确认，见
/// <see cref="FirstDownloadedTime"/>）。文件明文经 <see cref="IFinanceDataProtector"/> 加密存
/// <see cref="FileContentEncrypted"/>（含全量账号明文，与 view 权限分离，仅 finance.eft.download 可解密下载）。
/// </remarks>
public class EftBatch : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>批次编号（生成时分配）</summary>
    public string? Number { get; set; }

    /// <summary>状态</summary>
    public EftBatchStatus Status { get; set; } = EftBatchStatus.Draft;

    /// <summary>出款银行账户档案</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>文件格式（决定定长记录布局与币种约束）</summary>
    public EftFileFormat Format { get; set; }

    /// <summary>批次币种（Nacha=USD，Cpa005=CAD）</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>生效日期（资金到账日）</summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>文件创建序号（生成时原子递增，0001-9999 循环）</summary>
    public int? FileCreationNumber { get; set; }

    /// <summary>笔数</summary>
    public int TotalCount { get; set; }

    /// <summary>总金额（交易币）</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>生成的文件名</summary>
    public string? FileName { get; set; }

    /// <summary>加密后的文件内容（含明文账号，v1: 版本前缀）</summary>
    public string? FileContentEncrypted { get; set; }

    /// <summary>生成时间</summary>
    public DateTime? GeneratedTime { get; set; }

    /// <summary>
    /// 文件首次被交出去的时间（<c>null</c> = 从未下载过）。
    /// </summary>
    /// <remarks>
    /// ★ 这个字段存在的唯一理由是**作废守卫**：作废会硬删批次行，把那些付款放回
    /// <c>GetQueueAsync</c> 的待付队列，于是它们看起来从没付过、可以再装一批发出去。
    /// 文件没交出去时这是正确的（生成错了就重建）；文件已经交给银行之后这就是**付第二次**。
    /// 框架无从知道文件有没有真的上传到银行门户，但完全知道它有没有被交出去过 ——
    /// 而「交出去过」正是释放付款从安全变危险的那条分界线。
    /// <para>
    /// 由 <c>DownloadAsync</c> 在解密成功之后原子戳（<c>ExecuteUpdate</c>，不经变更跟踪器
    /// 也就不碰 <see cref="ConcurrencyStamp"/>）—— 否则两个人同时下载会有一个拿到 409，
    /// 让一个纯读动作因并发而失败。
    /// </para>
    /// </remarks>
    public DateTime? FirstDownloadedTime { get; set; }

    /// <summary>文件被下载的次数（含首次）。</summary>
    public int DownloadCount { get; set; }

    /// <summary>作废原因</summary>
    public string? VoidReason { get; set; }
}
