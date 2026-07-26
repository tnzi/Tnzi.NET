namespace Tnzi.Finance.Entities;

/// <summary>
/// 单据上的一条内部讨论
/// </summary>
/// <remarks>
/// 内部可见，不随单据发给客户/供应商——单据本身要给对方看的话在 Memo 里说。
///
/// 用 <see cref="FullAuditedEntity{TKey}"/> 而非硬删：一条能被悄悄抹掉的讨论线
/// 在审计语境里等于没有。作者可以删自己的（软删保留行），但历史不会因此消失。
/// </remarks>
public class DocumentComment : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>单据类型（wire 令牌）</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>单据 ID</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>正文</summary>
    public string Body { get; set; } = string.Empty;
}
