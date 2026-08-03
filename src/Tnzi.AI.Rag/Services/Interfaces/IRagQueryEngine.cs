namespace Tnzi.AI.Rag.Services;

/// <summary>
/// RAG 查询引擎 - 单轮 Q&amp;A（无历史上下文）
/// <para>
/// 流程：IRagRetriever.RetrieveAsync → 格式化上下文 → 单次 LLM 调用 → 返回回答 + 引用。
/// 适用于独立问答场景，不维护对话历史。
/// </para>
/// </summary>
public interface IRagQueryEngine
{
    /// <summary>
    /// 执行 RAG 查询
    /// </summary>
    /// <param name="request">查询请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含回答和引用的结果</returns>
    Task<Result<RagQueryResult>> QueryAsync(RagQueryRequest request, CancellationToken ct = default);
}
