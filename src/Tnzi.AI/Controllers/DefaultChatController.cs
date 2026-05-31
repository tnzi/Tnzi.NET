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

}
