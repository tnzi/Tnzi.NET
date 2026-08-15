namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 一条 Chrome DevTools Protocol（CDP）连接：发命令、等事件，仅此而已。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么手写而不是引第三方库：</b>本包用到的 CDP 面只有
/// <c>Target.createTarget</c> / <c>Target.attachToTarget</c> / <c>Page.enable</c> /
/// <c>Page.navigate</c> / <c>Page.printToPDF</c> 五个命令加一个 <c>Page.loadEventFired</c> 事件，
/// 全是 2017 年就稳定下来的域。为这点东西引入浏览器自动化库会把 Rx 分支、WebDriver BiDi 预览版
/// 一并拖进**所有**用 <c>Tnzi.Documents</c> 的应用（<c>Tnzi.Signing</c> 只用盖章与定位，
/// 一行浏览器代码都不需要），与本包「重依赖收在包内不外溢」的立身之本相反。
/// </para>
/// <para>
/// 协议形态：请求 <c>{id, method, params, sessionId?}</c>，响应 <c>{id, result|error}</c>，
/// 事件 <c>{method, params, sessionId?}</c>。同一条连接上用 <c>sessionId</c> 区分浏览器级与页面级
/// （即 flatten 模式），所以不需要为页面再开一条 WebSocket。
/// </para>
/// <para>
/// ★ <b>读循环退出时必须把在途请求一并置为失败</b>：浏览器崩了或被杀掉时连接直接断开，
/// 不主动失败的话调用方会一直等到超时才知道对面早就没了 —— 而那正是最需要把真实原因说出来的时刻。
/// </para>
/// </remarks>
internal sealed class DevToolsSession : IAsyncDisposable
{
    private const int ReceiveBufferSize = 32 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ClientWebSocket _socket;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _eventWaiters = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Task _reader;

    private int _lastId;

    private DevToolsSession(ClientWebSocket socket)
    {
        _socket = socket;
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>连接到 DevTools 端点。</summary>
    /// <param name="endpoint">浏览器级 WebSocket 端点（<c>ws://127.0.0.1:port/devtools/browser/…</c>）。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<DevToolsSession> ConnectAsync(Uri endpoint, CancellationToken ct)
    {
        var socket = new ClientWebSocket();

        try
        {
            await socket.ConnectAsync(endpoint, ct);
        }
        catch (WebSocketException ex)
        {
            socket.Dispose();
            throw new DocumentConversionException(
                $"Failed to connect to the browser's DevTools endpoint at '{endpoint}': {ex.Message}",
                innerException: ex);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new DevToolsSession(socket);
    }

    /// <summary>发一条命令并等它的响应。</summary>
    /// <param name="method">CDP 方法名。</param>
    /// <param name="parameters">参数对象；为 null 时不带 <c>params</c>。</param>
    /// <param name="sessionId">页面会话 id；为 null 时是浏览器级命令。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<JsonElement> SendAsync(string method, object? parameters = null, string? sessionId = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _lastId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        try
        {
            // ★ 空值必须**不写这个键**，而不是写成 null：浏览器对 sessionId 的校验是
            // 「要么没有，要么是字符串」，写 null 会被直接拒掉（"Message may have string 'sessionId' property"）。
            // JsonIgnoreCondition.WhenWritingNull 管的是对象的属性，管不到字典的值 —— 这里只能自己挑。
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = id,
                ["method"] = method
            };

            if (parameters != null)
                payload["params"] = parameters;

            if (sessionId != null)
                payload["sessionId"] = sessionId;

            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);

            await _sendGate.WaitAsync(ct);
            try
            {
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
            {
                throw new DocumentConversionException(
                    $"Failed to send '{method}' to the browser: {ex.Message}", innerException: ex);
            }
            finally
            {
                _sendGate.Release();
            }

            return await completion.Task.WaitAsync(ct);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// 登记一个事件等待者，返回的 Task 在该事件到达时完成。
    /// </summary>
    /// <param name="method">CDP 事件名。</param>
    /// <remarks>
    /// ★ <b>必须在触发它的命令之前登记。</b>页面加载可能快到 <c>Page.navigate</c> 的响应还没回来
    /// 事件就已经发出，navigate 之后再登记就会一直等到超时。
    /// </remarks>
    public Task<JsonElement> WhenEventAsync(string method)
    {
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _eventWaiters[method] = completion;
        return completion.Task;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();

        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, closeTimeout.Token);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // 对面可能已经退出了；关闭握手失败不影响任何结果
        }

        _socket.Dispose();

        try
        {
            await _reader;
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // 读循环因连接关闭而结束是预期路径
        }

        _shutdown.Dispose();
        _sendGate.Dispose();
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[ReceiveBufferSize];

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _shutdown.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                message.Position = 0;
                Dispatch(message);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // 连接断开（浏览器退出、被杀、或本方 Dispose）：交给 finally 让在途请求立刻失败
        }
        finally
        {
            FaultEveryone();
        }
    }

    private void Dispatch(Stream message)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException)
        {
            // 不该发生；解析不了的帧丢弃即可，不能让读循环因此终止（终止会误伤在途请求）
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
            {
                if (!_pending.TryRemove(id, out var completion))
                    return;

                if (root.TryGetProperty("error", out var error))
                {
                    var text = error.TryGetProperty("message", out var errorMessage)
                        ? errorMessage.GetString()
                        : error.ToString();

                    completion.TrySetException(new DocumentConversionException($"The browser rejected a DevTools command: {text}"));
                    return;
                }

                completion.TrySetResult(root.TryGetProperty("result", out var payload) ? payload.Clone() : default);
                return;
            }

            if (root.TryGetProperty("method", out var method)
                && method.GetString() is { } name
                && _eventWaiters.TryRemove(name, out var waiter))
            {
                waiter.TrySetResult(root.TryGetProperty("params", out var eventParameters) ? eventParameters.Clone() : default);
            }
        }
    }

    private void FaultEveryone()
    {
        var failure = new DocumentConversionException(
            "The browser closed the DevTools connection before the conversion finished. " +
            "It may have crashed, been killed, or run out of memory.",
            isRetryable: true);

        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var completion))
                completion.TrySetException(failure);
        }

        foreach (var name in _eventWaiters.Keys)
        {
            if (_eventWaiters.TryRemove(name, out var waiter))
                waiter.TrySetException(failure);
        }
    }
}
