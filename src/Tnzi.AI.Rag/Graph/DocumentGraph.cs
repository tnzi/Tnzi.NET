namespace Tnzi.AI.Rag.Graph;

/// <summary>
/// 一份文档在知识图谱里留下的节点与边（按 <see cref="KnowledgeGraphNode.DocumentId"/> / <see cref="KnowledgeGraphEdge.DocumentId"/> 归属）。
/// </summary>
/// <remarks>
/// 两处要用它，语义相同：删除文档时清掉它的图谱（否则被删文档的实体描述继续经图谱片段进入回答），
/// 摄取一份文档前清掉它上一次留下的图谱（否则重试 / 重新摄取每次都叠加一份同名节点）。
/// </remarks>
internal static class DocumentGraph
{
    /// <summary>删除指定文档的全部图谱节点与边。先删边再删节点：边对节点有外键。</summary>
    public static async Task RemoveAsync(
        IRepository<KnowledgeGraphEdge, Guid> edgeRepository,
        IRepository<KnowledgeGraphNode, Guid> nodeRepository,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await edgeRepository.DeleteAsync(e => e.DocumentId == documentId, cancellationToken);
        await nodeRepository.DeleteAsync(n => n.DocumentId == documentId, cancellationToken);
    }
}
