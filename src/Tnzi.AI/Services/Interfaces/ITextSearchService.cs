namespace Tnzi.AI.Services;

/// <summary>
/// 文本搜索服务接口 - 用于 RAG 向量检索
/// </summary>
/// <remarks>
/// <para>
/// 定义文本搜索的标准接口，供 TextSearchProvider 使用。
/// 用户需要实现此接口来连接到具体的向量存储（如 Redis、Qdrant、Pinecone 等）。
/// </para>
/// <para>
/// 框架提供默认的空实现 NoOpTextSearchService，不执行实际搜索。
/// </para>
/// </remarks>
public interface ITextSearchService
{
    /// <summary>
    /// 执行语义搜索
    /// </summary>
    /// <param name="query">搜索查询文本</param>
    /// <param name="maxResults">最大返回结果数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>搜索结果列表</returns>
    Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default);

    /// <summary>
    /// 执行语义搜索（含过滤条件）
    /// </summary>
    /// <param name="query">搜索查询文本</param>
    /// <param name="filter">过滤条件（可选）</param>
    /// <param name="maxResults">最大返回结果数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>搜索结果列表</returns>
    Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        TextSearchFilter? filter,
        int maxResults = 5,
        CancellationToken ct = default)
        => SearchAsync(query, maxResults, ct);

    /// <summary>
    /// 本实现是否支持对话范围检索：检索的是已索引的聊天历史，并且真正按 <see cref="TextSearchFilter.UserId"/> /
    /// <see cref="TextSearchFilter.ThreadId"/>（以及 AgentId / CollectionName）限定结果。
    /// </summary>
    /// <remarks>
    /// 聊天历史记忆（<c>ChatHistoryMemoryProvider</c>）只在它为 true 时工作。默认 false：框架内置的检索实现
    /// （向量检索与混合检索）只认 <see cref="TextSearchFilter.KnowledgeBaseIds"/>，其余字段原样忽略，
    /// 拿去做「历史对话」检索会退化成跨全部知识库的检索，绕过 Agent 的知识库授权，结果还被当作以前的对话注入。
    /// 提供聊天历史索引的实现覆写为 true，并保证上述字段确实限定了结果。
    /// </remarks>
    bool SupportsConversationScope => false;
}
