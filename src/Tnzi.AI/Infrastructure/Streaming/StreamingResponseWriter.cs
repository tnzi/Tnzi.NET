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
    }

    /// <summary>
    /// 写入一个流式事件到 HttpResponse
    /// </summary>
    public static async Task WriteEventAsync(HttpResponse response, StreamEvent evt, StreamingFormat format, CancellationToken ct = default)
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
}
