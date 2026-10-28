namespace Tnzi.AI.Controllers;

/// <summary>
/// 聊天控制器
/// 提供聊天 API 端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("chat")]
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultChatController : ApiControllerBase
{
    protected readonly IChatService ChatService;

    /// <summary>
    /// 初始化聊天控制器
    /// </summary>
    public DefaultChatController(IChatService chatService)
    {
        ChatService = Check.NotNull(chatService);
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<ChatResponseDto>> Chat([FromBody] ChatRequestDto request, CancellationToken cancellationToken = default)
    {
        // 已认证用户强制使用自身 ID，防止客户端伪造
        request.UserId = CurrentUser?.Id ?? request.UserId;
        var result = await ChatService.ChatAsync(request, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 流式发送消息（支持 SSE 和 NDJSON 格式）
    /// </summary>
    [HttpPost("stream")]
    public virtual async Task ChatStreaming([FromBody] ChatRequestDto request, CancellationToken cancellationToken = default)
    {
        request.UserId = CurrentUser?.Id ?? request.UserId;
        var format = StreamingResponseWriter.NegotiateFormat(Request);
        var stream = ChatService.ChatStreamingAsync(request, cancellationToken);
        await StreamingResponseWriter.WriteFullStreamAsync(Response, stream, format, cancellationToken);
    }

    /// <summary>
    /// 线程上还没结束的运行（绑定了外部 CLI 运行时的 Agent 的回合）。刷新或断线之后，客户端据此重新接上正在写的回复。
    /// </summary>
    [HttpGet("threads/{threadId:guid}/runs")]
    public virtual async Task<ApiResult<List<ChatRunDto>>> GetActiveRuns(Guid threadId, CancellationToken cancellationToken = default)
    {
        var userId = AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());
        var result = await ChatService.GetActiveRunsAsync(threadId, userId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重新接上一次运行的流（SSE / NDJSON，事件形状与 <c>chat/stream</c> 相同）：从头，或从 <paramref name="fromSequence"/> 之后。
    /// 运行已经结束时补完剩下的事件后结束。
    /// </summary>
    [HttpGet("runs/{runId:guid}/stream")]
    public virtual async Task AttachStream(Guid runId, [FromQuery] int fromSequence = 0, CancellationToken cancellationToken = default)
    {
        var format = StreamingResponseWriter.NegotiateFormat(Request);
        var stream = ChatService.AttachStreamingAsync(runId, fromSequence, cancellationToken);
        await StreamingResponseWriter.WriteFullStreamAsync(Response, stream, format, cancellationToken);
    }

    /// <summary>
    /// 在服务端取消一次运行（不只是断开连接）。已经写出的部分会作为停下的回复留在线程里。
    /// </summary>
    [HttpPost("runs/{runId:guid}/cancel")]
    public virtual async Task<ApiResult> CancelRun(Guid runId, CancellationToken cancellationToken = default)
    {
        var result = await ChatService.CancelRunAsync(runId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 回答一次运行的权限请求（流上的 <c>permission_request</c> 事件）：从它给的 <c>options</c> 里挑一个。
    /// </summary>
    [HttpPost("runs/{runId:guid}/permissions/{requestId:guid}")]
    public virtual async Task<ApiResult> AnswerRunPermission(
        Guid runId, Guid requestId, [FromBody] CliPermissionAnswerDto answer, CancellationToken cancellationToken = default)
    {
        var result = await ChatService.AnswerRunPermissionAsync(runId, requestId, answer, cancellationToken);
        return result.ToApiResult();
    }
}
