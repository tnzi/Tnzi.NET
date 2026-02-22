namespace Tnzi.Domain.Entities;

/// <summary>
/// 包含创建者、修改者、创建时间、修改时间、软删除、租户的全功能实体基类
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class FullAuditedEntity<TKey> : AuditedEntity<TKey>,
    IHasModifier,
    IHasDeleter,
    ISoftDelete,
    IMultiTenant
{
    public Guid? LastModifierId { get; set; }
    
    public bool IsDeleted { get; set; }
    public Guid? DeleterId { get; set; }
    public DateTime? DeletionTime { get; set; }
    
    public Guid? TenantId { get; set; }
}

