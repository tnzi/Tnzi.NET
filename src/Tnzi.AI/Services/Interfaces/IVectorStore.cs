
namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 向量存储接口 — RAG 向量搜索和管理的抽象层
/// </summary>
/// <remarks>
/// <para>
/// 定义面向 RAG 场景的向量存储标准接口，使用 <c>float[]</c> 作为向量类型，
/// 保持数据库无关性。具体实现可对接 pgvector、Redis、Qdrant、Pinecone 等后端。
/// </para>
/// <para>
/// Tnzi.AI 模块提供 <see cref="NoOpTextSearchService"/> 作为默认空实现。
/// RAG 模块（Tnzi.AI.Rag）提供基于 pgvector 的实现 <c>PgVectorStore</c>。
/// 用户也可注册自定义实现替换。
/// </para>
/// </remarks>
public interface IVectorStore
{
    /// <summary>
    /// 向量相似度搜索
    /// </summary>
    /// <param name="queryVector">查询向量（float[] 格式）</param>
    /// <param name="topK">返回的最大结果数</param>
    /// <param name="knowledgeBaseId">指定知识库 ID（null 则跨所有启用的知识库搜索）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>按相似度降序排列的搜索结果</returns>
    Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        Guid? knowledgeBaseId = null,
        CancellationToken ct = default);

    /// <summary>
    /// 向量相似度搜索（带 metadata 过滤）
    /// </summary>
    /// <param name="queryVector">查询向量</param>
    /// <param name="topK">返回的最大结果数</param>
    /// <param name="knowledgeBaseId">指定知识库 ID</param>
    /// <param name="metadataFilter">metadata 键值过滤条件（JSON 字段内匹配）</param>
    /// <param name="ct">取消令牌</param>
    Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        Guid? knowledgeBaseId,
        Dictionary<string, string>? metadataFilter,
        CancellationToken ct = default)
    {
        // 默认实现: 忽略 metadata 过滤，委托给基础搜索
        return SearchAsync(queryVector, topK, knowledgeBaseId, ct);
    }

    /// <summary>
    /// 插入或更新向量数据（供非数据库后端使用）
    /// </summary>
    /// <param name="chunkId">块 ID</param>
    /// <param name="embedding">嵌入向量</param>
    /// <param name="documentId">所属文档 ID</param>
    /// <param name="knowledgeBaseId">所属知识库 ID</param>
    /// <param name="content">块文本内容</param>
    /// <param name="chunkIndex">块在文档中的索引</param>
    /// <param name="metadata">额外元数据（JSON 格式）</param>
    /// <param name="ct">取消令牌</param>
    Task UpsertAsync(Guid chunkId, float[] embedding, Guid documentId, Guid knowledgeBaseId,
        string content, int chunkIndex, string? metadata = null, CancellationToken ct = default)
    {
        throw new NotSupportedException("This vector store does not support direct upsert.");
    }

    /// <summary>
    /// 删除指定文档的所有向量块
    /// </summary>
    /// <param name="documentId">文档 ID</param>
    /// <param name="ct">取消令牌</param>
    Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// 删除指定知识库的所有向量块
    /// </summary>
    /// <param name="knowledgeBaseId">知识库 ID</param>
    /// <param name="ct">取消令牌</param>
    Task DeleteByKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken ct = default);
}

/// <summary>
/// 向量搜索结果
/// </summary>
public class VectorSearchResult
{
    /// <summary>块 ID</summary>
    public Guid Id { get; set; }

    /// <summary>块文本内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>所属文档 ID</summary>
    public Guid DocumentId { get; set; }

    /// <summary>所属知识库 ID</summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>块在文档中的索引</summary>
    public int ChunkIndex { get; set; }

    /// <summary>额外元数据（JSON 格式）</summary>
    public string? Metadata { get; set; }

    /// <summary>父块 ID（层级切块时的父子关系）</summary>
    public Guid? ParentChunkId { get; set; }

    /// <summary>相似度得分（0-1，越高越相似）</summary>
    public double Score { get; set; }
}
