namespace Tnzi.Domain.Entities;

/// <summary>
/// 包含创建时间和修改时间的实体基类（适用于业务实体，不需要软删除的场景）
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class AuditedEntity<TKey> : CreationAuditedEntity<TKey>, IHasModificationTime, IHasCreator
{
    public DateTime? LastModificationTime { get; set; }
    public virtual Guid? CreatorId { get; set; }
}
