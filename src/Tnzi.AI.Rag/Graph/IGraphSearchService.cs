namespace Tnzi.AI.Rag.Graph;

/// <summary>
/// 知识图谱搜索服务 — 基于实体名称/类型的图谱检索，支持关系扩展
/// </summary>
public interface IGraphSearchService
{
    /// <summary>
    /// 在知识图谱中搜索匹配的实体及其关系
    /// </summary>
    /// <param name="query">搜索查询文本</param>
    /// <param name="knowledgeBaseId">知识库 ID</param>
    /// <param name="options">搜索选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>图谱搜索结果列表（按相关性降序排列）</returns>
    Task<IReadOnlyList<GraphSearchResult>> SearchAsync(
        string query, Guid knowledgeBaseId, GraphSearchOptions? options = null, CancellationToken ct = default);
}

/// <summary>
/// 图谱搜索结果
/// </summary>
/// <param name="NodeName">匹配的实体名称</param>
/// <param name="NodeType">实体类型（Person, Organization, Concept 等）</param>
/// <param name="RelatedNodes">关联的实体列表（含关系类型）</param>
/// <param name="Score">匹配得分（0-1）</param>
/// <param name="ContextSnippet">生成的上下文片段，可注入 LLM 提示词</param>
public sealed record GraphSearchResult(
    string NodeName,
    string NodeType,
    IReadOnlyList<RelatedNodeInfo> RelatedNodes,
    double Score,
    string ContextSnippet);

/// <summary>
/// 关联节点信息
/// </summary>
/// <param name="NodeName">关联实体名称</param>
/// <param name="NodeType">关联实体类型</param>
/// <param name="RelationType">关系类型</param>
/// <param name="Direction">关系方向（Outgoing: 当前节点→目标节点, Incoming: 源节点→当前节点）</param>
public sealed record RelatedNodeInfo(
    string NodeName,
    string NodeType,
    string RelationType,
    RelationDirection Direction);

/// <summary>
/// 关系方向
/// </summary>
public enum RelationDirection
{
    /// <summary>当前节点为源节点（当前节点 → 目标节点）</summary>
    Outgoing,

    /// <summary>当前节点为目标节点（源节点 → 当前节点）</summary>
    Incoming
}

/// <summary>
/// 图谱搜索选项
/// </summary>
/// <param name="MaxResults">最大返回结果数</param>
/// <param name="IncludeRelationships">是否包含关联实体</param>
/// <param name="MaxHops">关系扩展跳数（当前仅支持 1-hop）</param>
public sealed record GraphSearchOptions(
    int MaxResults = 5,
    bool IncludeRelationships = true,
    int MaxHops = 1);
