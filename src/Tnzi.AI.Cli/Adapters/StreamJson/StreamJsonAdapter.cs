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
/// <b>一个 <c>result</c> 是一轮的结束，不一定是这个进程的结束</b>：这一轮起了后台任务（<c>run_in_background</c>）而任务还在跑时，
/// CLI 在任务完成后自己开下一轮（2.1.317 实测：<c>system/background_tasks_changed</c> 报出还在跑的任务，任务完成时先来
/// <c>task_notification</c>，接着一条新的 <c>system/init</c> 和下一轮，result 带 <c>origin.kind: task-notification</c>）。
/// 这时产出 <see cref="CliAgentEventType.TurnCompleted"/> 并接着读，直到某一轮结束时不再有后台任务，或等待到了
/// <see cref="CliAgentLaunchContext.BackgroundTaskWait"/>（到点即结束读取，执行器终止进程，后台任务随之结束）。
/// 任务在一轮之内就完成时，CLI 把结果交给同一轮，不另开一轮（同样实测）。
/// </para>
/// <para>
/// 未知事件类型一律降级为 <see cref="CliAgentEventType.Log"/>，绝不崩溃 ——
/// CLI 会随版本新增事件类型，把未知形状当错误处理等于让每次上游升级都成为一次故障。
/// </para>
/// </remarks>
public class StreamJsonAdapter : ICliProtocolAdapter, ICliTurnContinuation, ICliTaskControl, ICliTurnInput
{
    private static readonly JsonSerializerOptions FrameOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>唯一一种许可请求的 <c>control_request</c> 子类型。</summary>
    private const string CanUseToolSubtype = "can_use_tool";

    /// <summary>agent 问用户问题的工具（2.1.316 实测：带 <c>--permission-prompt-tool stdio</c> 时才在工具列表里）。</summary>
    internal const string AskUserQuestionTool = "AskUserQuestion";

    /// <summary>agent 交出计划、问能不能开始的工具（同上）。</summary>
    internal const string ExitPlanModeTool = "ExitPlanMode";

    /// <summary>没有人值守时交出 AskUserQuestion 的那个 deny：说清楚没有回答，免得 agent 当成用户选了第一项。</summary>
    private const string UnattendedQuestionMessage =
        "No one is attending this run, so these questions cannot be answered. Do not assume answers: " +
        "continue with what you can do without them, and put the questions in your reply.";

    /// <summary>没有人值守时交出 ExitPlanMode 的那个 deny。</summary>
    private const string UnattendedPlanMessage =
        "No one is attending this run, so the plan cannot be approved. Do not start implementing it: give the plan as your reply.";

    private const string DeclinedQuestionMessage =
        "The user chose not to answer these questions. Do not assume answers: continue without them, or ask in your reply.";

    private const string RejectedPlanMessage = "The user did not approve the plan. Stay in plan mode and revise it.";

    /// <summary>
    /// 交出的计划什么都没有（入参里没有 <c>plan</c>，进入计划档以来也没写过一个字）：退回给 agent 一次，让它把计划写成回复里的文字再交。
    /// 实测 claude 2.1.317 在没有 Write 的一轮里会只在思考里打好计划就交出 <c>ExitPlanMode {}</c>，人什么都看不到；
    /// 让它把计划填进 <c>plan</c> 参数没用（计划档里它照样交 <c>{}</c>），写成文字则由框架收下（<see cref="CliPlanSources.AssistantText"/>）。
    /// </summary>
    internal const string MissingPlanMessage =
        "The plan was not handed over: no plan file could be written in this session, so the person who approves it cannot see it. " +
        "Write the complete plan in your reply as text, then call ExitPlanMode again.";

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
    private bool _relaysInteractions;

    // 进入计划档以来主对话里的文字：CLI 交出的计划为空时（没有 Write 工具写不出计划文件），计划请求用它补上
    private bool _inPlanMode;
    private readonly StringBuilder _planText = new();
    private readonly StringBuilder _planThinking = new();

    // agent 调 ExitPlanMode 时自己传的 plan，按工具调用 id：CLI 发来的批准请求里可能已经没有它了
    private readonly Dictionary<string, string> _submittedPlans = new(StringComparer.Ordinal);

    // 这一段计划档里已经因为「计划是空的」退回过一次：再交还是空的就照样交给使用方，不来回打转
    private bool _sentBackEmptyPlan;

    // 主对话最后一次模型调用的用量（上下文窗口的占用）与 result 帧报的各模型窗口大小
    private (string Model, StreamJsonUsage Usage)? _lastMainUsage;
    private readonly Dictionary<string, long> _contextWindows = new(StringComparer.Ordinal);
    private string? _lastMainModel;
    private ICliAgentTransport? _transport;

    // 后台任务：CLI 最近一次报出的、还在跑的那些（整份替换），以及见过的全部后台任务 id（只为它们的完成通知出状态事件）
    private List<CliBackgroundTask> _backgroundTasks = [];
    private readonly HashSet<string> _backgroundTaskIds = new(StringComparer.Ordinal);

    // 任务（子 agent、后台命令）：任务 id → 起它的那次工具调用 id（嵌套派出时用它找上一层）。属于进程，交接之后照样认得
    private readonly Dictionary<string, string> _tasks = new(StringComparer.Ordinal);

    // 发出去、还没应答的停下任务请求：请求 id → 任务 id。应答从读取线程来，请求从执行器的后台循环来
    private readonly ConcurrentDictionary<string, string> _stopRequests = new(StringComparer.Ordinal);
    private const string StopTaskRequestPrefix = "tnzi-stop-";

    /// <summary>换强度的控制请求，按 request_id：CLI 答 error 时留一条警告日志事件。</summary>
    private readonly ConcurrentDictionary<string, string> _effortRequests = new(StringComparer.Ordinal);
    private const string EffortRequestPrefix = "tnzi-effort-";

    /// <summary>这个进程的会话现在用的推理强度：起进程时的 <c>--effort</c>，接手的运行换过之后是换成的值；null = 没给。</summary>
    private string? _sessionEffort;

    // 已经说过模型的子 agent（按起它的调用 id）
    private readonly HashSet<string> _subAgentModels = new(StringComparer.Ordinal);

    // CLI 自己写出的回复（「Not logged in」之类）标的模型名，不是任何真实模型
    private const string SyntheticModel = "<synthetic>";
    private TimeSpan _backgroundTaskWait;
    private CancellationTokenSource? _waitTimer;

    // 一轮结束而 CLI 还会开下一轮；最后一轮的 result 已到；等下一轮等到了上限
    private volatile bool _awaitingFollowUp;

    /// <summary>
    /// 一轮进行中写进去的用户消息（<see cref="SendUserMessageAsync"/>），按我们给的 uuid：收下（<c>command_lifecycle started</c>）之前
    /// 这一轮结束了的，CLI 会为它另开一轮，读取不能在那条 result 处结束。
    /// </summary>
    private readonly Dictionary<string, PendingInput> _inputs = new(StringComparer.Ordinal);

    /// <summary>这一轮的提示已经写出：在那之前写进去的消息会被 CLI 当成第一轮，排在提示前面。</summary>
    private bool _promptWritten;

    /// <summary>这一轮结束了，而写进去的消息还没被收下：等 CLI 为它开的那一轮（有上限，见 <see cref="InputPickupWait"/>）。</summary>
    private volatile bool _awaitingInput;

    /// <summary>一轮结束时写进去的消息还没被收下，等 CLI 为它开下一轮的上限（实测下一轮紧接着 result 就开始）。</summary>
    internal static readonly TimeSpan InputPickupWait = TimeSpan.FromMinutes(2);
    private volatile bool _finished;
    private bool _backgroundWaitExpired;

    // 等后台任务时交给了下一条运行、它的用户帧已写进 stdin，那一轮的 result 还没到（见 ContinueAsync）
    private bool _submittedTurnPending;

    // 逐帧的状态变化与交接（ContinueAsync / CompleteRunSegment，来自执行器的线程）互斥：交接要么整个落在一帧之前，要么整个落在它之后
    private readonly Lock _turnGate = new();

    // 先发出去的「只进会话、不开一轮」用户帧还有几条没回 result（CLI 对每条回一条空 result）
    private int _contextFramesPending;

    // 续接时上一个进程停掉的后台任务：CLI 在这次运行的第一轮之前为每个发一条 stopped 通知、再回一条空 result，这些 result 还有几条没到。
    // 只认这次运行还没有任何 assistant 帧时到的、不是这次运行起的任务的通知（一轮里前台工具的通知不算）
    private int _staleTaskResultsPending;
    private bool _sawAssistant;

    // 最后一次上下文压缩、最后一轮的本地命令（compact 之类）
    private CliCompaction? _compaction;
    private string? _localCommand;

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
            "--permission-mode", PermissionModeValue(context.PermissionMode)
        };

        if (HandsPromptsToHost(context))
        {
            // 要人回答的事（权限提示、AskUserQuestion、ExitPlanMode）以 can_use_tool 交给框架。不带它时 -p 模式下没有人回答：
            // 权限提示当场被拒，提问与计划工具根本不在 agent 手里（见 CliProviderDescriptor.HostPromptArgs）
            args.AddRange(context.Provider.HostPromptArgs!);
        }

        if (!context.RelaysInteractions)
        {
            // 没有人值守：提问工具只会拿到空回答，agent 会"自行推断"继续，而用户永远看不到那个问题
            args.Add("--disallowedTools");
            args.Add(AskUserQuestionTool);
        }

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
        CliPermissionMode.Plan => "plan",
        _ => "default"
    };

    /// <summary>这一轮要不要让 CLI 把要人回答的事交给框架：有东西要交给使用方（权限请求或交互请求），且 provider 有这个开关。</summary>
    internal static bool HandsPromptsToHost(CliAgentLaunchContext context)
        => context.Provider.HostPromptArgs is { Count: > 0 }
           && (context.RelaysInteractions || (context.AsksForApproval && AsksInMode(context.PermissionMode)));

    /// <summary>哪些档位的权限请求会交给使用方：「先问我」，以及计划获批之后接着做的「计划」档（改动前逐个问）。</summary>
    private static bool AsksInMode(CliPermissionMode mode) => mode is CliPermissionMode.Ask or CliPermissionMode.Plan;

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
        _asksForApproval = context.AsksForApproval && AsksInMode(context.PermissionMode);
        _relaysInteractions = context.RelaysInteractions;
        _inPlanMode = context.PermissionMode == CliPermissionMode.Plan;
        _transport = transport;
        _resumeRejectionDetectable = context.Provider.ResumeRejectionDetectable;
        _backgroundTaskWait = context.BackgroundTaskWait > TimeSpan.Zero ? context.BackgroundTaskWait : TimeSpan.Zero;
        _sessionEffort = string.IsNullOrWhiteSpace(context.ThinkingLevel) ? null : context.ThinkingLevel;

        // 提示词经 stdin 投递。这里内联 await 是安全的：transport 在构造时就已启动
        // 独立的 stdout 抽水任务，管道不会写满（见 ProcessTransport 的注释）。
        foreach (var inputFrame in BuildInputFrames(context))
        {
            await transport.WriteLineAsync(inputFrame, cancellationToken);
        }

        lock (_turnGate)
        {
            _promptWritten = true;
        }

        // 等后台任务开下一轮有上限：读取用一个可以单独到点的令牌，到点时（且只在等下一轮时）安静地结束读取
        using var waitTimer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _waitTimer = waitTimer;
        var lines = transport.ReadLinesAsync(waitTimer.Token).GetAsyncEnumerator(waitTimer.Token);
        try
        {
            while (true)
            {
                bool hasLine;
                try
                {
                    hasLine = await lines.MoveNextAsync();
                }
                catch (OperationCanceledException) when ((_awaitingFollowUp || _awaitingInput) && !cancellationToken.IsCancellationRequested)
                {
                    // 等后台任务（或等 CLI 为写进去的消息开下一轮）等到了上限：已经有的回答照常交付，进程由执行器终止（后台任务随之结束）。
                    // 没被收下的消息由线程的消息队列在运行结束时作为新的一轮发出
                    _backgroundWaitExpired = true;
                    hasLine = false;
                }

                if (!hasLine)
                {
                    break;
                }

                if (ParseFrame(lines.Current) is not { } frame)
                {
                    continue;
                }

                List<CliAgentEvent> events;
                lock (_turnGate)
                {
                    events = HandleFrame(frame, transport, cancellationToken).ToList();
                }

                foreach (var evt in events)
                {
                    yield return evt;
                }

                if (_finished)
                {
                    // 最后一轮的终止信号已到。不等 stdout EOF —— 因为 stdin 还开着，它永远不会来。
                    break;
                }
            }
        }
        finally
        {
            _waitTimer = null;
            await lines.DisposeAsync();
        }

        if (_backgroundWaitExpired && _backgroundTasks.Count > 0)
        {
            yield return new CliAgentEvent
            {
                Type = CliAgentEventType.Status,
                Status = "background_tasks_stopped",
                Content = DescribeBackgroundTasks(),
                SessionId = _sessionId
            };
        }

        // 拿到终态后关 stdin，让 CLI 自行收尾退出。
        await transport.CloseInputAsync();
    }

    /// <summary>一行 stdout → 一帧；空行、非 JSON 行（启动横幅、告警）、没有 type 的帧为 null（不是协议错误，丢弃即可）。</summary>
    private static StreamJsonFrame? ParseFrame(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StreamJsonFrame>(line, FrameOptions) is { Type: not null } frame ? frame : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 这一轮写给 CLI 的用户帧。通常是一帧：附件在前、提示（尾部带上这一轮的上下文）在后。
    /// </summary>
    /// <remarks>
    /// 用户这一轮是一条斜杠命令、而有东西要随它带上（agent 说明、这一轮的上下文、附件）时，provider 接受「只进会话、不开一轮」的用户帧
    /// （<see cref="CliProviderDescriptor.AcceptsContextFrames"/>）就发两帧：先是只含那些东西的一帧（<c>shouldQuery: false</c>），
    /// 再是命令本身。追加在命令后面会成为命令的参数；同一帧的另一个内容块会让 CLI 认不出命令（见那个标志的说明）。
    /// provider 不接受时照常一帧：上下文追加在尾部，命令会把它当参数收下（能做的只有这样，文档里写明了）。
    /// </remarks>
    private List<string> BuildInputFrames(CliAgentLaunchContext context)
    {
        if (context.Provider.AcceptsContextFrames && IsSlashCommand(context.Prompt))
        {
            var preamble = string.Join("\n\n---\n\n",
                new[] { context.InlineSystemPrompt, context.TurnContext }.Where(part => !string.IsNullOrWhiteSpace(part)));
            if (preamble.Length > 0 || context.Attachments.Count > 0)
            {
                _contextFramesPending = 1;
                return
                [
                    BuildUserFrame(preamble, context.Attachments, contextOnly: true),
                    BuildUserFrame(context.Prompt, [])
                ];
            }
        }

        return [BuildUserFrame(BuildPrompt(context), context.Attachments)];
    }

    /// <summary>这条提示是不是一条斜杠命令（<c>/名字</c>，可带参数）：CLI 会把它展开成命令或技能。</summary>
    internal static bool IsSlashCommand(string prompt) => SlashCommand.IsMatch(prompt);

    private static readonly Regex SlashCommand = new(
        @"^\s*/[A-Za-z][\w:.\-]*(\s|$)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>还在跑的后台任务，给人看的一行（说明，没有说明时用 id）。</summary>
    private string? DescribeBackgroundTasks()
        => _backgroundTasks.Count == 0 ? null : string.Join("; ", _backgroundTasks.Select(t => t.Description ?? t.Id));

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
            UsageLimitResetsAt = failure == CliRunFailureReason.UsageLimit ? _usageLimitResetsAt : null,
            Context = SelectContext(),
            Compaction = _compaction,
            LocalCommand = _localCommand
        };
    }

    /// <summary>
    /// 上下文窗口的占用：主对话最后一次模型调用的输入（含缓存读写）加输出 —— 下一轮开始时上下文里已经有这么多。
    /// 窗口大小取 result 帧 <c>modelUsage</c> 里那个模型的 <c>contextWindow</c>。
    /// </summary>
    private CliContextUsage? SelectContext()
    {
        if (_lastMainUsage is not { } last)
        {
            // 压缩之后还没有过模型调用（一轮只做了 /compact）：占用就是压缩后的大小
            var model = _lastMainModel ?? (_contextWindows.Count == 1 ? _contextWindows.Keys.First() : null);
            if (_compaction?.PostTokens is not { } compacted || compacted <= 0 || model is null)
            {
                return null;
            }

            return new CliContextUsage(compacted, _contextWindows.TryGetValue(model, out var compactedWindow) ? compactedWindow : null, model);
        }

        var tokens = last.Usage.InputTokens + last.Usage.CacheReadInputTokens + last.Usage.CacheCreationInputTokens + last.Usage.OutputTokens;
        return tokens <= 0
            ? null
            : new CliContextUsage(tokens, _contextWindows.TryGetValue(last.Model, out var window) ? window : null, last.Model);
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
            return context.PromptWithTurnContext;
        }

        return $"{context.InlineSystemPrompt}\n\n---\n\n{context.PromptWithTurnContext}";
    }

    /// <summary>
    /// 一条 user 输入帧：附件在前（图片、PDF 文档块，base64 source），文本在后。
    /// </summary>
    /// <remarks>
    /// 附件排在文本前面是 Claude 对多模态输入的建议顺序。附件是文档时带上文件名作 title，模型据此在回答里称呼它。
    /// 没有文本（只发了附件）时不发空文本块：空文本块会被 API 拒收。
    /// </remarks>
    /// <param name="prompt">文本。</param>
    /// <param name="attachments">附件。</param>
    /// <param name="contextOnly">只写进会话、不开一轮（<c>shouldQuery: false</c>），见 <see cref="CliProviderDescriptor.AcceptsContextFrames"/>。</param>
    internal static string BuildUserFrame(string prompt, IReadOnlyList<CliPromptAttachment> attachments, bool contextOnly = false)
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

        if (!contextOnly)
        {
            return JsonSerializer.Serialize(payload);
        }

        var frame = JsonSerializer.SerializeToNode(payload)!.AsObject();
        frame["shouldQuery"] = false;
        return frame.ToJsonString();
    }

    private IEnumerable<CliAgentEvent> HandleFrame(
        StreamJsonFrame frame, ICliAgentTransport transport, CancellationToken cancellationToken)
    {
        // 会话里的对话条目（assistant 消息、回灌的工具结果）：最后一个就是以后截断续接时要保留到的位置。
        // 子 agent 的帧不算：它们记在子 agent 自己的记录里，不在主会话上，截断续接指向它会被 CLI 拒绝
        if (frame.Type is "assistant" or "user" && string.IsNullOrEmpty(frame.ParentToolUseId) && !string.IsNullOrWhiteSpace(frame.Uuid))
        {
            _lastEntryId = frame.Uuid;
        }

        // 等着的下一轮开始了：等待的上限不再适用（这一轮自己由看门狗管）
        if ((_awaitingFollowUp || _awaitingInput) && StartsTurn(frame))
        {
            _awaitingFollowUp = false;
            _awaitingInput = false;
            _waitTimer?.CancelAfter(Timeout.InfiniteTimeSpan);
        }

        switch (frame.Type)
        {
            case "assistant":
                _sawAssistant = true;
                return HandleAssistant(frame);

            case "system":
                if (!string.IsNullOrWhiteSpace(frame.SessionId))
                {
                    _sessionId = frame.SessionId;
                }

                return HandleSystem(frame);

            case "result":
                return HandleResult(frame);

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

            case "control_response":
                return HandleControlResponse(frame);

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

            case "command_lifecycle":
                return HandleCommandLifecycle(frame);

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

    /// <summary>
    /// 为上一个进程停掉的任务回的那条 result：由任务通知开出、一次模型调用都没有、不是错误。
    /// 它在这一轮之前到达；当成终止信号会在 CLI 回答这条消息的中途结束读取，执行器随即终止进程（退出码 143），回答丢失。
    /// </summary>
    private static bool IsStaleTaskResult(StreamJsonFrame frame)
        => !frame.IsError && frame.NumTurns == 0 && IsTaskNotificationTurn(frame);

    /// <summary>这条 result 结束的是不是任务通知开出的一轮（<c>origin.kind = task-notification</c>）。</summary>
    private static bool IsTaskNotificationTurn(StreamJsonFrame frame)
        => frame.Origin is { ValueKind: JsonValueKind.Object } origin
           && origin.TryGetProperty("kind", out var kind)
           && kind.ValueKind == JsonValueKind.String
           && kind.ValueEquals("task-notification");

    /// <inheritdoc />
    public bool IsAwaitingFollowUp => _awaitingFollowUp && !_finished;

    /// <inheritdoc />
    public CliAgentResult CompleteRunSegment(CliSessionOutcome outcome)
    {
        Check.NotNull(outcome);

        lock (_turnGate)
        {
            var result = GetResult(outcome);
            ResetRunState();
            return result;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 只在等后台任务时写（没有一轮在进行）：一轮进行中写进去的用户消息，CLI 可能把它并进那一轮，分不清哪段回答是谁的。
    /// 写之前就离开等待并解除等待的上限：这一轮由看门狗管。任务通知开出的一轮恰好同时开始时，它的 result 带
    /// <c>origin.kind = task-notification</c>，不当作这一轮的回答（<see cref="HandleResult"/>），接着读到这一轮的为止。
    /// </remarks>
    public async Task<bool> ContinueAsync(CliAgentLaunchContext context, CancellationToken cancellationToken)
    {
        Check.NotNull(context);

        List<string> frames;
        ICliAgentTransport transport;
        lock (_turnGate)
        {
            if (_finished || !_awaitingFollowUp || _transport is null || _waitTimer is not { IsCancellationRequested: false } waitTimer)
            {
                return false;
            }

            _awaitingFollowUp = false;
            _submittedTurnPending = true;
            // 等待的上限按接手的这条运行的配置（运行期可改）
            _backgroundTaskWait = context.BackgroundTaskWait > TimeSpan.Zero ? context.BackgroundTaskWait : TimeSpan.Zero;
            waitTimer.CancelAfter(Timeout.InfiniteTimeSpan);
            transport = _transport;
            frames = BuildInputFrames(context);

            // 接手的这一轮要的强度与会话现在的不同（两边都给了，执行器才会把进程交过来）：先换档，再写用户帧
            if (context.Provider.ChangesEffortInSession
                && !string.IsNullOrWhiteSpace(context.ThinkingLevel) && _sessionEffort is not null
                && !string.Equals(context.ThinkingLevel, _sessionEffort, StringComparison.Ordinal))
            {
                var requestId = $"{EffortRequestPrefix}{Guid.NewGuid():N}";
                _effortRequests[requestId] = context.ThinkingLevel;
                frames.Insert(0, JsonSerializer.Serialize(new
                {
                    type = "control_request",
                    request_id = requestId,
                    request = new { subtype = "apply_flag_settings", settings = new { effortLevel = context.ThinkingLevel } }
                }));
                _sessionEffort = context.ThinkingLevel;
            }
        }

        foreach (var frame in frames)
        {
            await transport.WriteLineAsync(frame, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// 交接之后从零算的那些：用量、记录、这一轮的回答与结局。会话、后台任务、上下文占用与计划档是这个进程的，留着。
    /// </summary>
    private void ResetRunState()
    {
        _messageUsage.Clear();
        _unidentifiedMessages = 0;
        _reportedTotals = null;
        _turnUsage = null;
        _transcript.Clear();
        _finalResultText = null;
        _lastAssistantText = null;
        _sawResult = false;
        _resultIsError = false;
        _numTurns = 0;
        _totalCostUsd = 0m;
        _requestedResumeSessionId = null;
        _compaction = null;
        _localCommand = null;
        _usageLimitReached = false;
        _usageLimitNotice = null;
        _usageLimitResetsAt = null;
        _rejectedRateLimit = null;
        _rateLimited = false;
        _backgroundWaitExpired = false;
    }

    /// <summary>一帧是不是一轮的开始或其中的内容（而不是两轮之间的后台任务播报）。</summary>
    private static bool StartsTurn(StreamJsonFrame frame)
        => frame.Type is "assistant" or "user" or "control_request"
           || (frame.Type == "system" && frame.Subtype == "init");

    private IEnumerable<CliAgentEvent> HandleSystem(StreamJsonFrame frame)
    {
        if (frame.Subtype is "init" or "status" && !string.IsNullOrWhiteSpace(frame.PermissionMode))
        {
            TrackPlanMode(frame.PermissionMode);
        }

        if (TaskEvent(frame) is { } taskEvent)
        {
            // 任务（子 agent、后台命令）的开始 / 进展 / 结局：一条任务事件。后台任务的完成照常再出下面那条「后台任务完成」
            return frame.Subtype == "task_notification" && frame.TaskId is { } finishedId && _backgroundTaskIds.Contains(finishedId)
                ? [taskEvent, BackgroundTaskFinished(frame)]
                : [taskEvent];
        }

        if (frame.Subtype == "task_updated" && frame.TaskId is { } updatedId && _tasks.ContainsKey(updatedId))
        {
            // 任务的状态补丁：结局随后由 task_notification 带来，这条不出
            return [];
        }

        switch (frame.Subtype)
        {
            case "background_tasks_changed":
                // 整份替换：这就是此刻还在跑的全部后台任务。不落成事件（任务开始已有那次工具调用，结束有下面的通知）
                _backgroundTasks = ReadBackgroundTasks(frame.Tasks);
                foreach (var task in _backgroundTasks)
                {
                    _backgroundTaskIds.Add(task.Id);
                }

                return [];

            case "task_notification" when frame.TaskId is { } taskId && _backgroundTaskIds.Contains(taskId):
                // 一个后台任务结束了：它通常是 CLI 接着开一轮的原因，给界面一行「后台命令完成」（前台工具的同类通知不出：它们已有工具结果）
                return [BackgroundTaskFinished(frame)];

            case "task_notification" when frame.TaskId is not null && !_sawAssistant:
                // 上一个进程留下的任务（续接一个后台任务还在跑时被取消的会话）：CLI 接着为它回一条空 result，那不是这次运行的一轮
                _staleTaskResultsPending++;
                goto default;

            case "compact_boundary":
            {
                _compaction = ReadCompaction(frame.CompactMetadata);

                // 压缩之前那次调用的占用不再是上下文的大小
                if (_lastMainUsage is { } last)
                {
                    _lastMainModel = last.Model;
                }

                _lastMainUsage = null;
                return [new CliAgentEvent
                {
                    Type = CliAgentEventType.Compacted,
                    Compaction = _compaction,
                    SessionId = _sessionId
                }];
            }

            default:
                // 会话 ID 尽早外发：进程中途崩溃时，等终态才拿它就已经晚了。
                return [new CliAgentEvent
                {
                    Type = CliAgentEventType.Status,
                    Status = "running",
                    SessionId = _sessionId
                }];
        }
    }

    private CliAgentEvent BackgroundTaskFinished(StreamJsonFrame frame) => new()
    {
        Type = CliAgentEventType.Status,
        Status = "background_task_finished",
        Content = StripAnsi(frame.Summary),
        SessionId = _sessionId
    };

    /// <summary>
    /// 任务的一帧（<c>task_started</c> / <c>task_progress</c> / <c>task_notification</c>）→ 一条任务事件；认不出的任务为 null。
    /// </summary>
    /// <remarks>
    /// 认任务看 <c>task_started</c> 带不带 <c>tool_use_id</c>（起它的那次工具调用；子 agent 是 <c>local_agent</c>，后台命令是 <c>local_bash</c>），之后按任务 id 认。
    /// 嵌套派出的子 agent 的 assistant / user 帧 CLI 不发（实测 2.1.319），它做到哪只有这几帧说得出。
    /// 上一个进程留下的任务（续接一个后台任务还在跑时被取消的会话）不出：起它的调用不在这次运行里，照旧按任务通知处理。
    /// </remarks>
    private CliAgentEvent? TaskEvent(StreamJsonFrame frame)
    {
        if (frame.TaskId is not { } taskId)
        {
            return null;
        }

        switch (frame.Subtype)
        {
            case "task_started":
            {
                if (string.IsNullOrEmpty(frame.ToolUseId))
                {
                    return null;
                }

                _tasks[taskId] = frame.ToolUseId;
                return new CliAgentEvent
                {
                    Type = CliAgentEventType.Task,
                    CallId = frame.ToolUseId,
                    ParentCallId = frame.ParentTaskId is { } parentTask && _tasks.TryGetValue(parentTask, out var parentCall) ? parentCall : null,
                    SessionId = _sessionId,
                    Task = new CliTask
                    {
                        TaskId = taskId,
                        Kind = string.IsNullOrEmpty(frame.TaskType) && !string.IsNullOrEmpty(frame.SubagentType)
                            ? CliTaskKinds.Agent
                            : CliTaskKinds.FromClaude(frame.TaskType),
                        AgentType = frame.SubagentType,
                        Description = StripAnsi(ReadString(frame.Description)),
                        Prompt = ReadString(frame.Prompt),
                        Background = frame.IsBackgrounded is { ValueKind: JsonValueKind.True or JsonValueKind.False } background
                            ? background.GetBoolean()
                            : null,
                        Status = CliTaskStatuses.Running
                    }
                };
            }

            case "task_progress" when _tasks.TryGetValue(taskId, out var callId):
                return new CliAgentEvent
                {
                    Type = CliAgentEventType.Task,
                    CallId = callId,
                    SessionId = _sessionId,
                    Task = new CliTask
                    {
                        TaskId = taskId,
                        AgentType = frame.SubagentType,
                        Activity = StripAnsi(ReadString(frame.Description)),
                        LastTool = frame.LastToolName,
                        ToolUseCount = frame.Usage?.ToolUses,
                        TotalTokens = frame.Usage?.TotalTokens,
                        DurationMs = frame.Usage?.DurationMs,
                        Status = CliTaskStatuses.Running
                    }
                };

            case "task_notification" when _tasks.TryGetValue(taskId, out var callId):
                return new CliAgentEvent
                {
                    Type = CliAgentEventType.Task,
                    CallId = callId,
                    SessionId = _sessionId,
                    Task = new CliTask
                    {
                        TaskId = taskId,
                        Summary = StripAnsi(frame.Summary),
                        ToolUseCount = frame.Usage?.ToolUses,
                        TotalTokens = frame.Usage?.TotalTokens,
                        DurationMs = frame.Usage?.DurationMs,
                        Status = ReadString(frame.Status) switch
                        {
                            "completed" => CliTaskStatuses.Completed,
                            "failed" => CliTaskStatuses.Failed,
                            // stopped（TaskStop、用户停下）与 killed 都是被停掉的
                            _ => CliTaskStatuses.Stopped
                        }
                    }
                };

            default:
                return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 写一帧 <c>control_request</c>（<c>subtype: stop_task</c>）。CLI 的应答（<c>control_response</c>）只在出错时留一条警告日志事件：
    /// 任务已经结束、id 不认识都是这种情况，停下本来就是想让它结束。
    /// </remarks>
    public async Task<bool> StopTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        Check.NotNullOrWhiteSpace(taskId);

        ICliAgentTransport transport;
        string requestId;
        lock (_turnGate)
        {
            if (_finished || _transport is null)
            {
                return false;
            }

            transport = _transport;
            requestId = $"{StopTaskRequestPrefix}{Guid.NewGuid():N}";
            _stopRequests[requestId] = taskId;
        }

        await transport.WriteLineAsync(JsonSerializer.Serialize(new
        {
            type = "control_request",
            request_id = requestId,
            request = new { subtype = "stop_task", task_id = taskId }
        }), cancellationToken);
        return true;
    }

    /// <summary>CLI 对我们发出的控制请求的应答：停下任务、换强度失败时留一条警告日志事件，其余不出事件。</summary>
    /// <remarks>换强度的应答只说收到：2.1.308 对认不出的值也答 success（不生效），值的合法性靠入队时按模型的档位校验。</remarks>
    private IEnumerable<CliAgentEvent> HandleControlResponse(StreamJsonFrame frame)
    {
        if (frame.Response is not { ValueKind: JsonValueKind.Object } response
            || ReadText(response, "request_id") is not { } requestId)
        {
            return [];
        }

        if (_effortRequests.TryRemove(requestId, out var effort))
        {
            return ReadText(response, "subtype") == "error"
                ? [new CliAgentEvent
                {
                    Type = CliAgentEventType.Log,
                    Level = "warning",
                    Content = $"The CLI could not switch the reasoning effort to {effort}: {ReadText(response, "error") ?? "no reason given"}"
                }]
                : [];
        }

        if (!_stopRequests.TryRemove(requestId, out var taskId))
        {
            return [];
        }

        return ReadText(response, "subtype") == "error"
            ? [new CliAgentEvent
            {
                Type = CliAgentEventType.Log,
                Level = "warning",
                Content = $"The CLI could not stop task {taskId}: {ReadText(response, "error") ?? "no reason given"}"
            }]
            : [];
    }

    /// <summary>
    /// 会话换了档位：进入计划档时从头记主对话的文字（之前的计划都已有了结局），离开时停止记录。
    /// </summary>
    private void TrackPlanMode(string mode)
    {
        var inPlan = string.Equals(mode, "plan", StringComparison.Ordinal);
        if (inPlan && !_inPlanMode)
        {
            _planText.Clear();
            _planThinking.Clear();
            _sentBackEmptyPlan = false;
        }

        _inPlanMode = inPlan;
    }

    /// <summary>
    /// 一条 result：一轮的结束。CLI 还有后台任务在跑（或还有排队的轮次）时它会接着开下一轮：产出
    /// <see cref="CliAgentEventType.TurnCompleted"/> 并接着读；否则这就是终止信号。
    /// </summary>
    /// <remarks>
    /// 先发出去的「只进会话」用户帧各回一条空 result，在命令那一轮之前到达：吞掉，它们不是一轮。
    /// 出错的一轮不等：错误就是这次运行的结局。
    /// </remarks>
    private IEnumerable<CliAgentEvent> HandleResult(StreamJsonFrame frame)
    {
        if (_contextFramesPending > 0)
        {
            _contextFramesPending--;
            return [];
        }

        if (_staleTaskResultsPending > 0 && IsStaleTaskResult(frame))
        {
            _staleTaskResultsPending--;
            return [];
        }

        _sawResult = true;
        _finalResultText = frame.ResultText;
        _resultIsError = frame.IsError;
        _numTurns = frame.NumTurns;
        _totalCostUsd = frame.TotalCostUsd;
        _localCommand = ReadString(frame.LocalCommand);
        if (!string.IsNullOrWhiteSpace(frame.SessionId))
        {
            _sessionId = frame.SessionId;
        }

        RecordResultUsage(frame);
        var events = TakeAnsweredInputs(frame);

        // 交接写进去的那一轮还没回答：任务通知开出的一轮先到时（交接与任务完成撞在一起），它之后还有那一轮
        if (_submittedTurnPending && !IsTaskNotificationTurn(frame))
        {
            _submittedTurnPending = false;
        }

        // 写进去的消息还没被收下就到了 result（它恰好在最后一段回答时到达）：CLI 为它另开一轮（2.1.322 实测，这条 result 的
        // queued_turn_count 仍是 0），读取接着走
        var inputPending = _inputs.Values.Any(i => !i.Started);
        var backgroundTurns = (_backgroundTasks.Count > 0 || frame.QueuedTurnCount > 0 || _submittedTurnPending)
                              && _backgroundTaskWait > TimeSpan.Zero;
        if (frame.IsError || (!backgroundTurns && !inputPending))
        {
            _finished = true;
            return events;
        }

        if (inputPending)
        {
            // 下一轮是这条消息的：不是两轮之间等后台任务，进程不能交给同线程的下一条运行
            _awaitingInput = true;
            _waitTimer?.CancelAfter(backgroundTurns && _backgroundTaskWait > InputPickupWait ? _backgroundTaskWait : InputPickupWait);
        }
        else
        {
            _awaitingFollowUp = true;
            _waitTimer?.CancelAfter(_backgroundTaskWait);
        }

        // 下一轮的回答从头算：上一轮的兜底文本不能被当成下一轮的
        _lastAssistantText = null;
        events.Add(new CliAgentEvent
        {
            Type = CliAgentEventType.TurnCompleted,
            Content = frame.ResultText,
            SessionId = _sessionId,
            Turn = new CliTurnBoundary { BackgroundTasks = _backgroundTasks.ToList() }
        });
        return events;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 写一条带 <c>uuid</c>（消息 id）与 <c>priority: "next"</c> 的用户帧：CLI 先报 <c>command_lifecycle queued</c>，在下一个工具结果之后
    /// 把它并进这一轮（<c>started</c>），这一轮的 result 在 <c>user_message_uuids</c> 里列出它（2.1.322 实测）。不带 priority 时
    /// 它排在哪由 CLI 的开关决定，可能等这一轮结束，所以显式给出。
    /// </remarks>
    public async Task<bool> SendUserMessageAsync(CliTurnInputMessage message, CancellationToken cancellationToken)
    {
        Check.NotNull(message);

        var uuid = message.Id.ToString();
        string frame;
        ICliAgentTransport transport;
        lock (_turnGate)
        {
            if (_finished || _awaitingFollowUp || !_promptWritten || _transport is null || _inputs.ContainsKey(uuid))
            {
                return false;
            }

            transport = _transport;
            _inputs[uuid] = new PendingInput(message.Id, message.Text);
            frame = BuildInputMessageFrame(uuid, message.Text, message.Attachments);
        }

        try
        {
            await transport.WriteLineAsync(frame, cancellationToken);
        }
        catch
        {
            lock (_turnGate)
            {
                _inputs.Remove(uuid);
            }

            throw;
        }

        return true;
    }

    /// <summary>一条一轮进行中交给 CLI 的用户帧：与提示同样的内容块，加上 uuid 与 <c>priority: "next"</c>。</summary>
    internal static string BuildInputMessageFrame(string uuid, string text, IReadOnlyList<CliPromptAttachment> attachments)
    {
        var frame = JsonNode.Parse(BuildUserFrame(text, attachments))!.AsObject();
        frame["uuid"] = uuid;
        frame["priority"] = "next";
        return frame.ToJsonString();
    }

    /// <summary>
    /// 写进去的消息有了着落：<c>started</c> = agent 在两步之间收下了它；<c>cancelled</c> / <c>discarded</c> / <c>refused</c> 在收下之前 = CLI 不收。
    /// 别的 uuid（不是我们写的）与收下之后的状态不出事件。
    /// </summary>
    private IEnumerable<CliAgentEvent> HandleCommandLifecycle(StreamJsonFrame frame)
    {
        if (frame.CommandUuid is not { } uuid || !_inputs.TryGetValue(uuid, out var input) || input.Started)
        {
            return [];
        }

        switch (frame.LifecycleState)
        {
            case "started":
                input.Started = true;
                return [Taken(input)];

            case "cancelled" or "discarded" or "refused":
                _inputs.Remove(uuid);
                return [new CliAgentEvent
                {
                    Type = CliAgentEventType.UserMessage,
                    Content = input.Text,
                    SessionId = _sessionId,
                    UserMessage = new CliUserMessage
                    {
                        MessageId = input.Id,
                        Taken = false,
                        Text = input.Text,
                        Reason = frame.LifecycleState == "refused"
                            ? "The agent's session declined this message."
                            : "The agent's session ended before it took this message in."
                    }
                }];

            default:
                return [];
        }
    }

    /// <summary>
    /// result 列出的、这一轮回答了的消息：没有 <c>command_lifecycle</c> 帧的 CLI 只从这里看得出它们被收下（位置只能算在这一轮的末尾）。
    /// 收下过的从此不再记着。
    /// </summary>
    private List<CliAgentEvent> TakeAnsweredInputs(StreamJsonFrame frame)
    {
        var events = new List<CliAgentEvent>();
        if (_inputs.Count == 0)
        {
            return events;
        }

        if (frame.UserMessageUuids is { ValueKind: JsonValueKind.Array } answered)
        {
            foreach (var item in answered.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } uuid
                    && _inputs.TryGetValue(uuid, out var input) && !input.Started)
                {
                    input.Started = true;
                    events.Add(Taken(input));
                }
            }
        }

        foreach (var taken in _inputs.Where(p => p.Value.Started).Select(p => p.Key).ToList())
        {
            _inputs.Remove(taken);
        }

        return events;
    }

    private CliAgentEvent Taken(PendingInput input) => new()
    {
        Type = CliAgentEventType.UserMessage,
        Content = input.Text,
        SessionId = _sessionId,
        UserMessage = new CliUserMessage { MessageId = input.Id, Taken = true, Text = input.Text }
    };

    /// <summary>一条写进去、还在等着落的消息。</summary>
    private sealed class PendingInput(Guid id, string text)
    {
        public Guid Id { get; } = id;

        public string Text { get; } = text;

        /// <summary>agent 已经收下了它。</summary>
        public bool Started { get; set; }
    }

    /// <summary><c>background_tasks_changed</c> 的 <c>tasks</c>：没有 id 的项跳过。</summary>
    internal static List<CliBackgroundTask> ReadBackgroundTasks(JsonElement? element)
    {
        var tasks = new List<CliBackgroundTask>();
        if (element is not { ValueKind: JsonValueKind.Array } items)
        {
            return tasks;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && ReadText(item, "task_id") is { } id)
            {
                tasks.Add(new CliBackgroundTask(id, StripAnsi(ReadText(item, "description")), ReadText(item, "task_type")));
            }
        }

        return tasks;
    }

    /// <summary><c>compact_metadata</c>：触发方式、压缩前后的 token 数（缺的为 null）。</summary>
    internal static CliCompaction ReadCompaction(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } metadata)
        {
            return new CliCompaction(null, null, null);
        }

        return new CliCompaction(ReadText(metadata, "trigger"), ReadLong(metadata, "pre_tokens"), ReadLong(metadata, "post_tokens"));
    }

    private static long? ReadLong(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

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

            // 子 agent（Task 工具里）的消息有自己的上下文，不算主对话的占用
            if (string.IsNullOrEmpty(frame.ParentToolUseId))
            {
                _lastMainUsage = (message.Model, message.Usage);
                _lastMainModel = message.Model;
            }
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

        // 子 agent（Agent 工具里）的消息：它写的字不是给用户的回答，不进交付物、计划与错误嗅探的记录；事件带上派出它的调用，由上层归到那张卡片下
        var subAgentCall = string.IsNullOrEmpty(frame.ParentToolUseId) ? null : frame.ParentToolUseId;

        // 子 agent 用的模型：它的第一条回复帧上说一次（任务帧不带模型）
        if (subAgentCall is not null && !string.IsNullOrWhiteSpace(message.Model) && message.Model != SyntheticModel
            && _subAgentModels.Add(subAgentCall))
        {
            events.Add(new CliAgentEvent
            {
                Type = CliAgentEventType.Task,
                CallId = subAgentCall,
                SessionId = _sessionId,
                Task = new CliTask { Kind = CliTaskKinds.Agent, Model = message.Model }
            });
        }

        foreach (var block in message.Content)
        {
            switch (block.Type)
            {
                case "text" when !string.IsNullOrEmpty(block.Text):
                    if (subAgentCall is null)
                    {
                        assistantText.Append(block.Text);
                        _transcript.Append(block.Text);
                        if (_inPlanMode && !string.IsNullOrWhiteSpace(block.Text))
                        {
                            _planText.Append(_planText.Length > 0 ? "\n\n" : string.Empty).Append(block.Text.Trim());
                        }
                    }

                    events.Add(new CliAgentEvent { Type = CliAgentEventType.Text, Content = block.Text, ParentCallId = subAgentCall });
                    break;

                case "thinking":
                    var thinking = string.IsNullOrEmpty(block.Thinking) ? block.Text : block.Thinking;
                    if (!string.IsNullOrEmpty(thinking))
                    {
                        if (_inPlanMode && subAgentCall is null && !string.IsNullOrWhiteSpace(thinking))
                        {
                            _planThinking.Append(_planThinking.Length > 0 ? "\n\n" : string.Empty).Append(thinking.Trim());
                        }

                        events.Add(new CliAgentEvent { Type = CliAgentEventType.Thinking, Content = thinking, ParentCallId = subAgentCall });
                    }

                    break;

                case "tool_use":
                    sawToolUse = true;
                    if (subAgentCall is null && block.Name == ExitPlanModeTool && !string.IsNullOrEmpty(block.Id)
                        && block.Input is { ValueKind: JsonValueKind.Object } planCall && ReadText(planCall, "plan") is { } submitted)
                    {
                        _submittedPlans[block.Id] = submitted;
                    }

                    events.Add(new CliAgentEvent
                    {
                        Type = CliAgentEventType.ToolUse,
                        Tool = block.Name,
                        CallId = block.Id,
                        Input = ToDictionary(block.Input),
                        ParentCallId = subAgentCall
                    });
                    break;
            }
        }

        // 调了工具的这一轮是中间态，哪怕它也带了叙述文本。把它当成"空结果时的兜底答案"
        // 会把「我先看一下日志」当成最终交付物回给用户。子 agent 的消息不碰它：它的报告不是主 agent 的回答
        if (subAgentCall is null)
        {
            _lastAssistantText = sawToolUse ? null : assistantText.ToString();
        }

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
                Status = block.IsError ? "failed" : "completed",
                ParentCallId = string.IsNullOrEmpty(frame.ParentToolUseId) ? null : frame.ParentToolUseId
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
        var interaction = isPermissionRequest ? InteractionKind(request!.ToolName) : null;

        // 问用户问题、交出计划：有人值守就交给使用方（与档位无关），没有人就当场拒绝 —— 绝不交给 agent 一份空回答（全权档也不自动放行）
        if (interaction is not null)
        {
            if (_relaysInteractions)
            {
                if (interaction == CliInteractionKinds.Plan && !_sentBackEmptyPlan && !HasPlanToShow(request!))
                {
                    // 人要据此决定批不批：一个空计划交给使用方等于让人批一张白纸。退回一次，让 agent 把计划写成文字
                    _sentBackEmptyPlan = true;
                    Respond(transport, frame.RequestId, DenyResponse(frame.RequestId, MissingPlanMessage), cancellationToken);
                    return [];
                }

                return [AskForInteraction(frame.RequestId, request!, interaction)];
            }

            Respond(transport, frame.RequestId, DenyResponse(frame.RequestId,
                interaction == CliInteractionKinds.Plan ? UnattendedPlanMessage : UnattendedQuestionMessage), cancellationToken);
            return [];
        }

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

        Respond(transport, frame.RequestId, response, cancellationToken);
        return [];
    }

    /// <summary>
    /// 当场应答一个 control_request。应答走同一条 stdin。不应答的后果是子进程阻塞到它的内部超时，整个任务卡死。
    /// </summary>
    /// <remarks>
    /// 帧翻译是同步的，等不了这次写入；写失败（stdin 已关、进程已退）至少要留下痕迹，
    /// 否则随后的看门狗超时看不出是这条应答没送到。
    /// </remarks>
    private void Respond(ICliAgentTransport transport, string requestId, object response, CancellationToken cancellationToken)
        => transport.WriteLineAsync(JsonSerializer.Serialize(response), cancellationToken).ContinueWith(
            task => _logger.LogWarning(task.Exception,
                "Could not answer control request {RequestId}; the CLI may wait for its own timeout", requestId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>这个计划请求有没有可以给人看的计划：入参里的 <c>plan</c>，或进入计划档以来主对话里写下的文字或思考。</summary>
    private bool HasPlanToShow(StreamJsonControlRequest request)
        => _planText.Length > 0 || _planThinking.Length > 0 || SubmittedPlan(request) is not null;

    /// <summary>agent 交出的 <c>plan</c>：批准请求的入参里的，没有时取那次工具调用（同一个 <c>tool_use_id</c>）的参数里的。</summary>
    private string? SubmittedPlan(StreamJsonControlRequest request)
        => (request.Input is { ValueKind: JsonValueKind.Object } input ? ReadText(input, "plan") : null)
           ?? (request.ToolUseId is { } callId ? _submittedPlans.GetValueOrDefault(callId) : null);

    /// <summary>这个工具调用是不是要人回答的交互（问题 / 计划）；不是时为 null。</summary>
    private static string? InteractionKind(string? toolName) => toolName switch
    {
        AskUserQuestionTool => CliInteractionKinds.Question,
        ExitPlanModeTool => CliInteractionKinds.Plan,
        _ => null
    };

    /// <summary>
    /// 记下一个等使用方回答的问题或计划，产出请求事件（<see cref="CliAgentPermission.Kind"/> 区分）。
    /// </summary>
    /// <remarks>
    /// <para>问题的选项是「交出回答」与「不答」；回答随 <see cref="CliPermissionDecision.Answers"/> 交回，写给 CLI 时并进原入参的 <c>answers</c>。</para>
    /// <para>
    /// 计划的选项是「批准」与「不批准」；以计划档启动的运行另有「批准并自动接受改动」（实测 <c>updatedPermissions</c> 里的
    /// <c>setMode acceptEdits</c> 生效，状态帧报 <c>permissionMode: acceptEdits</c>）。只批准时 claude 回到计划之前的档位：
    /// 以计划档启动的回到 <c>default</c>，改动前逐个问。
    /// </para>
    /// </remarks>
    private CliAgentEvent AskForInteraction(string requestId, StreamJsonControlRequest request, string kind)
    {
        var input = request.Input is { ValueKind: JsonValueKind.Object } value ? value.Clone() : (JsonElement?)null;
        _pendingApprovals[requestId] = new PendingApproval(input, null, kind);

        List<CliPermissionOption> options;
        IReadOnlyList<CliQuestion>? questions = null;
        string? plan = null;
        string? planSource = null;
        if (kind == CliInteractionKinds.Question)
        {
            questions = ReadQuestions(input);
            options =
            [
                new(CliInteractionOptionIds.Submit, "Submit answers", CliPermissionOptionKinds.AllowOnce),
                new(CliInteractionOptionIds.Decline, "Don't answer", CliPermissionOptionKinds.RejectOnce)
            ];
        }
        else
        {
            // 人要据此决定批不批：CLI 没交出计划（没有 Write 时入参是 {}）就用 agent 进入计划档以来写下的文字，并说明来源
            plan = SubmittedPlan(request);
            planSource = plan is not null ? CliPlanSources.Input : null;
            if (plan is null && _planText.Length > 0)
            {
                plan = _planText.ToString();
                planSource = CliPlanSources.AssistantText;
            }
            else if (plan is null && _planThinking.Length > 0)
            {
                plan = _planThinking.ToString();
                planSource = CliPlanSources.AssistantThinking;
            }

            options = _permissionMode == CliPermissionMode.Plan
                ?
                [
                    new(CliInteractionOptionIds.Approve, "Approve, ask before edits", CliPermissionOptionKinds.AllowOnce),
                    new(CliInteractionOptionIds.ApproveAcceptEdits, "Approve and auto-accept edits", CliPermissionOptionKinds.AllowOnce),
                    new(CliInteractionOptionIds.Reject, "Keep planning", CliPermissionOptionKinds.RejectOnce)
                ]
                :
                [
                    new(CliInteractionOptionIds.Approve, "Approve", CliPermissionOptionKinds.AllowOnce),
                    new(CliInteractionOptionIds.Reject, "Keep planning", CliPermissionOptionKinds.RejectOnce)
                ];
        }

        return new CliAgentEvent
        {
            Type = CliAgentEventType.PermissionRequest,
            Tool = request.ToolName,
            CallId = request.ToolUseId,
            // 问题与计划的内容在 Permission 上，入参不再原样重复一份（计划全文可能很长）
            SessionId = _sessionId,
            Permission = new CliAgentPermission
            {
                ProtocolKey = requestId,
                Kind = kind,
                Title = StripAnsi(request.Title ?? request.DisplayName),
                Questions = questions,
                Plan = plan,
                PlanSource = planSource,
                Options = options
            }
        };
    }

    /// <summary>AskUserQuestion 的 <c>questions</c>：认不出的项跳过（不崩），选项没有标签的丢掉。</summary>
    internal static List<CliQuestion> ReadQuestions(JsonElement? input)
    {
        var questions = new List<CliQuestion>();
        if (input is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty("questions", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return questions;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || ReadText(item, "question") is not { } question)
            {
                continue;
            }

            var options = new List<CliQuestionOption>();
            if (item.TryGetProperty("options", out var optionItems) && optionItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var option in optionItems.EnumerateArray())
                {
                    if (option.ValueKind == JsonValueKind.Object && ReadText(option, "label") is { } label)
                    {
                        options.Add(new CliQuestionOption(label, ReadText(option, "description")));
                    }
                }
            }

            var multiSelect = item.TryGetProperty("multiSelect", out var multi) && multi.ValueKind == JsonValueKind.True;
            questions.Add(new CliQuestion(question, ReadText(item, "header"), options, multiSelect));
        }

        return questions;
    }

    private static string? ReadText(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

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
            request.Input is { ValueKind: JsonValueKind.Object } input ? input.Clone() : null, suggestions, CliInteractionKinds.Permission);

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

        var response = pending.Kind switch
        {
            CliInteractionKinds.Question => QuestionResponse(protocolKey, pending, decision),
            CliInteractionKinds.Plan => PlanResponse(protocolKey, pending, decision),
            _ => PermissionResponse(protocolKey, pending, decision)
        };

        await _transport.WriteLineAsync(JsonSerializer.Serialize(response), cancellationToken);
        return true;
    }

    private static object PermissionResponse(string protocolKey, PendingApproval pending, CliPermissionDecision decision)
    {
        var keepAsking = decision.Allow
                         && string.Equals(decision.OptionId, CliPermissionOptionKinds.AllowAlways, StringComparison.Ordinal);
        return decision.Allow
            ? AllowResponse(protocolKey, pending.Input, keepAsking ? SessionScoped(pending.Suggestions) : null)
            : DenyResponse(protocolKey, MessageOr(decision, "The user did not allow this action."));
    }

    /// <summary>
    /// 问题的应答：允许时 <c>updatedInput</c> = 原入参加上 <c>answers</c>（问题原文 → 回答，多选用 ", " 连起来）。
    /// 实测 claude 据此给 agent 的工具结果是 <c>Your questions have been answered: "问题"="回答"</c>。
    /// </summary>
    private static object QuestionResponse(string protocolKey, PendingApproval pending, CliPermissionDecision decision)
    {
        if (!decision.Allow || decision.Answers is not { Count: > 0 } answers)
        {
            return DenyResponse(protocolKey, MessageOr(decision, DeclinedQuestionMessage));
        }

        var input = JsonNode.Parse((pending.Input is { ValueKind: JsonValueKind.Object } original ? original : EmptyToolInput).GetRawText())!.AsObject();
        var written = new JsonObject();
        foreach (var (question, values) in answers)
        {
            written[question] = string.Join(", ", values);
        }

        input["answers"] = written;
        return AllowResponse(protocolKey, input, updatedPermissions: null);
    }

    /// <summary>计划的应答：批准原样回传入参（选了「自动接受改动」时带上切到 acceptEdits 的规则），不批准时意见作为 deny 的说明。</summary>
    private static object PlanResponse(string protocolKey, PendingApproval pending, CliPermissionDecision decision)
    {
        if (!decision.Allow)
        {
            return DenyResponse(protocolKey, MessageOr(decision, RejectedPlanMessage));
        }

        var acceptEdits = string.Equals(decision.OptionId, CliInteractionOptionIds.ApproveAcceptEdits, StringComparison.Ordinal)
            ? new JsonArray(new JsonObject { ["type"] = "setMode", ["mode"] = "acceptEdits", ["destination"] = "session" })
            : null;
        return AllowResponse(protocolKey, pending.Input, acceptEdits);
    }

    private static string MessageOr(CliPermissionDecision decision, string fallback)
        => string.IsNullOrWhiteSpace(decision.Message) ? fallback : decision.Message;

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
        => AllowResponse(requestId, JsonNode.Parse((input is { ValueKind: JsonValueKind.Object } toolInput ? toolInput : EmptyToolInput).GetRawText())!, updatedPermissions);

    private static object AllowResponse(string requestId, JsonNode updatedInput, JsonNode? updatedPermissions)
    {
        var decision = new JsonObject
        {
            ["behavior"] = "allow",
            ["updatedInput"] = updatedInput
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

    /// <summary>一个等回答的 can_use_tool：应答允许时要原样回传入参，「本会话总是允许」要带上建议规则；种类决定怎么应答。</summary>
    private sealed record PendingApproval(JsonElement? Input, JsonElement? Suggestions, string Kind);

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
        // 一个进程里可以有几轮（后台任务完成后 CLI 自己开的那些）：这次运行的主循环用量是各轮之和
        if (frame.Usage is { } turn)
        {
            var usage = ToTokenUsage(turn);
            _turnUsage = _turnUsage is not { } earlier
                ? usage
                : earlier with
                {
                    InputTokens = earlier.InputTokens + usage.InputTokens,
                    OutputTokens = earlier.OutputTokens + usage.OutputTokens,
                    CacheReadTokens = earlier.CacheReadTokens + usage.CacheReadTokens,
                    CacheWriteTokens = earlier.CacheWriteTokens + usage.CacheWriteTokens
                };
        }

        if (frame.ModelUsage is not { Count: > 0 })
        {
            return;
        }

        _reportedTotals = new Dictionary<string, CliAgentTokenUsage>(StringComparer.Ordinal);
        foreach (var (model, usage) in frame.ModelUsage)
        {
            if (usage.ContextWindow is > 0 and var window)
            {
                _contextWindows[model] = window;
            }

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

        // 退出码只在回答交付之前说明问题。两种情形下它什么都不说明，已经交付的回答照常算完成：
        // · 最后一轮的 result 已到且不是错误（_finished）：之后适配器关 stdin、执行器在宽限期后终止还没退出的进程
        //   （它起的子进程，比如一个 dev server，握着它不放），143 / 137 是执行器自己造成的；就算是 CLI 在收尾时自己出了错，
        //   回答也已经完整交付。判成崩溃会丢掉付过钱的答案，续接过的会话还会被当成被拒、开新会话把整段提示重跑一遍。
        // · 等后台任务等到了上限：进程同样是执行器终止的。
        if (outcome.ExitCode is { } exitCode && exitCode != 0 && !_finished && !_backgroundWaitExpired)
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

    private string? SelectOutput()
    {
        if (!string.IsNullOrWhiteSpace(_finalResultText))
        {
            return _finalResultText;
        }

        if (!string.IsNullOrWhiteSpace(_lastAssistantText))
        {
            return _lastAssistantText;
        }

        // 一轮只做了 CLI 自己的事（/compact 之类）：没有回答不是缺了回答，结局在 Compaction / LocalCommand 上
        if (_localCommand is not null || _compaction is not null)
        {
            return null;
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
