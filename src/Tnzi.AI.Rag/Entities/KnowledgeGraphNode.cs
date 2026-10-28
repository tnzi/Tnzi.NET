namespace Tnzi.AI.Rag.Entities;

/// <summary>
/// 知识图谱节点 - 从文档中提取的实体（人物、组织、概念等）
/// </summary>
public class KnowledgeGraphNode : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 所属知识库 ID
    /// </summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>
    /// 抽出这一行的来源文档 ID：删除文档时据此清掉它的图谱，重新摄取同一文档时据此先删后插。
    /// 为空 = 来源未知（这一列出现之前抽取的存量行，或只实现了旧签名 <c>IGraphExtractor.ExtractAsync</c> 的自定义抽取器写的行），
    /// 这些行只随整个知识库一起删除。
    /// </summary>
    public Guid? DocumentId { get; set; }

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
    /// 额外属性（JSON 格式，预留）。内置的图谱抽取器不写入它，自定义抽取器可以用。
    /// </summary>
    public string? Properties { get; set; }
}
