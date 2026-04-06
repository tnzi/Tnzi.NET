namespace Tnzi.AI.Rag.Entities;

/// <summary>
/// 知识图谱节点 — 从文档中提取的实体（人物、组织、概念等）
/// </summary>
public class KnowledgeGraphNode : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 所属知识库 ID
    /// </summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>
    /// 实体类型（Person, Organization, Concept, Location, Event 等）
    /// </summary>
    public string EntityType { get; set; } = null!;

    /// <summary>
    /// 实体名称
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// 实体描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 额外属性（JSON 格式）
    /// </summary>
    public string? Properties { get; set; }
}
