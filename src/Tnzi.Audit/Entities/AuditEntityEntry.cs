
namespace Tnzi.Audit.Entities;

/// <summary>
/// 实体变更审计条目
/// </summary>
public class AuditEntityEntry : EntityBase<Guid>, IHasCreationTime
{
    /// <summary>
    /// 获取或设置 审计操作ID
    /// </summary>
    public Guid AuditOperationId { get; set; }

    /// <summary>
    /// 获取或设置 审计操作
    /// </summary>
    public virtual AuditOperation AuditOperation { get; set; } = null!;

    /// <summary>
    /// 获取或设置 实体类型名称
    /// </summary>
    public string EntityTypeName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 实体类型全名
    /// </summary>
    public string EntityTypeFullName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 实体ID（字符串形式）
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// 获取或设置 操作类型（Added, Modified, Deleted）
    /// </summary>
    public Metadata.EntityState OperationType { get; set; }

    /// <summary>
    /// 获取或设置 变更时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 获取或设置 属性变更条目集合
    /// </summary>
    public virtual ICollection<AuditPropertyEntry> PropertyEntries { get; set; } = new List<AuditPropertyEntry>();
}
