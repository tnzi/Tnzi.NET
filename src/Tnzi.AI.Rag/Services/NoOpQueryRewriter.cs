namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 透传查询改写器 — 不做任何改写，原样返回查询
/// </summary>
/// <remarks>
/// 作为 <see cref="IQueryRewriter"/> 的默认实现，保证在无 LLM 改写器时系统正常运行。
/// </remarks>
public class NoOpQueryRewriter : IQueryRewriter
{
    /// <inheritdoc />
    public Task<string> RewriteAsync(string query, CancellationToken ct = default)
    {
        return Task.FromResult(query);
    }
}
