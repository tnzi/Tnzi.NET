
namespace Tnzi.Audit.Entities;

/// <summary>
/// 属性变更审计条目
/// </summary>
public class AuditPropertyEntry : EntityBase<Guid>, IHasCreationTime
{
    /// <summary>
    /// 获取或设置 实体审计条目ID
    /// </summary>
    public Guid AuditEntityEntryId { get; set; }

    /// <summary>
    /// 获取或设置 实体审计条目
    /// </summary>
    public virtual AuditEntityEntry AuditEntityEntry { get; set; } = null!;

    /// <summary>
    /// 获取或设置 属性名称
    /// </summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 属性显示名称
    /// </summary>
    /// <remarks>
    /// 给自定义采集方留的列：内置的 <c>EntityAuditSaveChangesInterceptor</c> 不写它（恒为 <c>null</c>），
    /// 读取方不要指望它有值，按 <see cref="PropertyName"/> 显示。
    /// </remarks>
    public string? PropertyDisplayName { get; set; }

    /// <summary>
    /// 获取或设置 属性类型名称
    /// </summary>
    public string PropertyTypeName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 原始值（JSON格式）
    /// </summary>
    public string? OriginalValue { get; set; }

    /// <summary>
    /// 获取或设置 新值（JSON格式）
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// 获取或设置 变更时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}
