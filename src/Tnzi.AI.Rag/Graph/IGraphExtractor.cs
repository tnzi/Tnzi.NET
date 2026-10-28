namespace Tnzi.AI.Rag.Graph;

/// <summary>
/// 知识图谱实体关系提取器
/// </summary>
public interface IGraphExtractor
{
    /// <summary>
    /// 从文本中提取实体和关系，存储到知识图谱
    /// </summary>
    /// <param name="text">待提取的文本内容</param>
    /// <param name="knowledgeBaseId">所属知识库 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>提取结果（节点和边）</returns>
    Task<GraphExtractionResult> ExtractAsync(string text, Guid knowledgeBaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从一份文档的文本中提取实体和关系，存储到知识图谱，并把写下的每个节点与边标记为来自这份文档
    /// （<see cref="KnowledgeGraphNode.DocumentId"/> / <see cref="KnowledgeGraphEdge.DocumentId"/>）。
    /// </summary>
    /// <remarks>
    /// 摄取管线调用的是这个重载：删除文档时按来源清掉它的图谱，重新摄取同一文档时先删后插，都靠这一列。
    /// 默认实现转调不带文档的旧签名，只为让既有的自定义抽取器照常编译与工作，但那样写下的行没有来源，
    /// 删除文档时清不掉、重复摄取会叠加。自定义抽取器应实现本重载。
    /// </remarks>
    /// <param name="text">待提取的文本内容</param>
    /// <param name="knowledgeBaseId">所属知识库 ID</param>
    /// <param name="documentId">来源文档 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>提取结果（节点和边）</returns>
    Task<GraphExtractionResult> ExtractAsync(string text, Guid knowledgeBaseId, Guid documentId, CancellationToken cancellationToken = default)
        => ExtractAsync(text, knowledgeBaseId, cancellationToken);
}
