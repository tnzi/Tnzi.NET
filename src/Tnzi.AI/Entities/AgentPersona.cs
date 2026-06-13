namespace Tnzi.AI.Entities;

/// <summary>
/// Agent 人格实体 — 可复用的 Soul 内容。
/// 有意不实现 <see cref="IMultiTenant"/>：全局查询过滤器的严格等值条件会把
/// System 人格（TenantId=null）对所有真实租户隐藏。可见性由服务层通过
/// <see cref="ResourceScope"/> 联合过滤强制执行（System ∪ 当前租户的 Tenant 行）。
/// 软删除（FullAuditedEntity）与同为 IScopedResource 的 <see cref="Provider"/> 对齐：
/// 人格被 Agent.PersonaId 引用，硬删会导致 SetNull 后内容不可恢复。
/// </summary>
public class AgentPersona : FullAuditedEntity<Guid>, IScopedResource
{
    /// <summary>人格名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL 友好标识符</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>人格内容（Markdown 格式的 Soul 描述）</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>
    /// 可见性作用域：System（全局内置，所有租户可见）或 Tenant（私有，仅所属租户可见）。
    /// 默认 Tenant，由服务层在创建时根据当前租户上下文自动设置。
    /// </summary>
    public ResourceScope Scope { get; set; } = ResourceScope.Tenant;

    /// <summary>租户 ID（Scope=Tenant 时非空；Scope=System 时为 null）</summary>
    public Guid? TenantId { get; set; }
}
