namespace Tnzi.AI.Rag.Services.Interfaces;

/// <summary>
/// RAG 检索器接口 — 封装查询改写 → 嵌入生成 → 向量搜索 → 后处理的共享检索管线
/// </summary>
public interface IRagRetriever
{
    /// <summary>
    /// 执行 RAG 检索：查询改写 → 嵌入生成 → 向量搜索 → 后处理 → 过滤
    /// </summary>
    /// <param name="query">用户查询文本</param>
    /// <param name="options">检索选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>检索结果列表（按相关性降序排列）</returns>
    Task<List<RetrievalResult>> RetrieveAsync(string query, RagRetrievalOptions? options = null, CancellationToken ct = default);
}
