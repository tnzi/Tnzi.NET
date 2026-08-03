namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 查询改写接口 - 将用户查询改写为更适合语义搜索的形式
/// </summary>
/// <remarks>
/// <para>
/// 默认提供 <see cref="NoOpQueryRewriter"/> 透传实现。
/// 用户可注册 <see cref="LlmQueryRewriter"/>（基于 LLM 改写）或自定义实现替换。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Query rewriting is in preview")]
public interface IQueryRewriter
{
    /// <summary>
    /// 将用户查询改写为更适合语义搜索的形式
    /// </summary>
    /// <param name="query">原始用户查询</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>改写后的查询文本</returns>
    Task<string> RewriteAsync(string query, CancellationToken ct = default);
}
