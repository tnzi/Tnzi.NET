// R3：二级目录（Adapters/Acp/）只是开发期分类，不产生子命名空间。
namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// Agent Client Protocol 适配器：stdio 上的双向 JSON-RPC 2.0。
/// </summary>
/// <remarks>
/// <b>一次实现覆盖 6 个 CLI</b>（hermes / kimi / kiro / qoder / trae / grok）——
/// 这正是把「协议」和「provider 参数」分开的收益：它们的差异全在
/// <see cref="CliProviderDescriptor"/> 这张数据表里，代码只有一份。
/// <para>
/// 会话生命周期：<c>initialize</c> → <c>session/new</c> | <c>session/resume</c> →
/// （可选）<c>session/set_model</c> → <c>session/prompt</c>。
/// 期间 agent 会反向发起 <c>session/request_permission</c>，由
/// <see cref="AcpJsonRpcClient"/> 自动应答。
/// </para>
/// </remarks>
public class AcpAdapter : ICliProtocolAdapter
{
    /// <summary>
    /// <c>session/prompt</c> 返回后再等一小段，收尾那些跟在响应之后的通知。
    /// </summary>
    /// <remarks>
    /// 部分运行时会在 prompt 响应之后才发出最后一批 <c>session/update</c>；
    /// 响应一到就拆管道会把 turn 的最后几句话切掉。
    /// </remarks>
    private static readonly TimeSpan NotificationDrainGrace = TimeSpan.FromSeconds(2);

    private readonly ILogger<AcpAdapter> _logger;
    private readonly AcpDeliverableTracker _deliverable = new();
    private readonly Dictionary<string, AcpPendingToolCall> _pendingTools = new(StringComparer.Ordinal);
    private readonly Lock _toolGate = new();

    private CliAgentTokenUsage _usage = new();
    private string? _sessionId;
    private string? _modelId;
    private string? _stopReason;
    private string? _failureMessage;
    private CliRunFailureReason? _failureReason;
    private bool _resumeRejected;
    private string? _requestedResumeSessionId;

    /// <summary>初始化 ACP 适配器。</summary>
    public AcpAdapter(ILogger<AcpAdapter> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public CliAgentProtocol Protocol => CliAgentProtocol.Acp;

    /// <inheritdoc />
    public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
    {
        Check.NotNull(context);

        var args = new List<string>(context.Provider.LaunchArgs);
        args.AddRange(CliArgumentFilter.Filter(
            context.ExtraArgs, context.Provider.BlockedArgs, _logger, context.Provider.Key));
        args.AddRange(CliArgumentFilter.Filter(
            context.CustomArgs, context.Provider.BlockedArgs, _logger, context.Provider.Key));

        return new CliProcessSpec
        {
            ExecutablePath = context.ExecutablePath,
            Arguments = args,
            WorkingDirectory = context.WorkingDirectory,
            Environment = context.Environment,
            InheritAllHostEnvironment = context.InheritAllHostEnvironment,
            EnvironmentWhitelist = context.EnvironmentWhitelist,
            TerminateGrace = context.TerminateGrace
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CliAgentEvent> RunAsync(
        ICliAgentTransport transport,
        CliAgentLaunchContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Check.NotNull(transport);
        Check.NotNull(context);

        _requestedResumeSessionId = context.ResumeSessionId;

        var events = Channel.CreateUnbounded<CliAgentEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var client = new AcpJsonRpcClient(transport, _logger)
        {
            OnSessionUpdate = parameters => DispatchSessionUpdate(parameters, events.Writer),
            OnPromptResult = ExtractPromptResult
        };

        // 读循环与生命周期驱动分开跑：ACP 的请求要等响应，而响应只会从读循环来 ——
        // 放在同一个任务里就是自己等自己。
        var reader = Task.Run(async () =>
        {
            try
            {
                await foreach (var line in transport.ReadLinesAsync(cancellationToken))
                {
                    await client.HandleLineAsync(line, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消是正常终止路径。
            }
            finally
            {
                // 进程没了，所有还在等响应的请求必须被唤醒，否则生命周期任务永久挂起。
                client.FailAllPending(new IOException("The external agent process exited."));
            }
        }, CancellationToken.None);

        var lifecycle = Task.Run(
            () => DriveSessionAsync(client, context, events.Writer, cancellationToken),
            CancellationToken.None);

        await foreach (var evt in events.Reader.ReadAllAsync(CancellationToken.None))
        {
            yield return evt;
        }

        await lifecycle;
        await transport.CloseInputAsync();

        // 读循环随着进程退出自然结束；不无限等它，避免一个卡住的子进程把调用方一起拖住。
        await Task.WhenAny(reader, Task.Delay(NotificationDrainGrace, CancellationToken.None));
    }

    /// <inheritdoc />
    public CliAgentResult GetResult(CliSessionOutcome outcome)
    {
        Check.NotNull(outcome);

        var (deliverable, full) = _deliverable.Result();
        var status = CliRunStatus.Completed;
        var failure = _failureReason;
        var error = _failureMessage;

        if (outcome.Cancelled)
        {
            status = CliRunStatus.Cancelled;
            failure = CliRunFailureReason.Cancelled;
            error = "Execution cancelled.";
        }
        else if (outcome.WatchdogFailure is { } watchdog)
        {
            status = CliRunStatus.TimedOut;
            failure = watchdog;
            error = $"External agent timed out ({watchdog}).";
        }
        else if (_failureMessage is not null)
        {
            status = CliRunStatus.Failed;
        }
        else if (string.Equals(_stopReason, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            status = CliRunStatus.Cancelled;
            failure = CliRunFailureReason.Cancelled;
            error = "The external agent cancelled the prompt.";
        }
        else if (AcpProviderErrorSniffer.Sniff(outcome.StderrTail, full) is { } sniffed)
        {
            // 「成功」提升为失败：多个 ACP 运行时在上游 HTTP 失败时仍报 stopReason=end_turn。
            // 不提升的话用户看到的是一句空回复，而不是「token 过期」。
            status = CliRunStatus.Failed;
            failure = sniffed.Reason;
            error = $"The external agent reported a terminal provider failure ('{sniffed.Phrase}').";
        }

        var usage = new Dictionary<string, CliAgentTokenUsage>(StringComparer.Ordinal);
        if (_usage.InputTokens > 0 || _usage.OutputTokens > 0
            || _usage.CacheReadTokens > 0 || _usage.CacheWriteTokens > 0)
        {
            usage[_modelId ?? "unknown"] = _usage;
        }

        return new CliAgentResult
        {
            Status = status,
            FailureReason = status == CliRunStatus.Completed ? null : failure ?? CliRunFailureReason.Unknown,
            Output = status == CliRunStatus.Completed ? deliverable : null,
            FullTranscript = full,
            Error = string.IsNullOrWhiteSpace(error) ? null : AppendStderr(error, outcome.StderrTail),
            DurationMs = (long)outcome.Elapsed.TotalMilliseconds,
            SessionId = _resumeRejected ? null : _sessionId,
            Usage = usage,
            ResumeRejected = _resumeRejected
        };
    }

    private async Task DriveSessionAsync(
        AcpJsonRpcClient client,
        CliAgentLaunchContext context,
        ChannelWriter<CliAgentEvent> events,
        CancellationToken cancellationToken)
    {
        try
        {
            var initializeResult = await WithHandshakeTimeoutAsync(
                ct => client.RequestAsync("initialize", new
                {
                    protocolVersion = 1,
                    clientInfo = new { name = "tnzi-cli-agent", version = "1" },
                    clientCapabilities = new { }
                }, ct),
                context.HandshakeTimeout,
                cancellationToken);

            var mcpServers = AcpMcpServerTranslator.FilterByCapabilities(
                AcpMcpServerTranslator.Translate(context.McpConfigPath is null ? null : ReadMcpConfig(context.McpConfigPath), _logger),
                initializeResult, context.Provider.Key, _logger);

            var cwd = string.IsNullOrWhiteSpace(context.WorkingDirectory) ? "." : context.WorkingDirectory;

            if (!string.IsNullOrWhiteSpace(context.ResumeSessionId))
            {
                // 续接时也要带上 mcpServers：ACP 的 resume 会重新连接它们，
                // 不带的话续接后的任务会比新任务少掉一批工具。
                var resumeResult = await WithHandshakeTimeoutAsync(
                    ct => client.RequestAsync("session/resume", new
                    {
                        cwd,
                        sessionId = context.ResumeSessionId,
                        mcpServers
                    }, ct),
                    context.HandshakeTimeout,
                    cancellationToken);

                _sessionId = ExtractSessionId(resumeResult) ?? context.ResumeSessionId;
                if (!string.Equals(_sessionId, context.ResumeSessionId, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "[{Provider}] Runtime returned a different session id on resume; the original transcript was likely lost",
                        context.Provider.Key);
                }
            }
            else
            {
                var newResult = await WithHandshakeTimeoutAsync(
                    ct => client.RequestAsync("session/new", new { cwd, mcpServers }, ct),
                    context.HandshakeTimeout,
                    cancellationToken);

                _sessionId = ExtractSessionId(newResult);
                if (string.IsNullOrWhiteSpace(_sessionId))
                {
                    Fail(CliRunFailureReason.HandshakeTimeout, "session/new returned no session id.");
                    return;
                }
            }

            events.TryWrite(new CliAgentEvent
            {
                Type = CliAgentEventType.Status,
                Status = "running",
                SessionId = _sessionId
            });

            if (!string.IsNullOrWhiteSpace(context.Model) && !await TrySetModelAsync(client, context, cancellationToken))
            {
                return;
            }

            var promptText = string.IsNullOrWhiteSpace(context.InlineSystemPrompt)
                ? context.Prompt
                : $"{context.InlineSystemPrompt}\n\n---\n\n{context.Prompt}";

            await client.RequestAsync("session/prompt", new
            {
                sessionId = _sessionId,
                prompt = new[] { new { type = "text", text = promptText } }
            }, cancellationToken);

            // 响应到了不代表通知发完了，留一小段收尾窗口。
            await Task.Delay(NotificationDrainGrace, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // 取消由 outcome.Cancelled 表达，这里不覆写失败原因。
        }
        catch (AcpRpcException ex)
        {
            HandlePromptFailure(ex);
        }
        catch (IOException ex)
        {
            Fail(CliRunFailureReason.ProcessCrashed, ex.Message);
        }
        catch (TimeoutException ex)
        {
            Fail(CliRunFailureReason.HandshakeTimeout, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            Fail(CliRunFailureReason.LaunchFailed, ex.Message);
        }
        finally
        {
            events.TryComplete();
        }
    }

    private async Task<bool> TrySetModelAsync(
        AcpJsonRpcClient client, CliAgentLaunchContext context, CancellationToken cancellationToken)
    {
        try
        {
            await client.RequestAsync("session/set_model", new
            {
                sessionId = _sessionId,
                modelId = context.Model
            }, cancellationToken);
            return true;
        }
        catch (AcpRpcException ex)
        {
            // 这里必须让整个运行失败：静默回落到运行时的默认模型，会让用户以为自己选的
            // 模型生效了，而任务其实跑在别的模型上（成本与质量都对不上）。
            _logger.LogWarning(ex, "[{Provider}] Could not switch to model '{Model}'", context.Provider.Key, context.Model);
            HandlePromptFailure(ex);
            _failureMessage = $"The external agent could not switch to model '{context.Model}': {ex.Message}";
            return false;
        }
    }

    private void HandlePromptFailure(AcpRpcException ex)
    {
        var isSessionNotFound = IsSessionNotFound(ex);
        if (isSessionNotFound && !string.IsNullOrWhiteSpace(_requestedResumeSessionId))
        {
            // 多数运行时在 session/resume 时会把请求的 id 原样回给我们，即使会话已经没了；
            // 真正暴露出来是在 prompt（或 set_model）阶段。所以判定点在这里，不在 resume。
            _resumeRejected = true;
            _sessionId = null;
            Fail(CliRunFailureReason.ResumeRejected, $"The resumed session no longer exists: {ex.Message}");
            return;
        }

        Fail(CliRunFailureReason.ProviderError, ex.Message);
    }

    private static bool IsSessionNotFound(AcpRpcException ex)
    {
        var haystack = $"{ex.Message} {ex.Detail}";
        return haystack.Contains("session not found", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains("no session found", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains("unknown session", StringComparison.OrdinalIgnoreCase);
    }

    private void Fail(CliRunFailureReason reason, string message)
    {
        _failureReason = reason;
        _failureMessage = message;
    }

    private static string ReadMcpConfig(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static async Task<JsonElement> WithHandshakeTimeoutAsync(
        Func<CancellationToken, Task<JsonElement>> action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
        {
            cts.CancelAfter(timeout);
        }

        try
        {
            return await action(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The ACP handshake did not complete within {timeout}.");
        }
    }

    private static string? ExtractSessionId(JsonElement result)
        => result.ValueKind == JsonValueKind.Object && result.TryGetProperty("sessionId", out var id)
            ? id.GetString()
            : null;

    private void ExtractPromptResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (result.TryGetProperty("stopReason", out var stopReason))
        {
            _stopReason = stopReason.GetString();
        }

        if (result.TryGetProperty("usage", out var usage))
        {
            MergeUsage(usage);
        }

        if (result.TryGetProperty("_meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            if (meta.TryGetProperty("modelId", out var modelId))
            {
                _modelId = modelId.GetString();
            }

            // 部分运行时只在 _meta 下报用量；不看这里的话用量与成本面板恒为零。
            if (meta.TryGetProperty("usage", out var metaUsage))
            {
                MergeUsage(metaUsage);
            }
            else
            {
                MergeUsage(meta);
            }
        }
    }

    private void MergeUsage(JsonElement usage)
    {
        if (usage.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        _usage = _usage with
        {
            InputTokens = _usage.InputTokens + ReadLong(usage, "inputTokens", "input_tokens", "promptTokens"),
            OutputTokens = _usage.OutputTokens + ReadLong(usage, "outputTokens", "output_tokens", "completionTokens"),
            CacheReadTokens = _usage.CacheReadTokens + ReadLong(usage, "cachedReadTokens", "cacheReadTokens", "cache_read_tokens"),
            CacheWriteTokens = _usage.CacheWriteTokens + ReadLong(usage, "cacheWriteTokens", "cache_write_tokens")
        };
    }

    private static long ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return 0;
    }

    private void DispatchSessionUpdate(JsonElement parameters, ChannelWriter<CliAgentEvent> events)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("update", out var update))
        {
            return;
        }

        var (updateType, payload) = NormalizeUpdate(update);
        switch (updateType)
        {
            case "agent_message_chunk":
                EmitText(payload, CliAgentEventType.Text, events);
                break;

            case "agent_thought_chunk":
                EmitText(payload, CliAgentEventType.Thinking, events);
                break;

            case "tool_call":
                HandleToolCallStart(payload, events);
                break;

            case "tool_call_update":
                HandleToolCallUpdate(payload, events);
                break;

            case "usage_update":
                MergeUsage(payload);
                break;

            case "turn_end":
                ExtractPromptResult(payload);
                break;

            default:
                // 未知更新类型静默忽略而不是崩溃：运行时会随版本新增类型。
                break;
        }
    }

    private void EmitText(JsonElement payload, CliAgentEventType type, ChannelWriter<CliAgentEvent> events)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("text", out var text))
        {
            return;
        }

        var value = text.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var evt = new CliAgentEvent { Type = type, Content = value, SessionId = _sessionId };
        _deliverable.Observe(evt);
        events.TryWrite(evt);
    }

    private void HandleToolCallStart(JsonElement payload, ChannelWriter<CliAgentEvent> events)
    {
        var callId = ReadString(payload, "toolCallId");
        if (string.IsNullOrWhiteSpace(callId))
        {
            return;
        }

        var toolName = AcpToolNames.Normalize(ReadString(payload, "title") ?? ReadString(payload, "name"));
        var input = ReadObject(payload, "rawInput", "input", "parameters");

        // 部分运行时在初始 tool_call 就带全了入参，另一些逐 token 流式补齐。
        // 后者若立刻外发，UI 会看到 `{"comma` 这样的半截命令 —— 所以只有拿到完整入参
        // 才发 ToolUse，否则先缓存，等 completed/failed 再补发。
        if (input is not null)
        {
            lock (_toolGate)
            {
                _pendingTools[callId] = new AcpPendingToolCall { ToolName = toolName, Emitted = true };
            }

            var evt = new CliAgentEvent
            {
                Type = CliAgentEventType.ToolUse,
                Tool = toolName,
                CallId = callId,
                Input = input,
                SessionId = _sessionId
            };
            _deliverable.Observe(evt);
            events.TryWrite(evt);
            return;
        }

        lock (_toolGate)
        {
            _pendingTools[callId] = new AcpPendingToolCall { ToolName = toolName, Emitted = false };
        }
    }

    private void HandleToolCallUpdate(JsonElement payload, ChannelWriter<CliAgentEvent> events)
    {
        var callId = ReadString(payload, "toolCallId");
        if (string.IsNullOrWhiteSpace(callId))
        {
            return;
        }

        var status = ReadString(payload, "status");
        if (status is not ("completed" or "failed"))
        {
            return;
        }

        AcpPendingToolCall? pending;
        lock (_toolGate)
        {
            _pendingTools.Remove(callId, out pending);
        }

        if (pending is { Emitted: false })
        {
            var toolName = pending.ToolName
                           ?? AcpToolNames.Normalize(ReadString(payload, "title") ?? ReadString(payload, "name"));
            var deferred = new CliAgentEvent
            {
                Type = CliAgentEventType.ToolUse,
                Tool = toolName,
                CallId = callId,
                Input = ReadObject(payload, "rawInput", "input", "parameters"),
                SessionId = _sessionId
            };
            _deliverable.Observe(deferred);
            events.TryWrite(deferred);
        }

        events.TryWrite(new CliAgentEvent
        {
            Type = CliAgentEventType.ToolResult,
            Tool = pending?.ToolName,
            CallId = callId,
            Output = ReadToolOutput(payload),
            Status = status,
            SessionId = _sessionId
        });
    }

    private static (string Type, JsonElement Payload) NormalizeUpdate(JsonElement update)
    {
        if (update.ValueKind != JsonValueKind.Object)
        {
            return (string.Empty, update);
        }

        if (update.TryGetProperty("sessionUpdate", out var sessionUpdate) && sessionUpdate.ValueKind == JsonValueKind.String)
        {
            return (NormalizeUpdateType(sessionUpdate.GetString()), update);
        }

        if (update.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
        {
            return (NormalizeUpdateType(type.GetString()), update);
        }

        // 部分实现把枚举变体序列化成外部标签对象：{"agentMessageChunk": {...}}。
        var properties = update.EnumerateObject().ToList();
        if (properties.Count == 1)
        {
            return (NormalizeUpdateType(properties[0].Name), properties[0].Value);
        }

        return (string.Empty, update);
    }

    private static string NormalizeUpdateType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var key = raw.Trim().Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return key switch
        {
            "agentmessagechunk" => "agent_message_chunk",
            "agentthoughtchunk" => "agent_thought_chunk",
            "toolcall" => "tool_call",
            "toolcallupdate" => "tool_call_update",
            "usageupdate" => "usage_update",
            "turnend" or "endturn" => "turn_end",
            _ => string.Empty
        };
    }

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.GetString()
            : null;

    private static IReadOnlyDictionary<string, object?>? ReadObject(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }

            return result;
        }

        return null;
    }

    private static string? ReadToolOutput(JsonElement payload)
    {
        foreach (var name in new[] { "rawOutput", "output" })
        {
            if (payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            }
        }

        if (payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            return string.Join("\n", content.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var text)
                    ? text.GetString()
                    : item.GetRawText())
                .Where(s => !string.IsNullOrEmpty(s)));
        }

        return null;
    }

    private static string AppendStderr(string error, string stderrTail)
        => string.IsNullOrWhiteSpace(stderrTail) ? error : $"{error}\n--- stderr ---\n{stderrTail.TrimEnd()}";

    private sealed class AcpPendingToolCall
    {
        public string? ToolName { get; init; }
        public bool Emitted { get; init; }
    }
}

/// <summary>
/// 把 ACP 的人类可读工具标题归一化成稳定的 snake_case 标识符。
/// </summary>
/// <remarks>
/// ACP 规范里 <c>title</c> 是给人看的短标签（"Read file: /path/to/foo.go"），各家措辞、
/// 大小写都不同。UI 需要一个稳定标识符来配折叠状态和图标，所以在这里收口。
/// </remarks>
internal static class AcpToolNames
{
    /// <summary>归一化一个工具标题。</summary>
    public static string? Normalize(string? title)
    {
        var value = title?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // ACP 标题常是「工具名: 参数细节」，只取冒号前的部分。
        var colon = value.IndexOf(':');
        if (colon > 0)
        {
            value = value[..colon].Trim();
        }

        var lower = value.ToLowerInvariant();
        return lower switch
        {
            "read" or "read file" => "read_file",
            "write" or "write file" => "write_file",
            "edit" or "patch" => "edit_file",
            "shell" or "bash" or "terminal" or "run command" or "run shell command" => "terminal",
            "search" or "grep" or "find" => "search_files",
            "glob" => "glob",
            "web search" => "web_search",
            "fetch" or "web fetch" => "web_fetch",
            "todo" or "todo write" => "todo_write",
            _ => lower.Replace(' ', '_')
        };
    }
}
