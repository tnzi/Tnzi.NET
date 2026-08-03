// R3：二级目录（Adapters/Acp/）只是开发期分类，不产生子命名空间。
namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// ACP 的 JSON-RPC 2.0 传输层：请求/响应关联、通知分发、<b>反向请求</b>应答。
/// </summary>
/// <remarks>
/// 反向请求是 ACP 与单向流协议的关键差异：agent 会主动向客户端发
/// <c>session/request_permission</c> 并<b>阻塞等待应答</b>。不答的后果不是丢一条消息，
/// 而是整个任务挂到 agent 自己的内部超时。
/// </remarks>
internal sealed class AcpJsonRpcClient
{
    private readonly ICliAgentTransport _transport;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<AcpRpcResult>> _pending = new();
    private long _nextId;

    /// <summary>收到 <c>session/update</c> 通知时回调。</summary>
    public Action<JsonElement>? OnSessionUpdate { get; init; }

    /// <summary>收到 <c>session/prompt</c> 响应时回调（用量与 stopReason）。</summary>
    public Action<JsonElement>? OnPromptResult { get; init; }

    public AcpJsonRpcClient(ICliAgentTransport transport, ILogger logger)
    {
        _transport = transport;
        _logger = logger;
    }

    /// <summary>发一个请求并等待响应。</summary>
    public async Task<JsonElement> RequestAsync(
        string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<AcpRpcResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        });

        try
        {
            await _transport.WriteLineAsync(payload, cancellationToken);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        await using var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        var result = await completion.Task;
        if (result.Error is { } error)
        {
            throw new AcpRpcException(method, error.Code, error.Message, error.Data);
        }

        return result.Value;
    }

    /// <summary>
    /// 处理一行入站 JSON。区分三种形态：响应、反向请求、通知。
    /// </summary>
    public async Task HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            // 非 JSON 行（启动横幅）不是协议错误。
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            var hasId = root.TryGetProperty("id", out var idElement);
            var hasMethod = root.TryGetProperty("method", out var methodElement);

            if (hasId && !hasMethod)
            {
                CompleteResponse(root, idElement);
                return;
            }

            if (hasId && hasMethod)
            {
                await AnswerAgentRequestAsync(root, idElement, methodElement, cancellationToken);
                return;
            }

            if (hasMethod)
            {
                HandleNotification(root, methodElement);
            }
        }
    }

    /// <summary>进程退出时唤醒所有还在等的请求，避免调用方永久挂起。</summary>
    public void FailAllPending(Exception exception)
    {
        foreach (var (id, completion) in _pending)
        {
            _pending.TryRemove(id, out _);
            completion.TrySetException(exception);
        }
    }

    private void CompleteResponse(JsonElement root, JsonElement idElement)
    {
        if (!TryGetId(idElement, out var id) || !_pending.TryRemove(id, out var completion))
        {
            return;
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            completion.TrySetResult(new AcpRpcResult { Error = AcpRpcError.From(errorElement) });
            return;
        }

        var value = root.TryGetProperty("result", out var resultElement)
            ? resultElement.Clone()
            : default;

        OnPromptResult?.Invoke(value);
        completion.TrySetResult(new AcpRpcResult { Value = value });
    }

    private void HandleNotification(JsonElement root, JsonElement methodElement)
    {
        var method = methodElement.GetString();
        if (method is not ("session/update" or "session/notification"))
        {
            return;
        }

        if (root.TryGetProperty("params", out var paramsElement))
        {
            OnSessionUpdate?.Invoke(paramsElement.Clone());
        }
    }

    private async Task AnswerAgentRequestAsync(
        JsonElement root, JsonElement idElement, JsonElement methodElement, CancellationToken cancellationToken)
    {
        var method = methodElement.GetString();

        // id 必须原样回送：JSON-RPC 允许它是数字或字符串，重新序列化会改掉类型，
        // 而 agent 是按原值配对的。
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(idElement.GetRawText())
        };

        if (method == "session/request_permission")
        {
            root.TryGetProperty("params", out var parameters);
            var selection = AcpPermissionSelector.Select(parameters);
            if (selection is { } optionId)
            {
                if (AcpPermissionSelector.IsGrant(parameters, optionId))
                {
                    _logger.LogDebug("[acp] Auto-approved permission request with option '{OptionId}'", optionId);
                }
                else
                {
                    _logger.LogWarning(
                        "[acp] No safe grant offered; selecting the offered single-use reject option '{OptionId}'", optionId);
                }

                envelope["result"] = new JsonObject
                {
                    ["outcome"] = new JsonObject
                    {
                        ["outcome"] = "selected",
                        ["optionId"] = optionId
                    }
                };
            }
            else
            {
                // 请求里没有任何可安全选择的选项。回一个协议错误，而不是编造一个 agent
                // 从未提供的 optionId，也不回 "cancelled"（那表示整个 turn 被取消，
                // 会让本次任务直接中止而不只是拒绝这一个动作）。
                _logger.LogWarning("[acp] Permission request offered no safely selectable option; returning an error");
                envelope["error"] = new JsonObject
                {
                    ["code"] = -32603,
                    ["message"] = "no auto-selectable permission option offered"
                };
            }
        }
        else
        {
            // 未知的 agent→client 方法：回标准的 method not found。沉默会让 agent 一直等。
            envelope["error"] = new JsonObject
            {
                ["code"] = -32601,
                ["message"] = $"method not found: {method}"
            };
            _logger.LogDebug("[acp] Unhandled agent request method '{Method}'", method);
        }

        await _transport.WriteLineAsync(envelope.ToJsonString(), cancellationToken);
    }

    private static bool TryGetId(JsonElement element, out long id)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt64(out id):
                return true;
            case JsonValueKind.String when long.TryParse(element.GetString(), out id):
                return true;
            default:
                id = 0;
                return false;
        }
    }
}

/// <summary>一次 JSON-RPC 调用的结果。</summary>
internal readonly record struct AcpRpcResult
{
    /// <summary>成功时的 result 负载。</summary>
    public JsonElement Value { get; init; }

    /// <summary>失败时的 error 负载。</summary>
    public AcpRpcError? Error { get; init; }
}

/// <summary>JSON-RPC 错误负载。</summary>
internal readonly record struct AcpRpcError
{
    /// <summary>错误码。</summary>
    public int Code { get; init; }

    /// <summary>错误消息。</summary>
    public string Message { get; init; }

    /// <summary>厂商私有的细节（ACP 各家把真实原因放在这里）。</summary>
    public string? Data { get; init; }

    /// <summary>从 error 元素解析。</summary>
    public static AcpRpcError From(JsonElement element)
    {
        var code = element.TryGetProperty("code", out var c) && c.TryGetInt32(out var value) ? value : 0;
        var message = element.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
        string? data = null;
        if (element.TryGetProperty("data", out var d) && d.ValueKind != JsonValueKind.Null)
        {
            data = d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
        }

        return new AcpRpcError { Code = code, Message = message, Data = data };
    }
}

/// <summary>ACP JSON-RPC 调用返回了错误。</summary>
internal sealed class AcpRpcException : Exception
{
    /// <summary>失败的方法名。</summary>
    public string Method { get; }

    /// <summary>JSON-RPC 错误码。</summary>
    public int Code { get; }

    /// <summary>
    /// 厂商私有细节（ACP 各家把真实原因放在 JSON-RPC 的 <c>data</c> 字段里）。
    /// </summary>
    /// <remarks>刻意不叫 Data —— 那会遮蔽 <see cref="Exception.Data"/>。</remarks>
    public string? Detail { get; }

    public AcpRpcException(string method, int code, string message, string? data)
        : base(string.IsNullOrWhiteSpace(data) ? $"{method}: {message}" : $"{method}: {message} ({data})")
    {
        Method = method;
        Code = code;
        Detail = data;
    }
}
