namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 透传相关性评分器 - 不做任何评估，标记所有结果为相关
/// </summary>
/// <remarks>
/// 作为 <see cref="IRelevanceGrader"/> 的默认实现，保证在无 LLM 评分器时系统正常运行。
/// </remarks>
public class NoOpRelevanceGrader : IRelevanceGrader
{
    /// <inheritdoc />
    public Task<List<GradedResult>> GradeAsync(string query, List<TextSearchResult> results, CancellationToken ct = default)
    {
        var graded = results.Select(r => new GradedResult
        {
            Result = r,
            RelevanceScore = r.Score ?? 1.0,
            IsRelevant = true
        }).ToList();

        return Task.FromResult(graded);
    }
}
