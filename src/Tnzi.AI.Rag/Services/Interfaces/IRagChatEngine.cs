namespace Tnzi.AI.Rag.Services.Interfaces;

/// <summary>
/// RAG 聊天引擎 - 多轮对话（带历史上下文）
/// <para>
/// 委托给 IAgentRuntime.RunAsync，通过 ThreadId 维护对话历史，
/// 结合 RAG 检索上下文注入。支持完整的 AI 中间件管道。
/// </para>
/// </summary>
public interface IRagChatEngine
{
    /// <summary>
    /// 执行 RAG 聊天（非流式）
    /// </summary>
    /// <param name="request">聊天请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含回答、引用和线程 ID 的结果</returns>
    Task<Result<RagChatResult>> ChatAsync(RagChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// 执行 RAG 聊天（流式）
    /// </summary>
    /// <param name="request">聊天请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>流式 SSE 事件序列</returns>
    IAsyncEnumerable<StreamEvent> ChatStreamingAsync(RagChatRequest request, CancellationToken ct = default);
}
