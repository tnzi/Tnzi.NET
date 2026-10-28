
namespace Tnzi.AI.Services;

/// <summary>
/// 向量存储接口 - RAG 向量搜索和管理的抽象层
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
    /// 本存储是否直接检索 RAG 模块的分块表（摄取经 EF 写入的 <c>{prefix}_DocumentChunk</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为 <see langword="true"/>（<c>PgVectorStore</c>）时，写下分块表即完成入库，删除分块表的行即完成删除。
    /// </para>
    /// <para>
    /// 为 <see langword="false"/>（默认，含 <c>InMemoryVectorStore</c> 与 Qdrant、Milvus 这类外部向量库）时，RAG 摄取在写完分块表之后
    /// 还对每个分块调用 <see cref="UpsertAsync"/>，重建索引同样回写，删除文档 / 知识库时分块表与本存储各删一次。
    /// 外部向量库因此必须实现 <see cref="UpsertAsync"/>：默认实现抛 <see cref="NotSupportedException"/>，摄取据此失败，
    /// 而不是标记完成却永远检索不到。
    /// </para>
    /// <para>
    /// ★ 此前摄取只写分块表、从不调用 <see cref="UpsertAsync"/>：<c>AI:Rag:VectorStoreProvider=InMemory</c> 与自定义向量库下，
    /// 文档显示「已完成 / N 块」，检索恒为空；删除只删本存储（本来就空），分块表的行永不删除。
    /// 自己读分块表的自定义实现覆写本属性返回 <see langword="true"/>。
    /// </para>
    /// </remarks>
    bool UsesRagChunkTable => false;

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
