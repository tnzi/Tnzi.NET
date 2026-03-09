namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// 流式响应写入器 — 支持 SSE 和 NDJSON 格式
/// </summary>
public static class StreamingResponseWriter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 根据 Accept header 协商流式格式
    /// </summary>
    public static StreamingFormat NegotiateFormat(HttpRequest request)
    {
        var accept = request.Headers.Accept.ToString();

        if (accept.Contains("application/x-ndjson", StringComparison.OrdinalIgnoreCase))
            return StreamingFormat.NDJSON;

        // 默认 SSE
        return StreamingFormat.SSE;
    }

    /// <summary>
    /// 获取流式格式对应的 Content-Type
    /// </summary>
    public static string GetContentType(StreamingFormat format)
    {
        return format switch
        {
            StreamingFormat.SSE => "text/event-stream",
            StreamingFormat.NDJSON => "application/x-ndjson",
            _ => "text/event-stream"
        };
    }

    /// <summary>
    /// 配置 HttpResponse 的 headers 用于流式传输
    /// </summary>
    public static void ConfigureResponse(HttpResponse response, StreamingFormat format)
    {
        response.ContentType = GetContentType(format);
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no"; // Nginx 反代支持

        // 禁用 ASP.NET Core 响应缓冲（如 ResponseCompression 等中间件）
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    }

    /// <summary>
    /// 写入一个流式事件到 HttpResponse
    /// </summary>
    public static async Task WriteEventAsync(HttpResponse response, StreamEvent evt, StreamingFormat format, CancellationToken ct = default)
    {
        await WriteEventAsync<StreamEvent>(response, evt, format, ct);
    }

    /// <summary>
    /// 写入一个任意类型的流式事件到 HttpResponse
    /// </summary>
    public static async Task WriteEventAsync<T>(HttpResponse response, T evt, StreamingFormat format, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(evt, _jsonOptions);

        switch (format)
        {
            case StreamingFormat.SSE:
                await response.WriteAsync($"data: {json}\n\n", ct);
                break;
            case StreamingFormat.NDJSON:
                await response.WriteAsync($"{json}\n", ct);
                break;
        }

        await response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// 写入错误事件到 HttpResponse
    /// </summary>
    public static async Task WriteErrorAsync(HttpResponse response, string errorMessage, string? errorCode, StreamingFormat format, CancellationToken ct = default)
    {
        var errorEvent = new StreamEvent
        {
            IsError = true,
            IsDone = true,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode
        };
        await WriteEventAsync(response, errorEvent, format, ct);
    }

    /// <summary>
    /// 写入心跳信号（防止反向代理/CDN 超时断连）
    /// </summary>
    public static async Task WriteHeartbeatAsync(HttpResponse response, StreamingFormat format, CancellationToken ct = default)
    {
        switch (format)
        {
            case StreamingFormat.SSE:
                await response.WriteAsync(": heartbeat\n\n", ct);
                break;
            case StreamingFormat.NDJSON:
                await response.WriteAsync("\n", ct);
                break;
        }

        await response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// 写入 SSE 的 [DONE] 终止信号
    /// </summary>
    public static async Task WriteDoneAsync(HttpResponse response, StreamingFormat format, CancellationToken ct = default)
    {
        if (format == StreamingFormat.SSE)
        {
            await response.WriteAsync("data: [DONE]\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
        // NDJSON 不需要特殊终止符
    }

    /// <summary>
    /// 写入流式事件序列，并在相邻事件间隔超过阈值时自动插入心跳信号，防止反向代理超时断连。
    /// </summary>
    /// <remarks>
    /// 心跳在相邻事件之间检测注入。若 LLM 完全无响应（不产生任何事件），
    /// 应在 Agent 层设置超时（AgentExecutorOptions.ToolTimeoutSeconds）控制。
    /// </remarks>
    public static async Task WriteStreamWithHeartbeatAsync(
        HttpResponse response,
        IAsyncEnumerable<StreamEvent> events,
        StreamingFormat format,
        TimeSpan? heartbeatInterval = null,
        CancellationToken ct = default)
    {
        var interval = heartbeatInterval ?? TimeSpan.FromSeconds(15);
        var lastWrite = DateTime.UtcNow;

        await foreach (var evt in events.WithCancellation(ct))
        {
            var now = DateTime.UtcNow;
            if (now - lastWrite >= interval)
            {
                await WriteHeartbeatAsync(response, format, ct);
                lastWrite = now;
            }

            await WriteEventAsync(response, evt, format, ct);
            lastWrite = DateTime.UtcNow;
        }
    }
}
