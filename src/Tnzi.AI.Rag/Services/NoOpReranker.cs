namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 透传重排序器 — 不做任何重排序，仅截取 topK 结果
/// </summary>
/// <remarks>
/// 作为 <see cref="IReranker"/> 的默认实现，保证在无商业 reranker 时系统正常运行。
/// </remarks>
public class NoOpReranker : IReranker
{
    /// <inheritdoc />
    public Task<List<VectorSearchResult>> RerankAsync(
        string query,
        List<VectorSearchResult> results,
        int topK,
        CancellationToken ct = default)
    {
        return Task.FromResult(results.Take(topK).ToList());
    }
}
