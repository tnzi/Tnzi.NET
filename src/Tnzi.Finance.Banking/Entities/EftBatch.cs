namespace Tnzi.Finance.Banking.Entities;

/// <summary>
/// EFT 批次（电子资金转账付款批次，出款方向 = credit）
/// </summary>
/// <remarks>
/// 单据范式：Draft 可增删行/编辑/作废；Generate 组文件（含明文账号，加密固化不落 Storage）→ 分配
/// <see cref="Number"/>（<see cref="IDocumentNumberService"/> scope，前缀 <c>EftNumberPrefix</c>）+ 原子递增
/// <see cref="BankAccount.EftFileCreationNumber"/> → Generated 后不可改（要改须作废重建）。
/// 作废硬删关联 <see cref="EftBatchLine"/> 释放付款重入批。文件明文经 <see cref="IFinanceDataProtector"/> 加密存
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

    /// <summary>作废原因</summary>
    public string? VoidReason { get; set; }
}
