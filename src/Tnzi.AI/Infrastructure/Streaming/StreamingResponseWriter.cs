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
    /// 完整的流式传输生命周期管理 — 封装 pre-stream、IsDone 早发、主循环、drain、dispose。
    /// Pre-stream 阶段（首事件获取前）异常不会被捕获，由 ASP.NET Core 异常处理器正常返回 HTTP 错误。
    /// </summary>
    public static async Task WriteFullStreamAsync(
        HttpResponse response,
        IAsyncEnumerable<StreamEvent> stream,
        StreamingFormat format,
        CancellationToken ct = default)
    {
        var enumerator = stream.GetAsyncEnumerator(ct);

        try
        {
            // Pre-stream: headers 未提交，异常可正常返回 HTTP 错误
            if (!await enumerator.MoveNextAsync())
            {
                await enumerator.DisposeAsync();
                ConfigureResponse(response, format);
                await WriteDoneAsync(response, format, ct);
                return;
            }
        }
        catch
        {
            await enumerator.DisposeAsync();
            throw;
        }

        // 提交 headers，进入流式传输模式
        ConfigureResponse(response, format);

        var firstEvent = enumerator.Current;
        await WriteEventAsync(response, firstEvent, format, ct);
        if (firstEvent.IsToolCall)
        {
            await WriteHeartbeatAsync(response, format, ct);
        }

        var streamDone = firstEvent.IsDone;
        if (streamDone)
        {
            await WriteDoneAsync(response, format, CancellationToken.None);
        }

        if (!streamDone)
        {
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    var evt = enumerator.Current;
                    await WriteEventAsync(response, evt, format, ct);
                    if (evt.IsToolCall)
                    {
                        await WriteHeartbeatAsync(response, format, ct);
                    }

                    if (evt.IsDone)
                    {
                        streamDone = true;
                        await WriteDoneAsync(response, format, CancellationToken.None);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var errorMsg = ex is BusinessException ? ex.Message : "An internal error occurred";
                    await WriteErrorAsync(response, errorMsg, ErrorCodes.StreamingFailed, format, CancellationToken.None);
                }
                catch
                {
                    // Response 可能已被客户端断开
                }
            }
        }

        // Drain 剩余事件并 dispose
        try
        {
            while (await enumerator.MoveNextAsync()) { }
        }
        catch
        {
            // Drain 阶段异常静默处理
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (!streamDone)
        {
            await WriteDoneAsync(response, format, CancellationToken.None);
        }
    }

    /// <summary>
    /// 完整的流式传输生命周期管理（泛型版本）— 支持自定义事件类型，通过 mapper 转换后写入。
    /// 适用于源事件类型非 StreamEvent 的流式端点（如 Workflow）。
    /// </summary>
    /// <typeparam name="TSource">源事件类型</typeparam>
    /// <typeparam name="TOutput">输出事件类型（序列化到客户端）</typeparam>
    public static async Task WriteFullStreamAsync<TSource, TOutput>(
        HttpResponse response,
        IAsyncEnumerable<TSource> stream,
        Func<TSource, TOutput> mapper,
        Func<TOutput, bool> isDone,
        Func<Exception, TOutput> errorFactory,
        StreamingFormat format,
        CancellationToken ct = default)
    {
        var enumerator = stream.GetAsyncEnumerator(ct);

        try
        {
            if (!await enumerator.MoveNextAsync())
            {
                await enumerator.DisposeAsync();
                ConfigureResponse(response, format);
                await WriteDoneAsync(response, format, ct);
                return;
            }
        }
        catch
        {
            await enumerator.DisposeAsync();
            throw;
        }

        ConfigureResponse(response, format);

        var firstEvent = mapper(enumerator.Current);
        await WriteEventAsync(response, firstEvent, format, ct);

        var streamDone = isDone(firstEvent);
        if (streamDone)
        {
            await WriteDoneAsync(response, format, CancellationToken.None);
        }

        if (!streamDone)
        {
            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    var evt = mapper(enumerator.Current);
                    await WriteEventAsync(response, evt, format, ct);

                    if (isDone(evt))
                    {
                        streamDone = true;
                        await WriteDoneAsync(response, format, CancellationToken.None);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var errorEvent = errorFactory(ex);
                    await WriteEventAsync(response, errorEvent, format, CancellationToken.None);
                }
                catch
                {
                    // Response 可能已被客户端断开
                }
            }
        }

        try
        {
            while (await enumerator.MoveNextAsync()) { }
        }
        catch
        {
            // Drain 阶段异常静默处理
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (!streamDone)
        {
            await WriteDoneAsync(response, format, CancellationToken.None);
        }
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
