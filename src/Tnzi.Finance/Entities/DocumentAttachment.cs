namespace Tnzi.Finance.Entities;

/// <summary>
/// 挂在某张单据上的附件（供应商发来的 PDF、签回的报价、收据照片…）
/// </summary>
/// <remarks>
/// **Finance 核心刻意零 Storage 引用**：文件由前端经 Storage 上传拿到 fileId，
/// 本表只记"哪张单据挂了哪个文件"。<see cref="FileFieldAttribute"/> 让框架的
/// 文件引用跟踪把它算作活引用，清理任务不会把它删掉。
///
/// 单据用 <c>SourceType</c>+<c>SourceId</c> 多态引用，与总账来源令牌同一套词汇
/// （见 <c>FinanceSourceTypes</c>）。**刻意不校验令牌属于某个封闭枚举**——消费
/// 应用经 <c>ILedgerPostingService</c> 写自己的令牌，校验就把这个开放词汇关死了。
///
/// 文件名/大小/类型是**附加那一刻的快照**：列表因此不必为每一行回 Storage 查一次，
/// 而快照本身也是"当时挂上去的是什么"这一事实的记录。下载与预览仍按 FileId 取活文件。
/// </remarks>
public class DocumentAttachment : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>单据类型（wire 令牌）</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>单据 ID</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Storage 文件</summary>
    [FileField]
    public Guid FileId { get; set; }

    /// <summary>文件名（附加时快照）</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>内容类型（附加时快照）</summary>
    public string? ContentType { get; set; }

    /// <summary>字节数（附加时快照）</summary>
    public long FileSize { get; set; }

    /// <summary>说明（可空）</summary>
    public string? Caption { get; set; }
}
