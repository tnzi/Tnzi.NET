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
}
