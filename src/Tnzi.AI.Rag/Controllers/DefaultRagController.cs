namespace Tnzi.AI.Rag.Controllers;

/// <summary>
/// RAG 控制器 - 提供检索增强生成的查询和聊天 API
/// <para>
/// Query: 单轮 Q&amp;A（无历史上下文），适合独立问答。
/// Chat: 多轮对话（带历史上下文），支持流式响应。
/// </para>
/// </summary>
[DefaultController]
[Route("rag")]
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultRagController : ApiControllerBase
{
    protected readonly IRagQueryEngine QueryEngine;
    protected readonly IRagChatEngine ChatEngine;

    public DefaultRagController(IRagQueryEngine queryEngine, IRagChatEngine chatEngine)
    {
        QueryEngine = Check.NotNull(queryEngine);
        ChatEngine = Check.NotNull(chatEngine);
    }

    /// <summary>
    /// RAG 查询（单轮 Q&amp;A，无历史上下文）
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<RagQueryResult>> Query([FromBody] RagQueryRequest request, CancellationToken ct)
    {
        var result = await QueryEngine.QueryAsync(request, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// RAG 聊天（多轮对话，带历史上下文）
    /// </summary>
    [HttpPost("chat")]
    public virtual async Task<ApiResult<RagChatResult>> Chat([FromBody] RagChatRequest request, CancellationToken ct)
    {
        var result = await ChatEngine.ChatAsync(request, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// RAG 流式聊天（多轮对话，SSE/NDJSON 流式响应）
    /// </summary>
    [HttpPost("chat/stream")]
    public virtual async Task ChatStreaming([FromBody] RagChatRequest request, CancellationToken ct)
    {
        var format = StreamingResponseWriter.NegotiateFormat(Request);
        var stream = ChatEngine.ChatStreamingAsync(request, ct);
        await StreamingResponseWriter.WriteFullStreamAsync(Response, stream, format, ct);
    }
}
