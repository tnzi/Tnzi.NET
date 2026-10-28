namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// 流式响应写入器 - 支持 SSE 和 NDJSON 格式
/// </summary>
public static class StreamingResponseWriter
{
    /// <summary>
    /// 默认 JSON 序列化选项（camelCase + 忽略 null + 枚举按成员名）。
    /// ⚠️ 共享只读实例 —— 首次序列化后会被 .NET 冻结，<b>不要直接修改它的属性</b>
    /// （会抛 <see cref="InvalidOperationException"/>）。需要定制时请复制后再传入
    /// <see cref="WriteEventAsync{T}"/> 的 jsonOptions 参数：
    /// <c>new JsonSerializerOptions(StreamingResponseWriter.DefaultJsonOptions) { ... }</c>。
    /// </summary>
    /// <remarks>
    /// ★ 枚举必须按成员名写出：这是框架的 wire 契约（MVC 全局与 <c>TnziJsonDefaults</c> 都挂了
    /// <see cref="JsonStringEnumConverter"/>），前端按成员名比较。流式端点绕过 MVC 自己序列化，
    /// 少了这一条，同一个枚举在 REST 回放里是 <c>"Text"</c>、在实时流里是 <c>0</c>：
    /// 外部 CLI 运行的事件流里 <c>type</c> 永远对不上，文本与思考从不累积、错误从不浮现，
    /// 两端都不报错。
    /// </remarks>
    public static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
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
    public static async Task WriteEventAsync<T>(HttpResponse response, T evt, StreamingFormat format, CancellationToken ct = default, JsonSerializerOptions? jsonOptions = null)
    {
        var json = JsonSerializer.Serialize(evt, jsonOptions ?? DefaultJsonOptions);

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
    /// 心跳默认间隔：低于常见反向代理 / 负载均衡的空闲断开阈值（nginx proxy_read_timeout、云负载均衡的空闲超时默认均为 60 秒）。
    /// </summary>
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 推进枚举器；等待下一个事件期间每隔 <paramref name="interval"/> 写一次心跳。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 静默窗口出现在<b>等待</b>下一个事件时（工具执行、模型缓冲工具参数），心跳只能在这段等待里由计时器写出；
    /// 事件刚写完紧跟一个心跳、或下一个事件到了再补一个，都盖不住这段空档。
    /// </para>
    /// <para>
    /// 写入都在调用方这一条循环里顺序发生，心跳不会插进某个事件中间；同一时刻只有一个 <c>MoveNextAsync</c> 在途。
    /// 心跳写失败（客户端已断开）时不再写心跳、继续等这一步结束：之后写事件会以同样的原因失败，交给调用方既有的错误路径；
    /// 在这里抛出则会留下一个在途的 <c>MoveNextAsync</c>，调用方随后的排空会撞上「上一步还没结束」。
    /// </para>
    /// </remarks>
    private static async ValueTask<bool> MoveNextWithHeartbeatAsync<T>(
        IAsyncEnumerator<T> enumerator, HttpResponse response, StreamingFormat format, TimeSpan interval, CancellationToken ct)
    {
        var moveNext = enumerator.MoveNextAsync();
        if (moveNext.IsCompleted || interval <= TimeSpan.Zero)
        {
            return await moveNext;
        }

        var pending = moveNext.AsTask();
        using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatsEnabled = true;
        while (heartbeatsEnabled)
        {
            var tick = Task.Delay(interval, timerCts.Token);
            if (await Task.WhenAny(pending, tick) == pending || tick.IsCanceled)
            {
                break;
            }

            try
            {
                await WriteHeartbeatAsync(response, format, ct);
            }
            catch (Exception)
            {
                // 写不出心跳 = 连接已断或请求已取消：下一次写事件会以同样的原因失败并走调用方的错误路径
                heartbeatsEnabled = false;
            }
        }

        timerCts.Cancel();
        return await pending;
    }

    /// <summary>
    /// 完整的流式传输生命周期管理 - 封装 pre-stream、IsDone 早发、主循环、drain、dispose。
    /// Pre-stream 阶段（首事件获取前）异常不会被捕获，由 ASP.NET Core 异常处理器正常返回 HTTP 错误。
    /// </summary>
    /// <param name="response">HTTP 响应</param>
    /// <param name="stream">事件序列</param>
    /// <param name="format">SSE 或 NDJSON</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="heartbeatInterval">
    /// 首个事件写出后，等待下一个事件期间的心跳间隔（默认 <see cref="DefaultHeartbeatInterval"/>）。
    /// 首个事件之前不写心跳：那时响应头尚未提交，异常还要能以普通 HTTP 错误返回。
    /// </param>
    public static async Task WriteFullStreamAsync(
        HttpResponse response,
        IAsyncEnumerable<StreamEvent> stream,
        StreamingFormat format,
        CancellationToken ct = default,
        TimeSpan? heartbeatInterval = null)
    {
        var interval = heartbeatInterval ?? DefaultHeartbeatInterval;
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

        var streamDone = firstEvent.IsDone;
        if (streamDone)
        {
            await WriteDoneAsync(response, format, CancellationToken.None);
        }

        if (!streamDone)
        {
            try
            {
                while (await MoveNextWithHeartbeatAsync(enumerator, response, format, interval, ct))
                {
                    var evt = enumerator.Current;
                    await WriteEventAsync(response, evt, format, ct);

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
    /// 完整的流式传输生命周期管理（泛型版本）- 支持自定义事件类型，通过 mapper 转换后写入。
    /// 适用于源事件类型非 StreamEvent 的流式端点（如 Workflow）。
    /// </summary>
    /// <typeparam name="TSource">源事件类型</typeparam>
    /// <typeparam name="TOutput">输出事件类型（序列化到客户端）</typeparam>
    /// <param name="response">要写入的响应</param>
    /// <param name="stream">源事件序列</param>
    /// <param name="mapper">源事件到输出事件的转换</param>
    /// <param name="isDone">输出事件是否为终止事件</param>
    /// <param name="errorFactory">流式阶段出错时写给客户端的错误事件</param>
    /// <param name="format">流式格式</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="heartbeatInterval">
    /// 首个事件之后，等待下一个事件期间每隔这么久写一次心跳（SSE 注释行 / NDJSON 空行；默认 <see cref="DefaultHeartbeatInterval"/>），
    /// 免得一次长时间无事件的等待被反向代理当成空闲连接切断。首个事件之前不写：那时响应头还没提交，
    /// 枚举前段的异常仍要能作为普通 HTTP 错误返回。
    /// </param>
    public static async Task WriteFullStreamAsync<TSource, TOutput>(
        HttpResponse response,
        IAsyncEnumerable<TSource> stream,
        Func<TSource, TOutput> mapper,
        Func<TOutput, bool> isDone,
        Func<Exception, TOutput> errorFactory,
        StreamingFormat format,
        CancellationToken ct = default,
        TimeSpan? heartbeatInterval = null)
    {
        var interval = heartbeatInterval ?? DefaultHeartbeatInterval;
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
                while (await MoveNextWithHeartbeatAsync(enumerator, response, format, interval, ct))
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
    /// 写入流式事件序列；等待下一个事件期间每隔 <paramref name="heartbeatInterval"/>（默认 <see cref="DefaultHeartbeatInterval"/>）
    /// 写一次心跳，防止反向代理在静默期断开连接。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="WriteFullStreamAsync(HttpResponse, IAsyncEnumerable{StreamEvent}, StreamingFormat, CancellationToken, TimeSpan?)"/>
    /// 不同，这里不管响应头、不写终止信号、不捕获异常：调用方已经 <see cref="ConfigureResponse"/> 过、自己决定流怎么收尾时使用。
    /// </remarks>
    public static async Task WriteStreamWithHeartbeatAsync(
        HttpResponse response,
        IAsyncEnumerable<StreamEvent> events,
        StreamingFormat format,
        TimeSpan? heartbeatInterval = null,
        CancellationToken ct = default)
    {
        var interval = heartbeatInterval ?? DefaultHeartbeatInterval;
        await using var enumerator = events.GetAsyncEnumerator(ct);
        while (await MoveNextWithHeartbeatAsync(enumerator, response, format, interval, ct))
        {
            await WriteEventAsync(response, enumerator.Current, format, ct);
        }
    }
}
