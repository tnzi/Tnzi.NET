namespace Tnzi.AI.Rag.Services.Interfaces;

/// <summary>
/// 文档相关性评分接口 - 评估检索结果与查询的相关性
/// </summary>
/// <remarks>
/// <para>
/// 默认提供 <see cref="NoOpRelevanceGrader"/> 透传实现（标记所有结果为相关）。
/// 用户可注册 <see cref="LlmRelevanceGrader"/>（基于 LLM 评估）或自定义实现替换。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Relevance grading is in preview")]
public interface IRelevanceGrader
{
    /// <summary>
    /// 评估检索结果与查询的相关性
    /// </summary>
    /// <param name="query">原始用户查询</param>
    /// <param name="results">待评估的搜索结果列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>带相关性评分的结果列表</returns>
    Task<List<GradedResult>> GradeAsync(string query, List<TextSearchResult> results, CancellationToken ct = default);
}

/// <summary>
/// 带相关性评分的搜索结果
/// </summary>
public class GradedResult
{
    /// <summary>
    /// 原始搜索结果
    /// </summary>
    public TextSearchResult Result { get; set; } = null!;

    /// <summary>
    /// 相关性评分（0-1，越高越相关）
    /// </summary>
    public double RelevanceScore { get; set; }

    /// <summary>
    /// 是否与查询相关
    /// </summary>
    public bool IsRelevant { get; set; }
}
