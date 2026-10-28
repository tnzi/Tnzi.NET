namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 关键词搜索提供者接口 - 用于混合搜索中的关键词匹配部分
/// </summary>
/// <remarks>
/// <para>
/// 定义关键词搜索的标准接口，配合 <c>IVectorStore</c> 实现混合检索（Hybrid Search）。
/// 框架提供 <c>InMemoryKeywordSearchProvider</c>（基于 TF-IDF）和
/// <c>PgFullTextSearchProvider</c>（基于 PostgreSQL tsvector）两种实现。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Keyword search is in preview")]
public interface IKeywordSearchProvider
{
    /// <summary>
    /// 执行关键词搜索
    /// </summary>
    /// <param name="query">搜索查询文本</param>
    /// <param name="topK">返回的最大结果数</param>
    /// <param name="knowledgeBaseId">指定知识库 ID（null 则跨所有知识库搜索）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>按相关性降序排列的搜索结果</returns>
    Task<List<KeywordSearchResult>> SearchAsync(string query, int topK, Guid? knowledgeBaseId = null, CancellationToken ct = default);
}

/// <summary>
/// 关键词搜索结果
/// </summary>
[ExperimentalApi(Reason = "Keyword search is in preview")]
public class KeywordSearchResult
{
    /// <summary>块 ID</summary>
    public Guid ChunkId { get; set; }

    /// <summary>块文本内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>所属文档 ID</summary>
    public Guid DocumentId { get; set; }

    /// <summary>所属知识库 ID</summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>
    /// 块在文档中的索引。父文档检索按它取相邻窗口；提供者给不出时留空，混合检索会按 <see cref="ChunkId"/> 回查分块表补上。
    /// </summary>
    /// <remarks>
    /// ★ 此前没有这个字段，混合检索把仅关键词命中的块一律记作第 0 块：开启父文档检索后，注入上下文的是文档开头的几块
    /// （封面、目录），真正命中的段落被换掉。
    /// </remarks>
    public int? ChunkIndex { get; set; }

    /// <summary>TF-IDF 或全文搜索相关性得分</summary>
    public double Score { get; set; }
}
