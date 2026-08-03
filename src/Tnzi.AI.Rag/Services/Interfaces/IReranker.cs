namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 重排序器接口 - 对向量搜索结果进行二次排序以提高相关性
/// </summary>
/// <remarks>
/// <para>
/// 默认提供 <see cref="NoOpReranker"/> 透传实现。
/// 用户可注册商业 reranker（如 Cohere, Jina, bge-reranker）替换。
/// </para>
/// </remarks>
public interface IReranker
{
    /// <summary>
    /// 对向量搜索结果进行重排序
    /// </summary>
    /// <param name="query">原始查询文本</param>
    /// <param name="results">向量搜索返回的初始结果</param>
    /// <param name="topK">重排序后返回的最大结果数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>重排序后的结果列表</returns>
    Task<List<VectorSearchResult>> RerankAsync(
        string query,
        List<VectorSearchResult> results,
        int topK,
        CancellationToken ct = default);
}
