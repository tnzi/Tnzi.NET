namespace Tnzi.AI.Rag.Entities;

/// <summary>
/// 知识图谱边 - 表示两个实体之间的关系
/// </summary>
public class KnowledgeGraphEdge : CreationAuditedEntity<Guid>, IMultiTenant
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
    /// 源节点 ID
    /// </summary>
    public Guid SourceNodeId { get; set; }

    /// <summary>
    /// 目标节点 ID
    /// </summary>
    public Guid TargetNodeId { get; set; }

    /// <summary>
    /// 关系类型（如 works_for, located_in, related_to 等）
    /// </summary>
    public string RelationType { get; set; } = null!;

    /// <summary>
    /// 关系描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 关系权重（0.0 ~ 1.0，用于排序和过滤）
    /// </summary>
    public double? Weight { get; set; }

    /// <summary>
    /// 额外属性（JSON 格式，预留）。内置的图谱抽取器不写入它，自定义抽取器可以用。
    /// </summary>
    public string? Properties { get; set; }
}
