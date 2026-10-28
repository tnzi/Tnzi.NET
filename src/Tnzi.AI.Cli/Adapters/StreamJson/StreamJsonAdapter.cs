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

    /// <summary>唯一一种许可请求的 <c>control_request</c> 子类型。</summary>
    private const string CanUseToolSubtype = "can_use_tool";

    /// <summary>许可请求没带入参时回传的空对象。</summary>
    private static readonly JsonElement EmptyToolInput = CreateEmptyObject();

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
        // 截断续接（--resume-session-at）指向的条目不在会话里：实测 stderr "No message found with message.uuid of: <id>"，exit=1。
        // 与会话不存在同样处置：开新会话重试，并在这一轮里说明之前的对话已不可用。
        "no message found with message.uuid",
        "session not found",
        // 账号切换护栏（中英两种措辞都出现过）。
        "已绑定另外",
        "bound to another account"
    ];

    private readonly ILogger<StreamJsonAdapter> _logger;

    // 逐帧看到的用量，按 API 回复的 id 记一次（同一回复的几帧带同一份用量）；没有 id 的帧各算一次
    private readonly Dictionary<string, (string Model, StreamJsonUsage Usage)> _messageUsage = new(StringComparer.Ordinal);
    private int _unidentifiedMessages;

    // result 帧报的会话累计值（按模型）与本轮主循环用量
    private Dictionary<string, CliAgentTokenUsage>? _reportedTotals;
    private CliAgentTokenUsage? _turnUsage;

    private readonly StringBuilder _transcript = new();

    private string? _sessionId;
    private string? _lastEntryId;
    private string? _finalResultText;
    private string? _lastAssistantText;
    private bool _sawResult;
    private bool _resultIsError;
    private int _numTurns;
    private decimal _totalCostUsd;
    private string? _requestedResumeSessionId;
    private bool _resumeRejectionDetectable;
    private CliPermissionMode _permissionMode;
    private bool _asksForApproval;
    private ICliAgentTransport? _transport;

    /// <summary>等使用方回答的 can_use_tool，按 request_id。回答可能从执行器的另一个线程来。</summary>
    private readonly ConcurrentDictionary<string, PendingApproval> _pendingApprovals = new(StringComparer.Ordinal);

    // 账号用量上限：assistant 帧以 api_error = usage_limit_reached 报出（这是确证），或者先来一条 rejected 的
    // rate_limit_event、随后 result 以错误结束（旧版本只有这一路）
    private bool _usageLimitReached;
    private string? _usageLimitNotice;
    private DateTimeOffset? _usageLimitResetsAt;
    private StreamJsonRateLimit? _rejectedRateLimit;
    private bool _rateLimited;

    /// <summary>这次运行最后一次报出的配额窗口状态（<c>rate_limit_event</c>）；没收到过时为 null。</summary>
    internal StreamJsonRateLimit? LastRateLimitInfo { get; private set; }

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
            // 档位按运行定（启动档案）；缺省全权，无人值守会话里工具审批没有可交互的人
            "--permission-mode", PermissionModeValue(context.PermissionMode),
            // 交互式提问工具在无人值守模式下必然拿到空回答，agent 会"自行推断"继续，
            // 而用户永远看不到那个问题。需要澄清应该走框架自己的澄清通道。
            "--disallowedTools", "AskUserQuestion"
        };

        args.AddRange(context.Provider.LaunchArgs);

        if (!string.IsNullOrWhiteSpace(context.McpConfigPath))
        {
            args.Add("--mcp-config");
            args.Add(context.McpConfigPath);

            // strict：只用受管配置，包括显式的空对象。不 strict：与 CLI 自己找到的（用户级、项目级）合并，同名时受管配置里的生效
            if (context.StrictMcpConfig)
            {
                args.Add("--strict-mcp-config");
            }
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

            // 截断续接：只保留会话里这个条目及其之前的对话，分叉成一个新会话（原会话不动）。
            // 不加 --fork-session 时截断会在原会话里长出一条分支，下次按会话 id 续接接上的是哪一支取决于 CLI 的取舍。
            if (!string.IsNullOrWhiteSpace(context.ResumeAtEntryId))
            {
                args.Add("--resume-session-at");
                args.Add(context.ResumeAtEntryId);
                args.Add("--fork-session");
            }
        }

        // 隔离、启动档案、追加系统提示与附加目录：排在部署级参数之前。档案没接管的开关，部署方写在后面的那个生效
        args.AddRange(context.ManagedArgs);

        var blocked = CliArgumentFilter.Combine(context.Provider.BlockedArgs, context.OwnedArgs, RunOwnedArgs(context));
        args.AddRange(CliArgumentFilter.Filter(context.ExtraArgs, blocked, _logger, context.Provider.Key));
        args.AddRange(CliArgumentFilter.Filter(context.CustomArgs, blocked, _logger, context.Provider.Key));

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

    /// <summary>档位在 stream-json 上的 <c>--permission-mode</c> 取值。</summary>
    /// <remarks>
    /// 只读与先问我都是 <c>default</c>：没有批准通道时，要权限的操作被拒绝、记在 <c>permission_denials</c> 里，读与查资料照常。
    /// 不用 <c>plan</c>：它只出计划、不执行任何工具。
    /// </remarks>
    internal static string PermissionModeValue(CliPermissionMode mode) => mode switch
    {
        CliPermissionMode.Full => "bypassPermissions",
        CliPermissionMode.EditWorkspace => "acceptEdits",
        _ => "default"
    };

    /// <summary>
    /// 按这次运行的档位，部署级参数与自定义参数里还要丢弃的开关：不 strict 时的 <c>--strict-mcp-config</c>，
    /// 非全权时能绕过档位的 <c>--dangerously-skip-permissions</c>。
    /// </summary>
    private static Dictionary<string, BlockedArgMode> RunOwnedArgs(CliAgentLaunchContext context)
    {
        var owned = new Dictionary<string, BlockedArgMode>(StringComparer.Ordinal);
        if (!context.StrictMcpConfig)
        {
            owned["--strict-mcp-config"] = BlockedArgMode.Standalone;
        }

        if (context.PermissionMode != CliPermissionMode.Full)
        {
            owned["--dangerously-skip-permissions"] = BlockedArgMode.Standalone;
        }

        return owned;
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
        _permissionMode = context.PermissionMode;
        _asksForApproval = context.AsksForApproval && context.PermissionMode == CliPermissionMode.Ask;
        _transport = transport;
        _resumeRejectionDetectable = context.Provider.ResumeRejectionDetectable;

        // 提示词经 stdin 投递。这里内联 await 是安全的：transport 在构造时就已启动
        // 独立的 stdout 抽水任务，管道不会写满（见 ProcessTransport 的注释）。
        await transport.WriteLineAsync(BuildUserFrame(BuildPrompt(context), context.Attachments), cancellationToken);

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
        var resumeRejected = failure != CliRunFailureReason.UsageLimit && DetectResumeRejection(outcome, status);

        if (resumeRejected)
        {
            failure = CliRunFailureReason.ResumeRejected;
        }

        var (usage, isSessionTotal) = SelectUsage();

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
            SessionEntryId = resumeRejected ? null : _lastEntryId,
            Usage = usage,
            UsageIsSessionTotal = isSessionTotal,
            TurnUsage = isSessionTotal ? _turnUsage : null,
            ResumeRejected = resumeRejected,
            UsageLimitResetsAt = failure == CliRunFailureReason.UsageLimit ? _usageLimitResetsAt : null
        };
    }

    /// <summary>
    /// 这条 assistant 帧是不是「账号用量上限到了」的拒绝：<c>api_error</c> 是 <c>usage_limit_reached</c>，
    /// 或公开分类是 <c>rate_limit</c> 且带着一份 rejected 的 <c>rate_limit_info</c>。
    /// </summary>
    private static bool IsUsageLimitRefusal(StreamJsonFrame frame, out StreamJsonRateLimit? limit)
    {
        limit = frame.ApiErrorParams is { ValueKind: JsonValueKind.Object } parameters
                && parameters.TryGetProperty("rate_limit_info", out var info)
            ? StreamJsonRateLimit.Read(info)
            : null;

        if (ReadString(frame.ApiError) == "usage_limit_reached")
        {
            return true;
        }

        return ReadString(frame.Error) == "rate_limit" && limit is { IsRejected: true };
    }

    private static string? ReadString(JsonElement? element)
        => element is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

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

    /// <summary>
    /// 一条 user 输入帧：附件在前（图片、PDF 文档块，base64 source），文本在后。
    /// </summary>
    /// <remarks>
    /// 附件排在文本前面是 Claude 对多模态输入的建议顺序。附件是文档时带上文件名作 title，模型据此在回答里称呼它。
    /// 没有文本（只发了附件）时不发空文本块：空文本块会被 API 拒收。
    /// </remarks>
    internal static string BuildUserFrame(string prompt, IReadOnlyList<CliPromptAttachment> attachments)
    {
        var content = new List<object>(attachments.Count + 1);
        foreach (var attachment in attachments)
        {
            var source = new
            {
                type = "base64",
                media_type = attachment.MediaType,
                data = Convert.ToBase64String(attachment.Content)
            };

            content.Add(attachment.IsImage
                ? new { type = "image", source }
                : string.IsNullOrWhiteSpace(attachment.FileName)
                    ? new { type = "document", source }
                    : new { type = "document", source, title = attachment.FileName });
        }

        if (!string.IsNullOrWhiteSpace(prompt) || content.Count == 0)
        {
            content.Add(new { type = "text", text = prompt });
        }

        var payload = new
        {
            type = "user",
            message = new
            {
                role = "user",
                content
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private IEnumerable<CliAgentEvent> HandleFrame(
        StreamJsonFrame frame, ICliAgentTransport transport, CancellationToken cancellationToken)
    {
        // 会话里的对话条目（assistant 消息、回灌的工具结果）：最后一个就是以后截断续接时要保留到的位置
        if (frame.Type is "assistant" or "user" && !string.IsNullOrWhiteSpace(frame.Uuid))
        {
            _lastEntryId = frame.Uuid;
        }

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

                RecordResultUsage(frame);
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
                return HandleControlRequest(frame, transport, cancellationToken);

            case "rate_limit_event":
            {
                // 带外事件：配额窗口播报，既不是内容也不是错误。归一化为 Status
                // （而不是 Log），上层才能把它转成「接近限额」的用户提示；窗口状态（allowed / allowed_warning / rejected）放在 Content。
                var info = StreamJsonRateLimit.Read(frame.RateLimitInfo);
                if (info is not null)
                {
                    LastRateLimitInfo = info;
                    if (info.IsRejected)
                    {
                        _rejectedRateLimit = info;
                    }
                }

                return [new CliAgentEvent
                {
                    Type = CliAgentEventType.Status,
                    Status = "rate_limit",
                    Content = info?.Status,
                    SessionId = _sessionId
                }];
            }

            case "user":
                // 工具结果以 user 帧回灌。它是执行痕迹，不是用户输入。
                return HandleToolResults(frame);

            case "tool_progress":
                // 工具执行中的心跳（MCP 工具约每 30 秒一次，Bash / PowerShell 的带 task_id），刻意不产出事件：
                // ACP 的进行中 tool_call_update 同样丢弃，只有 stream-json 才发的信号会让上层按 provider 分支；
                // 「这个工具跑了多久」从 ToolUse 那条事件的时间就算得出；而每条事件都落库，长工具调用会每分钟多两行。
                // 看门狗不受影响：有工具在跑时它只按 ToolWatchdog 从工具开始计时。
                return [];

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
            RecordMessageUsage(message.Id, message.Model, message.Usage);
        }

        if (IsUsageLimitRefusal(frame, out var limit))
        {
            // CLI 把「账号用量上限到了」包成一条 assistant 消息（文字如 "You've hit your limit · resets 3pm"）。
            // 它不是模型的回答：不当文本发出、不进交付物，作为一条状态事件留在时间线上，运行以 UsageLimit 结束。
            var notice = string.Concat(message.Content.Where(b => b.Type == "text").Select(b => b.Text));
            _usageLimitReached = true;
            _usageLimitNotice = string.IsNullOrWhiteSpace(notice) ? _usageLimitNotice : notice.Trim();
            _usageLimitResetsAt = limit?.ResetsAt ?? _rejectedRateLimit?.ResetsAt ?? _usageLimitResetsAt;
            _transcript.Append(notice);
            events.Add(new CliAgentEvent
            {
                Type = CliAgentEventType.Status,
                Status = "usage_limit",
                Content = _usageLimitNotice
            });
            return events;
        }

        if (ReadString(frame.Error) == "rate_limit")
        {
            // 同一个 rate_limit 分类下的其它拒绝（组织级的限流之类）不带账号上限的信息：按限流处理，不报重置时间
            _rateLimited = true;
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
            var blocks = ReadBlocks(block.Content);
            events.Add(new CliAgentEvent
            {
                Type = CliAgentEventType.ToolResult,
                CallId = block.ToolUseId,
                Output = RenderBlocks(blocks),
                OutputBlocks = blocks.Count > 0 ? blocks : null,
                Status = block.IsError ? "failed" : "completed"
            });
        }

        return events;
    }

    private IEnumerable<CliAgentEvent> HandleControlRequest(
        StreamJsonFrame frame, ICliAgentTransport transport, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(frame.RequestId))
        {
            return [];
        }

        StreamJsonControlRequest? request = null;
        if (frame.Request is { } requestElement)
        {
            try
            {
                request = requestElement.Deserialize<StreamJsonControlRequest>(FrameOptions);
            }
            catch (JsonException)
            {
                // 解析不了就不知道在批准什么：下面按错误应答（仍然应答，见末尾）。
            }
        }

        var isPermissionRequest = string.Equals(request?.Subtype, CanUseToolSubtype, StringComparison.Ordinal);

        // 「先问我」且有人值守：不当场应答，交给使用方。CLI 停在这一个工具调用上等，读循环照常往下读
        if (isPermissionRequest && _asksForApproval)
        {
            return [AskForApproval(frame.RequestId, request!)];
        }

        // 只有 can_use_tool 是许可请求。updatedInput 会<b>替换</b>工具的入参，所以原样回传请求里的 JSON，
        // 不经任何转换：此前先拷成字典，嵌套的数组 / 对象被压成字符串，MultiEdit 的 edits、TodoWrite 的 todos
        // 这类参数一经批准就坏了。其它子类型（hook 回调、SDK MCP 消息）本适配器从不注册，回一份 allow 形状的应答
        // 等于答非所问，改回错误应答。
        // 只有全权档自动批准：别的档位下 CLI 自己按 --permission-mode 放行能放行的，问到这里的就是要人批准的，这一轮没有人可问
        object response = isPermissionRequest && _permissionMode != CliPermissionMode.Full
            ? DenyResponse(frame.RequestId, "This run's permission mode does not allow this action, and there is no one to approve it.")
            : isPermissionRequest
            ? AllowResponse(frame.RequestId, request!.Input, updatedPermissions: null)
            : new
            {
                type = "control_response",
                response = new
                {
                    subtype = "error",
                    request_id = frame.RequestId,
                    error = $"Unsupported control request subtype '{request?.Subtype ?? "unknown"}'."
                }
            };

        // 应答走同一条 stdin。不应答的后果是子进程阻塞到它的内部超时，整个任务卡死。
        // 帧翻译是同步的，等不了这次写入；写失败（stdin 已关、进程已退）至少要留下痕迹，
        // 否则随后的看门狗超时看不出是这条应答没送到。
        transport.WriteLineAsync(JsonSerializer.Serialize(response), cancellationToken).ContinueWith(
            task => _logger.LogWarning(task.Exception,
                "Could not answer control request {RequestId}; the CLI may wait for its own timeout", frame.RequestId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return [];
    }

    /// <summary>
    /// 记下一个等使用方回答的 <c>can_use_tool</c>，产出权限请求事件。
    /// </summary>
    /// <remarks>
    /// 选项由适配器按协议能做到的给出：允许一次、拒绝，CLI 给了「以后不再问」的建议规则时多一个「本会话总是允许」
    /// （应答时把那些规则的去处一律改成 <c>session</c>：建议里常是 <c>localSettings</c>，照原样回答会往用户的仓库里写设置文件）。
    /// </remarks>
    private CliAgentEvent AskForApproval(string requestId, StreamJsonControlRequest request)
    {
        var suggestions = request.PermissionSuggestions is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 0
            ? array.Clone()
            : (JsonElement?)null;

        _pendingApprovals[requestId] = new PendingApproval(
            request.Input is { ValueKind: JsonValueKind.Object } input ? input.Clone() : null, suggestions);

        var options = new List<CliPermissionOption>
        {
            new(CliPermissionOptionKinds.AllowOnce, "Allow once", CliPermissionOptionKinds.AllowOnce)
        };
        if (suggestions is not null)
        {
            options.Add(new CliPermissionOption(
                CliPermissionOptionKinds.AllowAlways, "Allow for this session", CliPermissionOptionKinds.AllowAlways));
        }

        options.Add(new CliPermissionOption(CliPermissionOptionKinds.RejectOnce, "Deny", CliPermissionOptionKinds.RejectOnce));

        return new CliAgentEvent
        {
            Type = CliAgentEventType.PermissionRequest,
            Tool = request.ToolName,
            CallId = request.ToolUseId,
            Input = ToDictionary(request.Input),
            Content = StripAnsi(request.Description),
            SessionId = _sessionId,
            Permission = new CliAgentPermission
            {
                ProtocolKey = requestId,
                Title = StripAnsi(request.Title ?? request.DisplayName),
                Reason = StripAnsi(request.DecisionReason),
                BlockedPath = request.BlockedPath,
                Options = options
            }
        };
    }

    /// <inheritdoc />
    public async Task<bool> AnswerPermissionAsync(string protocolKey, CliPermissionDecision decision, CancellationToken cancellationToken)
    {
        Check.NotNullOrWhiteSpace(protocolKey);
        Check.NotNull(decision);

        // 一个请求只应答一次：先摘掉再写，同一个键第二次来（超时与回答赛跑）什么都不写
        if (_transport is null || !_pendingApprovals.TryRemove(protocolKey, out var pending))
        {
            return false;
        }

        var keepAsking = decision.Allow
                         && string.Equals(decision.OptionId, CliPermissionOptionKinds.AllowAlways, StringComparison.Ordinal);
        object response = decision.Allow
            ? AllowResponse(protocolKey, pending.Input, keepAsking ? SessionScoped(pending.Suggestions) : null)
            : DenyResponse(protocolKey, string.IsNullOrWhiteSpace(decision.Message)
                ? "The user did not allow this action."
                : decision.Message);

        await _transport.WriteLineAsync(JsonSerializer.Serialize(response), cancellationToken);
        return true;
    }

    private static object DenyResponse(string requestId, string message) => new
    {
        type = "control_response",
        response = new
        {
            subtype = "success",
            request_id = requestId,
            response = new { behavior = "deny", message }
        }
    };

    private static object AllowResponse(string requestId, JsonElement? input, JsonNode? updatedPermissions)
    {
        var decision = new JsonObject
        {
            ["behavior"] = "allow",
            ["updatedInput"] = JsonNode.Parse((input is { ValueKind: JsonValueKind.Object } toolInput ? toolInput : EmptyToolInput).GetRawText())
        };
        if (updatedPermissions is not null)
        {
            decision["updatedPermissions"] = updatedPermissions;
        }

        return new JsonObject
        {
            ["type"] = "control_response",
            ["response"] = new JsonObject
            {
                ["subtype"] = "success",
                ["request_id"] = requestId,
                ["response"] = decision
            }
        };
    }

    /// <summary>CLI 建议的规则，去处一律改成本会话：不往用户级、项目级设置文件里写任何东西。</summary>
    private static JsonNode? SessionScoped(JsonElement? suggestions)
    {
        if (suggestions is not { ValueKind: JsonValueKind.Array } array || JsonNode.Parse(array.GetRawText()) is not JsonArray rules)
        {
            return null;
        }

        foreach (var rule in rules.OfType<JsonObject>())
        {
            rule["destination"] = "session";
        }

        return rules;
    }

    /// <summary>去掉 ANSI 转义（CLI 的说明可能带颜色码）。</summary>
    private static string? StripAnsi(string? text)
        => string.IsNullOrEmpty(text) ? text : AnsiEscape.Replace(text, string.Empty);

    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    /// <summary>一个等回答的 can_use_tool：应答允许时要原样回传入参，「本会话总是允许」要带上建议规则。</summary>
    private sealed record PendingApproval(JsonElement? Input, JsonElement? Suggestions);

    /// <summary>
    /// 记下一帧 assistant 消息的用量。同一个 API 回复的几帧（每个内容块一帧）只记一次，取最后一帧的那份。
    /// </summary>
    private void RecordMessageUsage(string? messageId, string model, StreamJsonUsage usage)
    {
        var key = string.IsNullOrWhiteSpace(messageId) ? $"#{++_unidentifiedMessages}" : messageId;
        _messageUsage[key] = (model, usage);
    }

    /// <summary>
    /// 记下 result 帧报的用量：<c>modelUsage</c> 是会话累计值（续接的会话从它自己保存的总量接着算），
    /// <c>usage</c> 是这一轮主循环的用量。两份分开存，折算交给执行器。
    /// </summary>
    private void RecordResultUsage(StreamJsonFrame frame)
    {
        if (frame.Usage is { } turn)
        {
            _turnUsage = ToTokenUsage(turn);
        }

        if (frame.ModelUsage is not { Count: > 0 })
        {
            return;
        }

        _reportedTotals = new Dictionary<string, CliAgentTokenUsage>(StringComparer.Ordinal);
        foreach (var (model, usage) in frame.ModelUsage)
        {
            _reportedTotals[model] = new CliAgentTokenUsage
            {
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                CacheReadTokens = usage.CacheReadInputTokens,
                CacheWriteTokens = usage.CacheCreationInputTokens,
                ReportedCostUsd = usage.CostUsd
            };
        }
    }

    /// <summary>
    /// 交给执行器的用量：有 result 帧的累计值就用它（它覆盖工具内部、子 agent、压缩等全部模型调用），否则用逐帧看到的。
    /// </summary>
    /// <remarks>
    /// 进程启动失败的 result 可能带一份全零的累计值，那时逐帧看到的才是已经花掉的那部分。
    /// 逐帧的数字只覆盖主循环与子 agent 的回复（工具内部调用的模型看不见），所以只在拿不到累计值时用。
    /// </remarks>
    private (IReadOnlyDictionary<string, CliAgentTokenUsage> Usage, bool IsSessionTotal) SelectUsage()
    {
        var streamed = AggregateMessageUsage();

        if (_reportedTotals is { Count: > 0 } totals && (HasTokens(totals.Values) || !HasTokens(streamed.Values)))
        {
            return (totals, true);
        }

        return (streamed, false);
    }

    private Dictionary<string, CliAgentTokenUsage> AggregateMessageUsage()
    {
        var byModel = new Dictionary<string, CliAgentTokenUsage>(StringComparer.Ordinal);
        foreach (var (model, usage) in _messageUsage.Values)
        {
            var existing = byModel.GetValueOrDefault(model) ?? new CliAgentTokenUsage();
            byModel[model] = existing with
            {
                InputTokens = existing.InputTokens + usage.InputTokens,
                OutputTokens = existing.OutputTokens + usage.OutputTokens,
                CacheReadTokens = existing.CacheReadTokens + usage.CacheReadInputTokens,
                CacheWriteTokens = existing.CacheWriteTokens + usage.CacheCreationInputTokens
            };
        }

        return byModel;
    }

    private static bool HasTokens(IEnumerable<CliAgentTokenUsage> usage)
        => usage.Any(u => u.InputTokens + u.OutputTokens + u.CacheReadTokens + u.CacheWriteTokens > 0);

    private static CliAgentTokenUsage ToTokenUsage(StreamJsonUsage usage) => new()
    {
        InputTokens = usage.InputTokens,
        OutputTokens = usage.OutputTokens,
        CacheReadTokens = usage.CacheReadInputTokens,
        CacheWriteTokens = usage.CacheCreationInputTokens
    };

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

        // 账号用量上限：确证（api_error）不看 result；只有 rejected 的配额播报时，要 result 也以错误结束才算
        if (_usageLimitReached || (_resultIsError && _rejectedRateLimit is not null))
        {
            _usageLimitResetsAt ??= _rejectedRateLimit?.ResetsAt;
            var notice = _usageLimitNotice ?? (string.IsNullOrWhiteSpace(_finalResultText) ? null : _finalResultText);
            return (CliRunStatus.Failed, CliRunFailureReason.UsageLimit,
                notice ?? "The external agent's account has reached its usage limit.");
        }

        if (_resultIsError && _rateLimited)
        {
            return (CliRunStatus.Failed, CliRunFailureReason.RateLimited,
                string.IsNullOrWhiteSpace(_finalResultText) ? "The external agent was rate limited." : _finalResultText);
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

    /// <remarks>
    /// 下面的短语表与辅助佐证是对内置 provider 实测出来的。provider 没有声明
    /// <see cref="CliProviderDescriptor.ResumeRejectionDetectable"/>（自定义 provider 一律如此）时一概不判：
    /// 一个没验证过的 CLI 启动时因认证或网络失败、stderr 里有输出，辅助佐证照样成立，
    /// 判成「被拒」会清掉仍然有效的会话指针并开新会话重跑，丢掉整段历史。分不清就不判（fail-closed），
    /// 这一轮以失败结束，会话指针留给下一轮。此前描述表上的这个能力位没有任何读者。
    /// </remarks>
    private bool DetectResumeRejection(CliSessionOutcome outcome, CliRunStatus status)
    {
        // 没请求过续接就无所谓被拒；成功的运行同理。
        if (string.IsNullOrWhiteSpace(_requestedResumeSessionId) || status == CliRunStatus.Completed)
        {
            return false;
        }

        if (!_resumeRejectionDetectable)
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

    private static JsonElement CreateEmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

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
                // 嵌套的对象与数组原样保留成 JSON：此前存成它们的原始文本（一个字符串），
                // 落库与流式时再序列化就成了带引号转义的字符串，工具卡片上 MultiEdit 的 edits、TodoWrite 的 todos 都看不成结构
                _ => property.Value.Clone()
            };
        }

        return result;
    }

    /// <summary>
    /// 工具结果的内容 → 分块：字符串是一个文本块；数组里的 text 块与 image 块（base64 source）按序保留，
    /// 认不出的块以其原始 JSON 作文本块（不丢）。
    /// </summary>
    internal static List<CliContentBlock> ReadBlocks(JsonElement? element)
    {
        var blocks = new List<CliContentBlock>();
        if (element is not { } value)
        {
            return blocks;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var text = value.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    blocks.Add(new CliContentBlock { Type = CliContentBlock.TextType, Text = text });
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (ReadBlock(item) is { } block)
                    {
                        blocks.Add(block);
                    }
                }

                break;

            case JsonValueKind.Null or JsonValueKind.Undefined:
                break;

            default:
                blocks.Add(new CliContentBlock { Type = CliContentBlock.TextType, Text = value.GetRawText() });
                break;
        }

        return blocks;
    }

    private static CliContentBlock? ReadBlock(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return new CliContentBlock { Type = CliContentBlock.TextType, Text = item.GetRawText() };
        }

        var type = item.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

        if (type == "text")
        {
            var text = item.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
                ? textElement.GetString()
                : null;
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return ReadFlattenedResourceLink(text) ?? new CliContentBlock { Type = CliContentBlock.TextType, Text = text };
        }

        if (type == "resource_link"
            && item.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String)
        {
            // MCP 的 resource_link 原样到达时（claude 2.1.303 还不这样做，见 ReadFlattenedResourceLink）
            return new CliContentBlock
            {
                Type = CliContentBlock.FileType,
                Uri = uri.GetString(),
                Name = item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null,
                MediaType = item.TryGetProperty("mimeType", out var mime) && mime.ValueKind == JsonValueKind.String ? mime.GetString() : null
            };
        }

        if (type == "image"
            && item.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object
            && source.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
        {
            var mediaType = source.TryGetProperty("media_type", out var media) && media.ValueKind == JsonValueKind.String
                ? media.GetString()
                : null;
            return new CliContentBlock { Type = CliContentBlock.ImageType, MediaType = mediaType, Data = data.GetString() };
        }

        return new CliContentBlock { Type = CliContentBlock.TextType, Text = item.GetRawText() };
    }

    /// <summary>
    /// 一个文本块是不是 claude 把 MCP <c>resource_link</c> 压平成的那一行：<c>[Resource link: 名字] 地址</c>。
    /// </summary>
    /// <remarks>
    /// claude 2.1.303 实测如此（媒体类型与大小在这一步就丢了），真机冒烟测试守着这个形态。认出来就还原成文件块，
    /// 工具交给用户的文件在聊天里因此是一张文件卡片而不是一行文字。整块必须恰好是这一行，模型或工具自己写的
    /// 普通文字不受影响。
    /// </remarks>
    internal static CliContentBlock? ReadFlattenedResourceLink(string text)
    {
        var match = FlattenedResourceLink.Match(text.Trim());
        return match.Success
            ? new CliContentBlock { Type = CliContentBlock.FileType, Name = match.Groups["name"].Value, Uri = match.Groups["uri"].Value }
            : null;
    }

    private static readonly Regex FlattenedResourceLink = new(
        @"^\[Resource link: (?<name>[^\]\r\n]+)\] (?<uri>\S+)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// 分块的文本形态（落库的 Output、时间线、日志用）：文本块按序拼接，图片块写成一行占位，不把 base64 塞进文本。
    /// </summary>
    internal static string? RenderBlocks(IReadOnlyList<CliContentBlock> blocks)
    {
        if (blocks.Count == 0)
        {
            return null;
        }

        return string.Join(
            "\n",
            blocks.Select(b => b.Type switch
                {
                    CliContentBlock.ImageType => $"[image: {b.MediaType ?? "unknown type"}]",
                    CliContentBlock.FileType => $"[file: {b.Name ?? b.Uri}] {b.Uri}",
                    _ => b.Text
                })
                .Where(s => !string.IsNullOrEmpty(s)));
    }
}
