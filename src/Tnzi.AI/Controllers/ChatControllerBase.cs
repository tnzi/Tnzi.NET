

namespace Tnzi.AI.Controllers;

/// <summary>
/// 聊天控制器基类
/// 提供聊天 API 端点，所有方法支持重写
/// </summary>
[Route("chat")]
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "chat")]
public abstract class ChatControllerBase : ApiControllerBase
{
    protected readonly IChatService ChatService;

    /// <summary>
    /// 初始化聊天控制器基类
    /// </summary>
    protected ChatControllerBase(
        IChatService chatService)
        : base()
    {
        ChatService = Check.NotNull(chatService);
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<ChatResponseDto>> Chat([FromBody] ChatRequestDto request, CancellationToken cancellationToken = default)
    {
        var result = await ChatService.ChatAsync(request, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 流式发送消息（支持 SSE 和 NDJSON 格式）
    /// </summary>
    [HttpPost("stream")]
    public virtual async Task ChatStreaming([FromBody] ChatRequestDto request, CancellationToken cancellationToken = default)
    {
        var format = StreamingResponseWriter.NegotiateFormat(Request);
        StreamingResponseWriter.ConfigureResponse(Response, format);

        await foreach (var evt in ChatService.ChatStreamingAsync(request, cancellationToken))
        {
            await StreamingResponseWriter.WriteEventAsync(Response, evt, format, cancellationToken);
        }

        await StreamingResponseWriter.WriteDoneAsync(Response, format, cancellationToken);
    }

}
