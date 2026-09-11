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

    /// <summary>
    /// LLM 的响应<b>无法解析</b>。与"确实一个实体都没有"必须可区分：两者都返回空集合，
    /// 但前者意味着这段文本的图谱从未被抽取过，而调用方看到的却是"抽完了，没东西"。
    /// </summary>
    public bool ResponseUnparsable { get; init; }

    /// <summary>响应无法解析时的结果。</summary>
    public static GraphExtractionResult Unparsable { get; } = new([], []) { ResponseUnparsable = true };
}
