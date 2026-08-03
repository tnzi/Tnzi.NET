namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IAgentDispatchFacade"/> 的唯一实现，也是整个框架里唯一的
/// 「内建 vs 外部」分支点。
/// </summary>
/// <remarks>
/// <para>
/// 分支只有一处，且判据只有一条：<c>ICliAgentBindingService.GetByAgentIdAsync</c> 是否返回绑定。
/// 没有 <c>AgentExecutionMode.ExternalCli</c>，没有 <c>ShouldSkipMiddleware</c>，
/// 内建路径一行未改。
/// </para>
/// <para>
/// 外部路径的事件翻译（<see cref="CliAgentEvent"/> → <see cref="AgentStreamChunk"/>）刻意留在核心：
/// 它是**契约之间**的映射，不是某个协议的细节。子模块只负责把各家 CLI 的私有形状归一化到
/// <see cref="CliAgentEvent"/>，到不了这里。
/// </para>
/// </remarks>
public class AgentDispatchFacade : IAgentDispatchFacade
{
    private readonly IAgentRuntime _runtime;
    private readonly ICliAgentBindingService _bindingService;
    private readonly ICliAgentDispatcher _cliDispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentThreadInternalService _threadService;
    private readonly ILogger<AgentDispatchFacade> _logger;

    /// <summary>初始化路由门面。</summary>
    public AgentDispatchFacade(
        IAgentRuntime runtime,
        ICliAgentBindingService bindingService,
        ICliAgentDispatcher cliDispatcher,
        IServiceScopeFactory scopeFactory,
        IAgentThreadInternalService threadService,
        ILogger<AgentDispatchFacade> logger)
    {
        _runtime = Check.NotNull(runtime);
        _bindingService = Check.NotNull(bindingService);
        _cliDispatcher = Check.NotNull(cliDispatcher);
        _scopeFactory = Check.NotNull(scopeFactory);
        _threadService = Check.NotNull(threadService);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 在独立作用域里入队，使这一行**立刻提交**。
    /// </summary>
    /// <remarks>
    /// 排队行绝不能落在调用方的环境事务里。宿主开着 <c>EnableGlobalUnitOfWork</c> 时，
    /// 请求的事务要到响应写出才提交，而门面紧接着就要等这条运行跑完 ——
    /// 队列处理器在另一条连接上，看不见未提交的行，于是运行永远不会开始、
    /// 请求永远等不到结果，**双方互相等到超时**；超时回滚后连记录都不留，
    /// 现场只剩一次「聊天挂了三分钟」而数据库里什么都没有。
    /// <para>
    /// 语义上这也是对的：一次外部运行动辄几分钟到几小时，它的生命周期本就长于
    /// 那个 HTTP 请求，不该由请求的事务决定它是否存在。
    /// </para>
    /// </remarks>
    /// <summary>
    /// 取或建这一轮所属的会话线程，并把用户消息落库。
    /// </summary>
    /// <remarks>
    /// 内建路径的线程是中间件管线建的，而外部执行按红线①<b>整条管线都不进</b> ——
    /// 于是在补上这一步之前，外部路径的 <c>ThreadId</c> 恒为 null，后果不是「少个 id」
    /// 那么轻：<c>EnqueueAsync</c> 正是按 ThreadId 去找上一轮的 <c>ProviderSessionId</c> 才决定
    /// 要不要续接的，所以**每一轮都开一个全新的 CLI 会话**，用户看到的就是 agent 完全不记得
    /// 上一句话。整套 resume 机制（会话指针、被拒判据、上下文丢失披露）当时都已实现，
    /// 只是从来没有任何东西触发它。
    /// </remarks>
    private async Task<Guid?> EnsureThreadAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (_, threadId, _) = await _threadService.GetOrCreateThreadAsync(
                request.ThreadId, request.AgentId, cancellationToken);

            // 就地回填到请求上。内建路径由 HistoryMiddleware 做这件事，而流式调用方
            // （ChatService）正是从 request.ThreadId 读出线程 id 写进 SSE 的 done 事件的。
            // 不回填的话前端永远学不到线程 id，下一轮又开一个新线程 —— 表面症状就是
            // 「聊天不连续」，而且非流式路径看起来是好的，只有流式坏，最难查。
            request.ThreadId = threadId;

            if (!string.IsNullOrEmpty(request.UserMessage))
            {
                await _threadService.SaveMessageAsync(
                    threadId, "user", request.UserMessage, ct: cancellationToken);
            }

            return threadId;
        }
        catch (Exception ex)
        {
            // 建不出线程不该让这一轮直接失败：没有它只是失去续接与历史，
            // 而用户要的那次执行本身仍然可以完成。
            _logger.LogWarning(ex, "Could not resolve a thread for the external run; continuity is lost for this turn");
            return request.ThreadId;
        }
    }

    /// <summary>把外部执行的回复写回线程，让会话历史与内建路径一致。</summary>
    private async Task PersistReplyAsync(Guid? threadId, string? reply, CancellationToken cancellationToken)
    {
        if (threadId is not { } id || string.IsNullOrEmpty(reply)) return;

        try
        {
            await _threadService.SaveMessageAsync(id, "assistant", reply, ct: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External run {ThreadId} completed but its reply could not be persisted", id);
        }
    }

    private async Task<Result<Guid>> EnqueueDetachedAsync(
        CliRunRequestDto request, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICliAgentDispatcher>();
        return await dispatcher.EnqueueAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AgentRunResult> RunAsync(
        AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var binding = await ResolveBindingAsync(request, cancellationToken);
        if (binding is null)
        {
            return await _runtime.RunAsync(request, cancellationToken);
        }

        var threadId = await EnsureThreadAsync(request, cancellationToken);

        var enqueued = await EnqueueDetachedAsync(ToCliRequest(request, threadId), cancellationToken);
        if (!enqueued.Succeeded)
        {
            return FailedResult(request, enqueued.Message ?? "Failed to enqueue external agent run");
        }

        var runId = enqueued.Data;

        // 消费到流结束 = 运行到达终态。这里刻意不做轮询：流本身在终态时结束，
        // 轮询只会在「跑了三小时的任务」上多制造几千次无谓查询。
        await foreach (var _ in _cliDispatcher.StreamAsync(runId, 0, cancellationToken))
        {
            // 非流式调用方不消费中间事件；事件的持久化在子模块里已经发生。
        }

        var run = await _cliDispatcher.GetAsync(runId, cancellationToken);
        if (!run.Succeeded || run.Data is null)
        {
            return FailedResult(request, run.Message ?? "External agent run vanished after dispatch");
        }

        await PersistReplyAsync(threadId, run.Data.Output, cancellationToken);

        return ToRunResult(request, run.Data, threadId);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var binding = await ResolveBindingAsync(request, cancellationToken);
        if (binding is null)
        {
            await foreach (var chunk in _runtime.RunStreamingAsync(request, cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                yield return chunk;
            }

            yield break;
        }

        var threadId = await EnsureThreadAsync(request, cancellationToken);

        var enqueued = await EnqueueDetachedAsync(ToCliRequest(request, threadId), cancellationToken);
        if (!enqueued.Succeeded)
        {
            yield return new AgentStreamChunk
            {
                Error = enqueued.Message ?? "Failed to enqueue external agent run",
                FinishReason = FinishReasons.Error
            };
            yield break;
        }

        var runId = enqueued.Data;

        await foreach (var evt in _cliDispatcher.StreamAsync(runId, 0, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            var chunk = ToStreamChunk(evt, request.StreamMode);
            if (chunk != null)
            {
                yield return chunk;
            }
        }

        var run = await _cliDispatcher.GetAsync(runId, cancellationToken);
        yield return run.Succeeded && run.Data is not null
            ? new AgentStreamChunk
            {
                FinishReason = ToFinishReason(run.Data.Status),
                Error = run.Data.Error,
                Usage = ParseUsage(run.Data.UsageJson)
            }
            : new AgentStreamChunk
            {
                FinishReason = FinishReasons.Error,
                Error = run.Message ?? "External agent run vanished after dispatch"
            };
    }

    /// <summary>
    /// 唯一的路由判据。无 AgentId（直接指定 provider/model 的裸调用）恒走内建 ——
    /// 外部执行是绑在 Agent 上的能力。
    /// </summary>
    private async Task<CliAgentBindingDto?> ResolveBindingAsync(
        AgentRunRequest request, CancellationToken cancellationToken)
    {
        if (request.AgentId is not { } agentId || agentId == Guid.Empty)
        {
            return null;
        }

        // Workflow 走内建编排：它的节点语义（审批、路由、辩论）建立在框架自己的
        // 执行图上，外部 agent 不参与其中。
        if (request.WorkflowId.HasValue)
        {
            return null;
        }

        var binding = await _bindingService.GetByAgentIdAsync(agentId, cancellationToken);
        if (binding != null)
        {
            _logger.LogDebug(
                "Agent {AgentId} routed to external CLI runtime {RuntimeId} ({ProviderKey})",
                agentId, binding.CliRuntimeId, binding.ProviderKey);
        }

        return binding;
    }

    private static CliRunRequestDto ToCliRequest(AgentRunRequest request, Guid? threadId) => new()
    {
        AgentId = request.AgentId!.Value,
        Prompt = request.UserMessage ?? string.Empty,
        ThreadId = threadId ?? request.ThreadId,
        AgentRunId = request.ExistingRunId,
        UserId = request.UserId
    };

    private static AgentRunResult ToRunResult(AgentRunRequest request, CliRunDto run, Guid? threadId) => new()
    {
        Response = run.Output ?? run.Error ?? string.Empty,
        RunId = run.AgentRunId,
        ThreadId = run.ThreadId ?? threadId ?? request.ThreadId,
        Usage = ParseUsage(run.UsageJson),
        FinishReason = ToFinishReason(run.Status),
        Status = ToAgentRunStatus(run.Status)
    };

    private static AgentRunResult FailedResult(AgentRunRequest request, string message) => new()
    {
        Response = message,
        ThreadId = request.ThreadId,
        FinishReason = FinishReasons.Error,
        Status = AgentRunStatus.Failed
    };

    private static AgentStreamChunk? ToStreamChunk(CliAgentEvent evt, StreamMode mode)
    {
        switch (evt.Type)
        {
            case CliAgentEventType.Text:
                return new AgentStreamChunk { Text = evt.Content, Mode = StreamMode.Messages };

            case CliAgentEventType.Thinking:
                return new AgentStreamChunk { ReasoningText = evt.Content, Mode = StreamMode.Messages };

            case CliAgentEventType.ToolUse:
                return new AgentStreamChunk
                {
                    IsToolCall = true,
                    ToolCallNames = evt.Tool is null ? null : [evt.Tool],
                    Mode = StreamMode.Messages
                };

            case CliAgentEventType.Error:
                return new AgentStreamChunk { Error = evt.Content, Mode = StreamMode.Messages };

            // 工具结果、状态播报、日志属于过程细节：只有显式要求更细粒度的调用方才收。
            case CliAgentEventType.ToolResult:
            case CliAgentEventType.Status:
            case CliAgentEventType.Log:
                return mode.HasFlag(StreamMode.Steps)
                    ? new AgentStreamChunk
                    {
                        Mode = StreamMode.Steps,
                        EventType = evt.Type.ToString(),
                        Text = evt.Type == CliAgentEventType.ToolResult ? null : evt.Content,
                        ToolCallNames = evt.Tool is null ? null : [evt.Tool]
                    }
                    : null;

            default:
                return null;
        }
    }

    private static string ToFinishReason(CliRunStatus status) => status switch
    {
        CliRunStatus.Completed => FinishReasons.Stop,
        CliRunStatus.Cancelled => FinishReasons.Cancelled,
        CliRunStatus.TimedOut => FinishReasons.Error,
        CliRunStatus.Failed => FinishReasons.Error,
        _ => FinishReasons.Stop
    };

    private static AgentRunStatus ToAgentRunStatus(CliRunStatus status) => status switch
    {
        CliRunStatus.Completed => AgentRunStatus.Completed,
        CliRunStatus.Cancelled => AgentRunStatus.Cancelled,
        CliRunStatus.Failed or CliRunStatus.TimedOut => AgentRunStatus.Failed,
        CliRunStatus.Running or CliRunStatus.Dispatched => AgentRunStatus.Running,
        _ => AgentRunStatus.Pending
    };

    /// <summary>
    /// 把按模型分组的用量 JSON 折叠成框架的单一 <see cref="TokenUsageDto"/>。
    /// 一个 turn 可能跨多个模型（子 agent / 压缩调用），这里累加；
    /// 按模型的明细保留在 <c>CliRun.UsageJson</c> 里供成本分析用。
    /// </summary>
    private static TokenUsageDto? ParseUsage(string? usageJson)
    {
        if (string.IsNullOrWhiteSpace(usageJson))
        {
            return null;
        }

        Dictionary<string, CliAgentTokenUsage>? byModel;
        try
        {
            byModel = JsonSerializer.Deserialize<Dictionary<string, CliAgentTokenUsage>>(
                usageJson, TnziJsonDefaults.Options);
        }
        catch (JsonException)
        {
            // 用量是观测数据，解析不了不该让整个运行的读取失败。
            return null;
        }

        if (byModel is null || byModel.Count == 0)
        {
            return null;
        }

        var usage = new TokenUsageDto();
        foreach (var entry in byModel.Values)
        {
            usage.InputTokens += (int)entry.InputTokens;
            usage.OutputTokens += (int)entry.OutputTokens;
            usage.CachedInputTokens += (int)entry.CacheReadTokens;
            usage.CacheCreationTokens += (int)entry.CacheWriteTokens;
        }

        usage.TotalTokens = usage.InputTokens + usage.OutputTokens;
        return usage;
    }
}
