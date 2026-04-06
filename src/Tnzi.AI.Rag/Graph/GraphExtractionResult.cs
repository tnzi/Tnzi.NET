namespace Tnzi.AI.Rag.Graph;

/// <summary>
/// 图谱提取结果
/// </summary>
/// <param name="Nodes">提取的实体节点</param>
/// <param name="Edges">提取的关系边</param>
public sealed record GraphExtractionResult(
    IReadOnlyList<KnowledgeGraphNode> Nodes,
    IReadOnlyList<KnowledgeGraphEdge> Edges)
{
    /// <summary>
    /// 空结果
    /// </summary>
    public static GraphExtractionResult Empty { get; } = new([], []);
}
