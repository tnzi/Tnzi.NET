namespace Tnzi.Domain.Entities;

/// <summary>
/// 包含创建时间的实体基类（适用于日志、快照等只需要记录创建时间的场景）
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class CreationAuditedEntity<TKey> : EntityBase<TKey>, IHasCreationTime
{
    public DateTime CreationTime { get; set; }
}
