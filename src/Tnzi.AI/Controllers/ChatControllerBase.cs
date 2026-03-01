

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
    protected ChatControllerBase(IChatService chatService)
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
        // 已认证用户强制使用自身 ID，防止客户端伪造
        request.UserId = CurrentUser?.Id ?? request.UserId;
        var format = StreamingResponseWriter.NegotiateFormat(Request);

        // 获取流式枚举器，pre-stream 阶段异常返回正常 HTTP 错误
        var stream = ChatService.ChatStreamingAsync(request, cancellationToken);
        var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        try
        {
            // 尝试获取第一个事件（此时 headers 尚未提交）
            if (!await enumerator.MoveNextAsync())
            {
                StreamingResponseWriter.ConfigureResponse(Response, format);
                await StreamingResponseWriter.WriteDoneAsync(Response, format, cancellationToken);
                return;
            }
        }
        catch (BusinessException)
        {
            // Pre-stream 阶段的业务异常交由框架异常过滤器处理
            throw;
        }

        // 提交 headers，进入流式传输模式
        StreamingResponseWriter.ConfigureResponse(Response, format);

        // 写入第一个事件
        var firstEvent = enumerator.Current;
        await StreamingResponseWriter.WriteEventAsync(Response, firstEvent, format, cancellationToken);
        if (firstEvent.IsToolCall)
        {
            await StreamingResponseWriter.WriteHeartbeatAsync(Response, format, cancellationToken);
        }

        // 继续写入后续事件
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                var evt = enumerator.Current;
                await StreamingResponseWriter.WriteEventAsync(Response, evt, format, cancellationToken);
                if (evt.IsToolCall)
                {
                    await StreamingResponseWriter.WriteHeartbeatAsync(Response, format, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            // Headers 已提交，无法返回 HTTP 错误码，写入 error event
            await StreamingResponseWriter.WriteErrorAsync(Response, ex.Message, ErrorCodes.StreamingFailed, format, CancellationToken.None);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        await StreamingResponseWriter.WriteDoneAsync(Response, format, CancellationToken.None);
    }

}
