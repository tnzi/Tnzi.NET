// R3：二级目录（Adapters/StreamJson/）只是开发期分类，不产生子命名空间。
namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// stream-json 协议适配器：stdio 上的行分隔 JSON 事件流（claude / codebuddy / qwen）。
/// </summary>
/// <remarks>
/// <para>
/// <b>两条必须同时成立的实测结论</b>（缺一个就跑不对）：
/// </para>
/// <list type="number">
/// <item>
/// <b>stdin 必须保持打开</b>：协议会在运行中发 <c>control_request</c> 要求应答，
/// 提前关掉 stdin 会让子进程一直等到它自己的内部超时。
/// </item>
/// <item>
/// <b><c>result</c> 事件是唯一的终止信号，不能等 stdout EOF</b>：正因为 stdin 保持打开，
/// CLI 不会自行退出，stdout 也不会关闭。等 EOF 的写法实测让一个 "Reply with OK" 的 turn
/// 挂满 180 秒超时窗口；改成收到 <c>result</c> 即结束后，同一个 turn 降到 6.4 秒。
/// 拿到终态后主动关 stdin，CLI 约 0.3 秒自行退出。
/// </item>
/// </list>
/// <para>
/// 未知事件类型一律降级为 <see cref="CliAgentEventType.Log"/>，绝不崩溃 ——
/// CLI 会随版本新增事件类型，把未知形状当错误处理等于让每次上游升级都成为一次故障。
/// </para>
/// </remarks>
public class StreamJsonAdapter : ICliProtocolAdapter
{
    private static readonly JsonSerializerOptions FrameOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// 能正面识别「resume 被拒绝」的 stderr 短语。
    /// </summary>
    /// <remarks>
    /// <b>匹配必须保持严格</b>：一次误判会让调度层丢掉一个本可恢复的会话指针并重跑整个任务，
    /// 所以任何含糊的措辞都不该进这张表。实测 claude 在 <c>--resume</c> 指向的会话不存在时
    /// 打印 "No conversation found with session ID: &lt;id&gt;"，
    /// 而 <c>result</c> 事件的 subtype 是通用的 <c>error_during_execution</c>，<b>不能</b>用来判断。
    /// </remarks>
    private static readonly string[] ResumeRejectedPhrases =
    [
        "no conversation found",
        "no saved session found",
        "session not found",
        // 账号切换护栏（中英两种措辞都出现过）。
        "已绑定另外",
        "bound to another account"
    ];

    private readonly ILogger<StreamJsonAdapter> _logger;
    private readonly Dictionary<string, CliAgentTokenUsage> _usage = new(StringComparer.Ordinal);
    private readonly StringBuilder _transcript = new();

    private string? _sessionId;
    private string? _finalResultText;
    private string? _lastAssistantText;
    private bool _sawResult;
    private bool _resultIsError;
    private int _numTurns;
    private decimal _totalCostUsd;
    private string? _requestedResumeSessionId;

    /// <summary>初始化 stream-json 适配器。</summary>
    public StreamJsonAdapter(ILogger<StreamJsonAdapter> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

    /// <inheritdoc />
    public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
    {
        Check.NotNull(context);

        var args = new List<string>
        {
            "-p",
            "--output-format", "stream-json",
            "--input-format", "stream-json",
            "--verbose",
            // 框架跑的是无人值守会话，工具审批没有可交互的人。
            "--permission-mode", "bypassPermissions",
            // 交互式提问工具在无人值守模式下必然拿到空回答，agent 会"自行推断"继续，
            // 而用户永远看不到那个问题。需要澄清应该走框架自己的澄清通道。
            "--disallowedTools", "AskUserQuestion"
        };

        args.AddRange(context.Provider.LaunchArgs);

        if (!string.IsNullOrWhiteSpace(context.McpConfigPath))
        {
            args.Add("--mcp-config");
            args.Add(context.McpConfigPath);
            // 有受管配置时就以它为准，包括显式的空对象；没有才让 CLI 继承本机 MCP 配置。
            args.Add("--strict-mcp-config");
        }

        if (!string.IsNullOrWhiteSpace(context.Model))
        {
            args.Add("--model");
            args.Add(context.Model);
        }

        if (!string.IsNullOrWhiteSpace(context.ThinkingLevel))
        {
            args.Add("--effort");
            args.Add(context.ThinkingLevel);
        }

        if (!string.IsNullOrWhiteSpace(context.ResumeSessionId))
        {
            args.Add("--resume");
            args.Add(context.ResumeSessionId);
        }

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

        // 提示词经 stdin 投递。这里内联 await 是安全的：transport 在构造时就已启动
        // 独立的 stdout 抽水任务，管道不会写满（见 ProcessTransport 的注释）。
        await transport.WriteLineAsync(BuildUserFrame(BuildPrompt(context)), cancellationToken);

        await foreach (var line in transport.ReadLinesAsync(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            StreamJsonFrame? frame;
            try
            {
                frame = JsonSerializer.Deserialize<StreamJsonFrame>(line, FrameOptions);
            }
            catch (JsonException)
            {
                // 非 JSON 行（启动横幅、告警）不是协议错误，丢弃即可。
                continue;
            }

            if (frame?.Type is null)
            {
                continue;
            }

            foreach (var evt in HandleFrame(frame, transport, cancellationToken))
            {
                yield return evt;
            }

            if (_sawResult)
            {
                // 终止信号已到。不等 stdout EOF —— 因为 stdin 还开着，它永远不会来。
                break;
            }
        }

        // 拿到终态后关 stdin，让 CLI 自行收尾退出。
        await transport.CloseInputAsync();
    }

    /// <inheritdoc />
    public CliAgentResult GetResult(CliSessionOutcome outcome)
    {
        Check.NotNull(outcome);

        var (status, failure, error) = Classify(outcome);
        var resumeRejected = DetectResumeRejection(outcome, status);

        if (resumeRejected)
        {
            failure = CliRunFailureReason.ResumeRejected;
        }

        return new CliAgentResult
        {
            Status = status,
            FailureReason = status == CliRunStatus.Completed ? null : failure,
            // 失败时不给交付物：一段被截断的记录如果被当成"最终答案"回给用户或写进工单，
            // 比一条明确的失败更糟糕。
            Output = status == CliRunStatus.Completed ? SelectOutput() : null,
            FullTranscript = _transcript.ToString(),
            Error = string.IsNullOrWhiteSpace(error) ? null : AppendStderr(error, outcome.StderrTail),
            DurationMs = (long)outcome.Elapsed.TotalMilliseconds,
            // 已知被拒的会话指针不能保存 —— 下一轮拿它续接必然再失败一次。
            SessionId = resumeRejected ? null : _sessionId,
            Usage = _usage,
            ResumeRejected = resumeRejected
        };
    }

    private string BuildPrompt(CliAgentLaunchContext context)
    {
        // 有原生记忆文件的 provider 已经从工作目录读到了 brief，再内联一遍等于每轮重复投递
        // 同一段内容，白白吃掉上下文窗口。
        if (string.IsNullOrWhiteSpace(context.InlineSystemPrompt))
        {
            return context.Prompt;
        }

        return $"{context.InlineSystemPrompt}\n\n---\n\n{context.Prompt}";
    }

    private static string BuildUserFrame(string prompt)
    {
        var payload = new
        {
            type = "user",
            message = new
            {
                role = "user",
                content = new[] { new { type = "text", text = prompt } }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private IEnumerable<CliAgentEvent> HandleFrame(
        StreamJsonFrame frame, ICliAgentTransport transport, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case "assistant":
                return HandleAssistant(frame);

            case "system":
                if (!string.IsNullOrWhiteSpace(frame.SessionId))
                {
                    _sessionId = frame.SessionId;
                }

                // 会话 ID 尽早外发：进程中途崩溃时，等终态才拿它就已经晚了。
                return [new CliAgentEvent
                {
                    Type = CliAgentEventType.Status,
                    Status = "running",
                    SessionId = _sessionId
                }];

            case "result":
                _sawResult = true;
                _finalResultText = frame.ResultText;
                _resultIsError = frame.IsError;
                _numTurns = frame.NumTurns;
                _totalCostUsd = frame.TotalCostUsd;
                if (!string.IsNullOrWhiteSpace(frame.SessionId))
                {
                    _sessionId = frame.SessionId;
                }

                MergeResultUsage(frame);
                return [];

            case "log":
                return frame.Log is null
                    ? []
                    : [new CliAgentEvent
                    {
                        Type = CliAgentEventType.Log,
                        Level = frame.Log.Level,
                        Content = frame.Log.Message
                    }];

            case "control_request":
                HandleControlRequest(frame, transport, cancellationToken);
                return [];

            case "rate_limit_event":
                // 带外事件：配额窗口播报，既不是内容也不是错误。归一化为 Status
                // （而不是 Log），上层才能把它转成「接近限额」的用户提示。
                return [new CliAgentEvent
                {
                    Type = CliAgentEventType.Status,
                    Status = "rate_limit",
                    SessionId = _sessionId
                }];

            case "user":
                // 工具结果以 user 帧回灌。它是执行痕迹，不是用户输入。
                return HandleToolResults(frame);

            default:
                _logger.LogDebug("[{Provider}] Unhandled stream-json event type '{Type}'", "stream-json", frame.Type);
                return [new CliAgentEvent
                {
                    Type = CliAgentEventType.Log,
                    Level = "debug",
                    Content = $"unhandled event type: {frame.Type}"
                }];
        }
    }

    private List<CliAgentEvent> HandleAssistant(StreamJsonFrame frame)
    {
        var events = new List<CliAgentEvent>();
        if (frame.Message is not { } messageElement)
        {
            return events;
        }

        StreamJsonMessage? message;
        try
        {
            message = messageElement.Deserialize<StreamJsonMessage>(FrameOptions);
        }
        catch (JsonException)
        {
            return events;
        }

        if (message?.Content is null)
        {
            return events;
        }

        if (message.Usage is not null && !string.IsNullOrWhiteSpace(message.Model))
        {
            AccumulateUsage(message.Model, message.Usage);
        }

        var assistantText = new StringBuilder();
        var sawToolUse = false;

        foreach (var block in message.Content)
        {
            switch (block.Type)
            {
                case "text" when !string.IsNullOrEmpty(block.Text):
                    assistantText.Append(block.Text);
                    _transcript.Append(block.Text);
                    events.Add(new CliAgentEvent { Type = CliAgentEventType.Text, Content = block.Text });
                    break;

                case "thinking":
                    var thinking = string.IsNullOrEmpty(block.Thinking) ? block.Text : block.Thinking;
                    if (!string.IsNullOrEmpty(thinking))
                    {
                        events.Add(new CliAgentEvent { Type = CliAgentEventType.Thinking, Content = thinking });
                    }

                    break;

                case "tool_use":
                    sawToolUse = true;
                    events.Add(new CliAgentEvent
                    {
                        Type = CliAgentEventType.ToolUse,
                        Tool = block.Name,
                        CallId = block.Id,
                        Input = ToDictionary(block.Input)
                    });
                    break;
            }
        }

        // 调了工具的这一轮是中间态，哪怕它也带了叙述文本。把它当成"空结果时的兜底答案"
        // 会把「我先看一下日志」当成最终交付物回给用户。
        _lastAssistantText = sawToolUse ? null : assistantText.ToString();
        return events;
    }

    private List<CliAgentEvent> HandleToolResults(StreamJsonFrame frame)
    {
        var events = new List<CliAgentEvent>();
        if (frame.Message is not { } messageElement)
        {
            return events;
        }

        StreamJsonMessage? message;
        try
        {
            message = messageElement.Deserialize<StreamJsonMessage>(FrameOptions);
        }
        catch (JsonException)
        {
            return events;
        }

        if (message?.Content is null)
        {
            return events;
        }

        foreach (var block in message.Content.Where(b => b.Type == "tool_result"))
        {
            events.Add(new CliAgentEvent
            {
                Type = CliAgentEventType.ToolResult,
                CallId = block.ToolUseId,
                Output = RenderContent(block.Content),
                Status = block.IsError ? "failed" : "completed"
            });
        }

        return events;
    }

    private void HandleControlRequest(
        StreamJsonFrame frame, ICliAgentTransport transport, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(frame.RequestId))
        {
            return;
        }

        Dictionary<string, object?> input = [];
        if (frame.Request is { } requestElement)
        {
            try
            {
                var request = requestElement.Deserialize<StreamJsonControlRequest>(FrameOptions);
                input = ToDictionary(request?.Input) as Dictionary<string, object?> ?? [];
            }
            catch (JsonException)
            {
                // 解析不了就按空入参放行：拒绝应答会让子进程挂到它自己的超时。
            }
        }

        var response = new
        {
            type = "control_response",
            response = new
            {
                subtype = "success",
                request_id = frame.RequestId,
                response = new
                {
                    behavior = "allow",
                    updatedInput = input
                }
            }
        };

        // 应答走同一条 stdin。不应答的后果是子进程阻塞到它的内部超时，整个任务卡死。
        _ = transport.WriteLineAsync(JsonSerializer.Serialize(response), cancellationToken);
    }

    private void AccumulateUsage(string model, StreamJsonUsage usage)
    {
        var existing = _usage.GetValueOrDefault(model) ?? new CliAgentTokenUsage();
        _usage[model] = existing with
        {
            InputTokens = existing.InputTokens + usage.InputTokens,
            OutputTokens = existing.OutputTokens + usage.OutputTokens,
            CacheReadTokens = existing.CacheReadTokens + usage.CacheReadInputTokens,
            CacheWriteTokens = existing.CacheWriteTokens + usage.CacheCreationInputTokens
        };
    }

    private void MergeResultUsage(StreamJsonFrame frame)
    {
        if (frame.ModelUsage is not { Count: > 0 })
        {
            return;
        }

        // result 帧的 modelUsage 是本轮的权威汇总，直接覆盖逐帧累加的结果：
        // 两者相加会双计。
        _usage.Clear();
        foreach (var (model, usage) in frame.ModelUsage)
        {
            _usage[model] = new CliAgentTokenUsage
            {
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                CacheReadTokens = usage.CacheReadInputTokens,
                CacheWriteTokens = usage.CacheCreationInputTokens,
                ReportedCostUsd = usage.CostUsd
            };
        }
    }

    private (CliRunStatus Status, CliRunFailureReason? Failure, string? Error) Classify(CliSessionOutcome outcome)
    {
        if (outcome.Cancelled)
        {
            return (CliRunStatus.Cancelled, CliRunFailureReason.Cancelled, "Execution cancelled.");
        }

        if (outcome.WatchdogFailure is { } watchdog)
        {
            return (CliRunStatus.TimedOut, watchdog, $"External agent timed out ({watchdog}).");
        }

        if (_resultIsError)
        {
            return (CliRunStatus.Failed, CliRunFailureReason.ProviderError,
                string.IsNullOrWhiteSpace(_finalResultText)
                    ? "The external agent returned an error result without details."
                    : _finalResultText);
        }

        // 进程干净退出不等于协议跑完。没有 result 事件就没有终态，不能当成功。
        if (!_sawResult)
        {
            return (CliRunStatus.Failed, CliRunFailureReason.ProcessCrashed,
                "The external agent stream ended without a terminal result event.");
        }

        if (outcome.ExitCode is { } exitCode && exitCode != 0)
        {
            return (CliRunStatus.Failed, CliRunFailureReason.ProcessCrashed,
                $"The external agent exited with code {exitCode}.");
        }

        return (CliRunStatus.Completed, null, null);
    }

    private bool DetectResumeRejection(CliSessionOutcome outcome, CliRunStatus status)
    {
        // 没请求过续接就无所谓被拒；成功的运行同理。
        if (string.IsNullOrWhiteSpace(_requestedResumeSessionId) || status == CliRunStatus.Completed)
        {
            return false;
        }

        // 取消与看门狗超时是框架自己造成的，绝不能算作 resume 被拒 ——
        // 那会白白丢掉一个仍然有效的会话指针。
        if (outcome.Cancelled || outcome.WatchdogFailure is not null)
        {
            return false;
        }

        var haystack = $"{outcome.StderrTail}\n{_finalResultText}";
        if (ResumeRejectedPhrases.Any(p => haystack.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 辅助佐证：一轮都没跑、也没花钱，且 CLI 非零退出 —— 会话根本没能建立起来。
        // 单独任一条都不够（正常的空回答也可能 0 turn），三条同时成立才认。
        return outcome.ExitCode is not null and not 0 && _numTurns == 0 && _totalCostUsd == 0m
               && !string.IsNullOrWhiteSpace(outcome.StderrTail);
    }

    private string SelectOutput()
    {
        if (!string.IsNullOrWhiteSpace(_finalResultText))
        {
            return _finalResultText;
        }

        if (!string.IsNullOrWhiteSpace(_lastAssistantText))
        {
            return _lastAssistantText;
        }

        return "The external agent completed without a final response.";
    }

    private static string AppendStderr(string error, string stderrTail)
        => string.IsNullOrWhiteSpace(stderrTail) ? error : $"{error}\n--- stderr ---\n{stderrTail.TrimEnd()}";

    private static IReadOnlyDictionary<string, object?>? ToDictionary(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } obj)
        {
            return null;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
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

    private static string? RenderContent(JsonElement? element)
    {
        if (element is not { } value)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Array => string.Join(
                "\n",
                value.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var text)
                        ? text.GetString()
                        : item.GetRawText())
                    .Where(s => !string.IsNullOrEmpty(s))),
            _ => value.GetRawText()
        };
    }
}
