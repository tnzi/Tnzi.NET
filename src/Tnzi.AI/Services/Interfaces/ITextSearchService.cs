namespace Tnzi.AI.Services.Interfaces;

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
}

/// <summary>
/// 文本搜索过滤条件
/// </summary>
public class TextSearchFilter
{
    /// <summary>
    /// 用户 ID 过滤
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Agent ID 过滤
    /// </summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// 线程 ID 过滤
    /// </summary>
    public string? ThreadId { get; set; }

    /// <summary>
    /// 集合名称过滤
    /// </summary>
    public string? CollectionName { get; set; }

    /// <summary>
    /// 额外元数据过滤
    /// </summary>
    public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>
/// 文本搜索结果
/// </summary>
public class TextSearchResult
{
    /// <summary>
    /// 结果文本内容
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 来源文档名称（可选）
    /// </summary>
    public string? SourceName { get; set; }

    /// <summary>
    /// 来源文档链接（可选）
    /// </summary>
    public string? SourceLink { get; set; }

    /// <summary>
    /// 相似度得分（可选）
    /// </summary>
    public double? Score { get; set; }

    /// <summary>
    /// 额外元数据
    /// </summary>
    public Dictionary<string, object?>? Metadata { get; set; }
}
