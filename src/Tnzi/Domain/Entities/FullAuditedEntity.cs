namespace Tnzi.Domain.Entities;

/// <summary>
/// 包含创建者、修改者、创建时间、修改时间、软删除的全功能实体基类
/// </summary>
/// <remarks>
/// 不包含多租户支持。若需要 TenantId，请使用 <see cref="MultiTenantAuditedEntity{TKey}"/>
/// 或在实体上显式实现 <see cref="IMultiTenant"/>。
/// </remarks>
[StableApi(Since = "0.1.0")]
public abstract class FullAuditedEntity<TKey> : AuditedEntity<TKey>,
    IHasModifier,
    IHasDeleter,
    ISoftDelete
{
    public Guid? LastModifierId { get; set; }

    public bool IsDeleted { get; set; }
    public Guid? DeleterId { get; set; }
    public DateTime? DeletionTime { get; set; }
}

/// <summary>
/// 包含创建者、修改者、创建时间、修改时间、软删除、多租户的全功能实体基类
/// </summary>
/// <remarks>
/// 在 <see cref="FullAuditedEntity{TKey}"/> 基础上加入 <see cref="IMultiTenant"/> 支持（TenantId 字段）。
/// 仅在实体需要按租户隔离时使用。
/// </remarks>
[StableApi(Since = "0.1.0")]
public abstract class MultiTenantAuditedEntity<TKey> : FullAuditedEntity<TKey>, IMultiTenant
{
    public Guid? TenantId { get; set; }
}

