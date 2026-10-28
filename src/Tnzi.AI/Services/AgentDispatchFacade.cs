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
/// <see cref="CliAgentEvent"/>，到不了这里。回复的折叠（文本块怎么接、工具调用怎么按 id 合并）用的是
/// <see cref="CliReplyAccumulator"/>，与子模块在运行结束时写进线程的那条回复同一份逻辑。
/// </para>
/// <para>
/// ★ <b>回复不在这里写</b>。外部运行结束时由 <c>Tnzi.AI.Cli</c> 自己把回复（连同工具调用）写进线程、给新线程起标题。
/// 此前这两件事挂在门面上，前提是发起请求的客户端一直连着；客户端刷新或断线，运行照跑完，回复却只留在运行记录里。
/// 门面现在只负责把一轮交出去、把运行的事件流翻译回来；终止块上的回复消息 id 取自运行记录。
/// </para>
/// </remarks>
public class AgentDispatchFacade : IAgentDispatchFacade
{
    private readonly IAgentRuntime _runtime;
    private readonly ICliAgentBindingService _bindingService;
    private readonly ICliAgentDispatcher _cliDispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentDispatchFacade> _logger;
    private readonly IOptionsMonitor<AiChatOptions>? _chatOptions;
    private readonly IUsageLimitService? _usageLimits;
    private readonly IBudgetService? _budgetService;
    private readonly ICurrentTenant? _currentTenant;

    /// <summary>初始化路由门面。</summary>
    public AgentDispatchFacade(
        IAgentRuntime runtime,
        ICliAgentBindingService bindingService,
        ICliAgentDispatcher cliDispatcher,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentDispatchFacade> logger,
        IOptionsMonitor<AiChatOptions>? chatOptions = null,
        IUsageLimitService? usageLimits = null,
        IBudgetService? budgetService = null,
        ICurrentTenant? currentTenant = null)
    {
        _runtime = Check.NotNull(runtime);
        _bindingService = Check.NotNull(bindingService);
        _cliDispatcher = Check.NotNull(cliDispatcher);
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);
        _chatOptions = chatOptions;
        _usageLimits = usageLimits;
        _budgetService = budgetService;
        _currentTenant = currentTenant;
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

        if (RefuseUnsupportedRequest(request) is { } refusal)
        {
            return FailedResult(threadId: null, refusal);
        }

        var turn = await QueueExternalTurnAsync(request, binding, cancellationToken);

        // 消费到流结束 = 运行到达终态。这里刻意不做轮询：流本身在终态时结束，
        // 轮询只会在「跑了三小时的任务」上多制造几千次无谓查询。
        await foreach (var _ in _cliDispatcher.StreamAsync(turn.RunId, 0, cancellationToken))
        {
            // 非流式调用方不消费中间事件；事件的持久化在子模块里已经发生。
        }

        cancellationToken.ThrowIfCancellationRequested();

        var run = await _cliDispatcher.GetAsync(turn.RunId, cancellationToken);
        if (!run.Succeeded || run.Data is null)
        {
            return FailedResult(turn.ThreadId, run.Message ?? "External agent run vanished after dispatch");
        }

        return ToRunResult(run.Data, turn.ThreadId);
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

        if (RefuseUnsupportedRequest(request) is { } refusal)
        {
            yield return new AgentStreamChunk { Error = refusal, FinishReason = FinishReasons.Error };
            yield break;
        }

        // 拒绝（附件、消费方处理器、入队失败）以 BusinessException 抛出：此时一个块都还没交出去，
        // 流式端点的响应头尚未提交，客户端拿到的是带状态码与错误码的普通 HTTP 错误。
        var turn = await QueueExternalTurnAsync(request, binding, cancellationToken);

        yield return new AgentStreamChunk
        {
            EventType = RunStreamEventTypes.RunStarted,
            RunId = turn.RunId,
            UserMessageId = turn.UserMessageId,
            Mode = StreamMode.Messages
        };

        await foreach (var chunk in StreamRunAsync(turn.RunId, 0, request.StreamMode, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <inheritdoc />
    public async Task<ChatRunDto?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _cliDispatcher.GetAsync(runId, cancellationToken);
        return run.Succeeded && run.Data is { } data ? ToChatRun(data) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatRunDto>> GetActiveRunsAsync(Guid threadId, CancellationToken cancellationToken = default)
    {
        var runs = await _cliDispatcher.GetListAsync(
            new CliRunQueryDto { ThreadId = threadId, ActiveOnly = true, PageSize = 20 }, cancellationToken);

        // 外部执行没加载时调度器的回退答 501：那就是「没有可接上的运行」
        if (!runs.Succeeded || runs.Data is null)
        {
            return [];
        }

        return runs.Data.Items
            .OrderBy(r => r.CreationTime)
            .Select(ToChatRun)
            .ToList();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> AttachStreamingAsync(
        Guid runId,
        int fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var run = await _cliDispatcher.GetAsync(runId, cancellationToken);
        if (!run.Succeeded || run.Data is null)
        {
            // 与「不是你的」同一个答案：不区分，否则等于告诉试探者哪些 id 是真的
            throw new BusinessException("Run not found.", ErrorCodes.ChatRunNotFound, 404);
        }

        yield return new AgentStreamChunk
        {
            EventType = RunStreamEventTypes.RunStarted,
            RunId = runId,
            UserMessageId = run.Data.UserMessageId,
            Mode = StreamMode.Messages
        };

        await foreach (var chunk in StreamRunAsync(runId, Math.Max(0, fromSequence), StreamMode.Messages, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <inheritdoc />
    public async Task<Result> AnswerRunPermissionAsync(
        Guid runId, Guid requestId, CliPermissionAnswerDto answer, CancellationToken cancellationToken = default)
    {
        var answered = await _cliDispatcher.AnswerPermissionAsync(runId, requestId, answer, cancellationToken);
        if (answered.Succeeded || answered.Code != 404 || answered.ErrorCode == ErrorCodes.CliPermissionRequestNotFound)
        {
            return answered;
        }

        return Result.Failure("Run not found.", 404, ErrorCodes.ChatRunNotFound);
    }

    /// <inheritdoc />
    public async Task<Result> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var cancelled = await _cliDispatcher.CancelAsync(runId, cancellationToken);
        if (cancelled.Succeeded || cancelled.Code != 404)
        {
            return cancelled;
        }

        return Result.Failure("Run not found.", 404, ErrorCodes.ChatRunNotFound);
    }

    /// <summary>
    /// 把一次外部运行的事件流翻译成流式块，最后给出终止块。
    /// </summary>
    /// <remarks>
    /// 从中途接上（<paramref name="fromSequence"/> &gt; 0）时仍从第一条事件读起：回复的折叠（文本块之间的分隔、
    /// 工具卡片的位置与名字）要看见前面的事件才算得对，只是那些块不再交出去。
    /// </remarks>
    private async IAsyncEnumerable<AgentStreamChunk> StreamRunAsync(
        Guid runId, int fromSequence, StreamMode mode,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reply = new CliReplyAccumulator();

        await foreach (var evt in _cliDispatcher.StreamAsync(runId, 0, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            var chunk = ToStreamChunk(evt, reply, mode);
            if (chunk != null && (evt.Sequence ?? int.MaxValue) > fromSequence)
            {
                yield return chunk;
            }
        }

        // 订阅在调用方取消时安静地结束（不抛）：此时运行多半还在跑，不能把它当成终态报出去
        cancellationToken.ThrowIfCancellationRequested();

        var run = await _cliDispatcher.GetAsync(runId, cancellationToken);
        if (!run.Succeeded || run.Data is null)
        {
            yield return new AgentStreamChunk
            {
                FinishReason = FinishReasons.Error,
                Error = run.Message ?? "External agent run vanished after dispatch",
                RunId = runId
            };
            yield break;
        }

        var usageLimitCode = UsageLimitErrorCode(run.Data.FailureReason);
        yield return new AgentStreamChunk
        {
            FinishReason = ToFinishReason(run.Data.Status),
            Error = run.Data.Status == CliRunStatus.Completed ? null : run.Data.Error,
            ErrorCode = usageLimitCode,
            UsageLimitResetsAt = usageLimitCode is null ? null : run.Data.UsageLimitResetsAt,
            Usage = ParseUsage(run.Data.UsageJson),
            UserMessageId = run.Data.UserMessageId,
            AssistantMessageId = run.Data.AssistantMessageId,
            RunId = runId
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

    /// <summary>
    /// 把一轮交给外部运行时：校验附件 → 核线程 → 消费方处理器 → 建线程、存用户消息 → 入队 → 通知处理器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>写任何东西之前先把能拒绝的都拒绝掉</b>（附件不合规、provider 收不了附件、线程不是你的、用量上限、预算、处理器拒绝）：
    /// 拒绝时线程里什么都不留，不会出现一条没有回复、也永远不会有回复的用户消息。拒绝一律以
    /// <see cref="BusinessException"/> 抛出，带状态码与错误码（用量上限还带 <see cref="UsageLimitDenialDto"/> 作错误详情）。
    /// 调度器入队时还有它自己的几道门（运行时被停用、Agent 被停用……），那些在用户消息写下之后才判得出：
    /// 入队失败时把这一轮写下的用户消息（与为它新建的线程）撤掉，结局同样是什么都不留。
    /// </para>
    /// <para>
    /// 线程、用户消息与排队行在一个独立的 DI 作用域里写、立刻提交。排队行绝不能落在调用方的环境事务里：
    /// 宿主开着 <c>EnableGlobalUnitOfWork</c> 时，请求的事务要到响应写出才提交，而流式请求要等这条运行跑完；
    /// 队列处理器在另一条连接上，看不见未提交的行，于是运行永远不会开始，<b>双方互相等到超时</b>。
    /// 线程也在这个作用域里建，理由相同再加一条：调度器自己会按 <c>AgentThread</c> 表核一遍线程归属，
    /// 一条刚在调用方事务里建出来、尚未提交的线程，在新作用域里是看不见的。
    /// </para>
    /// </remarks>
    private async Task<ExternalTurn> QueueExternalTurnAsync(
        AgentRunRequest request, CliAgentBindingDto binding, CancellationToken cancellationToken)
    {
        var limits = _chatOptions?.CurrentValue.Attachments ?? new ChatAttachmentOptions();
        var attachments = ChatAttachmentRules.Read(request.ContentParts, limits);
        if (!attachments.Succeeded)
        {
            throw new BusinessException(attachments.Message!, attachments.ErrorCode ?? ErrorCodes.InvalidContent, attachments.Code ?? 400);
        }

        var files = attachments.Data!;
        if (files.Count > 0 && !binding.AcceptsAttachments)
        {
            throw new BusinessException(
                $"This agent runs on {binding.ProviderKey ?? "an external CLI runtime"}, which cannot receive attachments.",
                ErrorCodes.UnsupportedMediaType, 415);
        }

        var text = PromptText(request);
        if (string.IsNullOrWhiteSpace(text) && files.Count == 0)
        {
            throw new BusinessException("Either Message or Content must be provided", ErrorCodes.InvalidContent, 400);
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var threads = services.GetRequiredService<IAgentThreadInternalService>();
        var agentId = request.AgentId!.Value;

        // 临时运行（评估）不建线程、不写消息，与内建路径的 ThreadResolution / History 守卫同口径；
        // 没有线程也就没有续接，CLI 每次开新会话。
        Guid? threadId = null;
        var degraded = false;
        if (!request.Ephemeral && request.ThreadId is { } requested)
        {
            (threadId, degraded) = await VerifyThreadAsync(threads, requested, agentId, cancellationToken);
        }

        await RefuseOverLimitTurnAsync(request, agentId, cancellationToken);

        var handlers = services.GetServices<ICliAgentTurnHandler>().ToList();
        var context = await RunTurnHandlersAsync(handlers, new CliTurnContext
        {
            AgentId = agentId,
            ThreadId = threadId,
            UserId = request.UserId,
            Message = text,
            Attachments = files.Select(f => new CliTurnAttachment(f.MediaType, f.FileName, f.Content.LongLength)).ToList(),
            RewoundTurns = request.RewoundTurns,
            IsEphemeral = request.Ephemeral,
            Metadata = request.TurnMetadata ?? EmptyMetadata
        }, cancellationToken);

        var isNewThread = false;
        if (!request.Ephemeral && threadId is null && !degraded)
        {
            (threadId, isNewThread) = await CreateThreadAsync(threads, agentId, cancellationToken);
        }

        // 就地回填到请求上。流式调用方（ChatService）正是从 request.ThreadId 读出线程 id 写进 SSE 事件的：
        // 不回填的话前端永远学不到线程 id，下一轮又开一个新线程。
        request.ThreadId = threadId;

        Guid? userMessageId = null;
        if (threadId is { } id)
        {
            userMessageId = await SaveUserMessageAsync(threads, id, text, request.TurnMetadata, cancellationToken);
        }

        var dispatcher = services.GetRequiredService<ICliAgentDispatcher>();
        var enqueued = await dispatcher.EnqueueAsync(new CliRunRequestDto
        {
            AgentId = agentId,
            Prompt = text,
            ThreadId = threadId,
            AgentRunId = request.ExistingRunId,
            UserId = request.UserId,
            PerTurnContext = context,
            Attachments = files.Count > 0 ? AttachmentParts(request.ContentParts) : null,
            UserMessageId = userMessageId,
            Model = CliModel(request.Model, binding),
            // 有登录用户在聊的一轮有人等着：「先问我」档的权限请求交给界面批准。进程内调用方（TnziAiClient 之类）可能是后台任务，按无人值守
            Attended = request.UserId is not null && !request.TrustedToolSelection
        }, cancellationToken);

        if (!enqueued.Succeeded)
        {
            await WithdrawTurnAsync(services, threadId, isNewThread, userMessageId);
            throw new BusinessException(
                enqueued.Message ?? "Failed to enqueue external agent run",
                enqueued.ErrorCode ?? ErrorCodes.ChatFailed,
                enqueued.Code is >= 400 ? enqueued.Code.Value : 500)
            {
                ErrorDetails = enqueued.ErrorDetails
            };
        }

        var queued = new CliTurnQueued
        {
            RunId = enqueued.Data,
            AgentId = agentId,
            ThreadId = threadId,
            IsNewThread = isNewThread,
            UserMessageId = userMessageId,
            UserId = request.UserId,
            Context = context,
            RewoundTurns = request.RewoundTurns,
            Metadata = request.TurnMetadata ?? EmptyMetadata
        };
        await NotifyQueuedAsync(handlers, queued);

        return new ExternalTurn(enqueued.Data, threadId, userMessageId);
    }

    /// <summary>
    /// 核调用方给的线程：存在、归运行发起人、绑在这个 Agent 上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>线程服务的拒绝必须原样传播</b>。<c>GetOrCreateThreadAsync</c> 对「不是你的」「不存在」
    /// 「不属于这个 Agent」一律抛 <see cref="BusinessException"/>（404）；这里若把它和基础设施故障
    /// 一起吞掉再把调用方给的 id 原样交给调度器，就等于放行一个未经校验的 ThreadId ——
    /// 运行会续接受害者的 CLI 会话、跑在受害者的每线程工作区里、再把回复写进受害者的线程。
    /// </para>
    /// <para>
    /// 真正的基础设施故障（数据库不可达）仍然降级：这一轮照跑，只是没有续接与历史。
    /// 但降级的答案是 <c>null</c> 而不是调用方给的那个 id —— 没人核过的 id 不能活过这一步，
    /// 丢一轮连续性是安全的方向，放行则不是。
    /// </para>
    /// </remarks>
    private async Task<(Guid? ThreadId, bool Degraded)> VerifyThreadAsync(
        IAgentThreadInternalService threads, Guid threadId, Guid agentId, CancellationToken cancellationToken)
    {
        try
        {
            var (verified, _) = await threads.GetOrCreateThreadAsync(threadId, agentId, cancellationToken);
            return (verified, false);
        }
        catch (BusinessException)
        {
            // 归属不符 / 线程不存在 / Agent 不存在：这是判决，不是故障。
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve a thread for the external run; continuity is lost for this turn");
            return (null, true);
        }
    }

    private async Task<(Guid? ThreadId, bool IsNew)> CreateThreadAsync(
        IAgentThreadInternalService threads, Guid agentId, CancellationToken cancellationToken)
    {
        try
        {
            var (created, isNew) = await threads.GetOrCreateThreadAsync(null, agentId, cancellationToken);
            return (created, isNew);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create a thread for the external run; continuity is lost for this turn");
            return (null, false);
        }
    }

    private async Task<Guid?> SaveUserMessageAsync(
        IAgentThreadInternalService threads, Guid threadId, string text,
        IReadOnlyDictionary<string, JsonElement>? metadata, CancellationToken cancellationToken)
    {
        try
        {
            // 带元数据时连同它一起存（重新生成要原样再带一次）；不带时走原来那个方法
            var id = metadata is { Count: > 0 }
                ? await threads.SaveUserMessageAsync(threadId, text, metadata, ct: cancellationToken)
                : await threads.SaveMessageAsync(threadId, MessageRole.User, text, ct: cancellationToken);
            return id == Guid.Empty ? null : id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 写不进用户消息不该让这一轮跑不了：回复照样会写进线程，只是缺了提问那一半
            _logger.LogWarning(ex, "Could not store the user message of an external run in thread {ThreadId}", threadId);
            return null;
        }
    }

    /// <summary>
    /// 用量上限与 USD 预算：在建线程、存用户消息之前拒绝。认领之后执行器还会再查一遍（队列可能积压），这一道是为了不留痕迹地当场告诉调用方。
    /// </summary>
    /// <remarks>
    /// 只查不开窗口：窗口在这一轮真正开始（执行器认领）时才开。归属与执行器、调度器一致：<see cref="AgentRunRequest.UserId"/>；
    /// 没有用户时跳过，与内建路径的 <c>QuotaMiddleware</c> 一致。
    /// </remarks>
    private async Task RefuseOverLimitTurnAsync(AgentRunRequest request, Guid agentId, CancellationToken cancellationToken)
    {
        if (request.UserId is not { } userId)
        {
            return;
        }

        if (_usageLimits is not null && await _usageLimits.CheckAsync(userId, cancellationToken) is { } denial)
        {
            var window = denial.Window == UsageLimitWindow.Session ? "5-hour" : "weekly";
            throw new BusinessException(
                denial.ResetsAt is { } resetsAt
                    ? $"You have reached your {window} usage limit. It resets at {resetsAt:yyyy-MM-dd HH:mm} UTC."
                    : $"You have reached your {window} usage limit.",
                ErrorCodes.UsageLimitReached, 429)
            {
                ErrorDetails = denial
            };
        }

        if (_budgetService is not null)
        {
            var budget = await _budgetService.CheckBudgetAsync(userId, _currentTenant?.Id, agentId, cancellationToken);
            if (!budget.IsAllowed)
            {
                throw new BusinessException(budget.Reason ?? "The AI cost budget is exhausted.", ErrorCodes.CliBudgetExceeded, 402);
            }
        }
    }

    /// <summary>
    /// 入队失败后撤掉这一轮写下的东西：用户消息，以及为它新建的线程。尽力而为，撤不掉只记日志（拒绝照常交给调用方）。
    /// </summary>
    private async Task WithdrawTurnAsync(IServiceProvider services, Guid? threadId, bool isNewThread, Guid? userMessageId)
    {
        if (threadId is not { } id)
        {
            return;
        }

        try
        {
            if (isNewThread && services.GetService<IAgentThreadService>() is { } threadService)
            {
                await threadService.DeleteAsync(id);
                return;
            }

            if (userMessageId is { } messageId)
            {
                var threads = services.GetRequiredService<IAgentThreadInternalService>();
                if (await threads.FindUserMessageAsync(id, messageId, CancellationToken.None) is { } message)
                {
                    await threads.DeleteMessagesFromAsync(id, message.Order, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The external run could not be queued, and the turn it wrote to thread {ThreadId} could not be withdrawn", id);
        }
    }

    /// <summary>
    /// 交给 CLI 的模型：模型清单里有、且属于这个运行时的 provider 才交。聊天入口已经按用户的允许列表校验过；
    /// 没配模型清单时一律不交（由绑定决定），否则任何调用方都能点名最贵的那个。
    /// </summary>
    private string? CliModel(string? requested, CliAgentBindingDto binding)
    {
        if (string.IsNullOrWhiteSpace(requested) || _chatOptions?.CurrentValue.Models is not { Count: > 0 } catalog)
        {
            return null;
        }

        var match = catalog.FirstOrDefault(m =>
            m is not null
            && string.Equals(m.Id?.Trim(), requested.Trim(), StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(m.Provider) || string.Equals(m.Provider.Trim(), binding.ProviderKey, StringComparison.OrdinalIgnoreCase)));

        if (match is null)
        {
            _logger.LogWarning("Model {Model} is not in the model list for provider {ProviderKey}; the binding's model is used instead",
                requested, binding.ProviderKey);
        }

        return match?.Id.Trim();
    }

    /// <summary>
    /// 依次调用消费方的轮次处理器，返回拼接后的上下文；任一拒绝即以 <see cref="BusinessException"/> 抛出。
    /// </summary>
    private async Task<string?> RunTurnHandlersAsync(
        IReadOnlyList<ICliAgentTurnHandler> handlers, CliTurnContext turn, CancellationToken cancellationToken)
    {
        if (handlers.Count == 0)
        {
            return null;
        }

        var contexts = new List<string>();
        foreach (var handler in handlers)
        {
            CliTurnDecision decision;
            try
            {
                decision = await handler.OnTurnAsync(turn, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not BusinessException)
            {
                // 失败关闭：一个处理器坏了，不能让这一轮在没有它的上下文（或越过它的拒绝）的情况下照跑
                _logger.LogError(ex, "Turn handler {Handler} failed for agent {AgentId}; the turn was not queued",
                    handler.GetType().Name, turn.AgentId);
                throw new BusinessException(
                    "This message could not be sent. Please try again.", ErrorCodes.CliTurnHandlerFailed, 500);
            }

            if (decision.Refused)
            {
                _logger.LogInformation("Turn handler {Handler} refused a turn for agent {AgentId} (thread={ThreadId})",
                    handler.GetType().Name, turn.AgentId, turn.ThreadId);
                throw new BusinessException(
                    decision.Message ?? "This message was refused.",
                    decision.ErrorCode ?? ErrorCodes.CliTurnRefused,
                    decision.StatusCode);
            }

            if (!string.IsNullOrWhiteSpace(decision.Context))
            {
                contexts.Add(decision.Context.Trim());
            }
        }

        return contexts.Count == 0 ? null : string.Join("\n\n", contexts);
    }

    /// <summary>
    /// 告诉处理器们这一轮已入队。运行已经在队列里了，处理器的失败只记日志。
    /// </summary>
    private async Task NotifyQueuedAsync(IReadOnlyList<ICliAgentTurnHandler> handlers, CliTurnQueued queued)
    {
        foreach (var handler in handlers)
        {
            try
            {
                await handler.OnTurnQueuedAsync(queued, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Turn handler {Handler} failed after run {RunId} was queued",
                    handler.GetType().Name, queued.RunId);
            }
        }
    }

    /// <summary>
    /// 外部执行做不到、又不能静默忽略的请求项；能执行时返回 null。
    /// </summary>
    /// <remarks>
    /// 钉住的 Agent 版本（只有评估设置它）：CLI 执行器按活行装配 Agent，没有版本快照可跑。此前照常入队，评估的是
    /// 现行定义，结果却记在用例钉住的版本名下。provider / model 覆盖不在此列：模型在模型清单里时交给 CLI（<c>--model</c>），
    /// 不在时由绑定决定，拒绝它会让每条带模型的聊天都失败。
    /// </remarks>
    private static string? RefuseUnsupportedRequest(AgentRunRequest request)
        => request.AgentVersionNumber.HasValue
            ? "This agent runs on an external CLI runtime, which cannot run a pinned agent version."
            : null;

    /// <summary>
    /// 交给 CLI 的提示文本：与内建路径同一口径取内容块里的文本（<c>UserTurn</c>），内容块里没有文本时退回 Message：
    /// 用户写下的话绝不能因为它放错了字段就不见了。
    /// </summary>
    private static string PromptText(AgentRunRequest request)
    {
        var text = UserTurn.TextOf(request.UserMessage, request.ContentParts);
        return string.IsNullOrWhiteSpace(text) ? request.UserMessage ?? string.Empty : text;
    }

    /// <summary>内容块里的附件块（文本块在提示里，不重复带）。</summary>
    private static List<ContentPartDto> AttachmentParts(IReadOnlyList<ContentPartDto>? parts)
        => parts?.Where(p => p is not TextContentPartDto).ToList() ?? [];

    private static ChatRunDto ToChatRun(CliRunDto run) => new()
    {
        RunId = run.Id,
        ThreadId = run.ThreadId,
        UserMessageId = run.UserMessageId,
        Status = run.Status,
        CreationTime = run.CreationTime
    };

    private static AgentRunResult ToRunResult(CliRunDto run, Guid? threadId) => new()
    {
        Response = run.Output ?? run.Error ?? string.Empty,
        RunId = run.AgentRunId,
        ThreadId = run.ThreadId ?? threadId,
        Usage = ParseUsage(run.UsageJson),
        FinishReason = ToFinishReason(run.Status),
        Status = ToAgentRunStatus(run.Status),
        ErrorCode = UsageLimitErrorCode(run.FailureReason),
        UsageLimitResetsAt = UsageLimitErrorCode(run.FailureReason) is null ? null : run.UsageLimitResetsAt,
        UserMessageId = run.UserMessageId,
        AssistantMessageId = run.AssistantMessageId
    };

    /// <summary>
    /// 以用量上限结束的运行带哪个错误码：CLI 背后账号的上限是 <c>AI_CLI_USAGE_LIMIT</c>，框架给用户设的上限是 <c>AI_USAGE_LIMIT_REACHED</c>；
    /// 其余结局为 null。
    /// </summary>
    private static string? UsageLimitErrorCode(CliRunFailureReason? reason) => reason switch
    {
        CliRunFailureReason.UsageLimit => ErrorCodes.CliUsageLimit,
        CliRunFailureReason.UserUsageLimit => ErrorCodes.UsageLimitReached,
        _ => null
    };

    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyMetadata = new Dictionary<string, JsonElement>();

    private static AgentRunResult FailedResult(Guid? threadId, string message) => new()
    {
        Response = message,
        ThreadId = threadId,
        FinishReason = FinishReasons.Error,
        Status = AgentRunStatus.Failed
    };

    /// <summary>
    /// 一条外部事件 → 一个流式块（没有要交给客户端的内容时为 null）。
    /// </summary>
    /// <remarks>
    /// 文本与工具调用都先经 <paramref name="reply"/>：文本块之间的分隔、工具卡片在第几个字符处、工具结果配回哪一张卡，
    /// 与运行结束时写进线程的那条回复同一个算法，刷新之后看到的与流式时看到的逐字相同。
    /// </remarks>
    private static AgentStreamChunk? ToStreamChunk(CliAgentEvent evt, CliReplyAccumulator reply, StreamMode mode)
    {
        var update = reply.Apply(evt);

        switch (evt.Type)
        {
            case CliAgentEventType.Text:
                return update.Text is null
                    ? null
                    : new AgentStreamChunk { Text = update.Text, Mode = StreamMode.Messages, Sequence = evt.Sequence };

            case CliAgentEventType.Thinking:
                return new AgentStreamChunk { ReasoningText = evt.Content, Mode = StreamMode.Messages, Sequence = evt.Sequence };

            case CliAgentEventType.ToolUse:
                return new AgentStreamChunk
                {
                    IsToolCall = true,
                    ToolCallNames = evt.Tool is null ? null : [evt.Tool],
                    ToolCalls = update.ToolCall is null ? null : [update.ToolCall],
                    Mode = StreamMode.Messages,
                    Sequence = evt.Sequence
                };

            case CliAgentEventType.ToolResult:
                return update.ToolCall is null
                    ? null
                    : new AgentStreamChunk { ToolCalls = [update.ToolCall], Mode = StreamMode.Messages, Sequence = evt.Sequence };

            case CliAgentEventType.Error:
                return new AgentStreamChunk { Error = evt.Content, Mode = StreamMode.Messages, Sequence = evt.Sequence };

            // 权限请求与结局总是交出（不论粒度）：界面据此弹出与收起批准卡片，漏掉一个就有一个动作一直等到超时
            case CliAgentEventType.PermissionRequest:
            case CliAgentEventType.PermissionResolved:
                return evt.Permission is null
                    ? null
                    : new AgentStreamChunk
                    {
                        Mode = StreamMode.Messages,
                        EventType = evt.Type == CliAgentEventType.PermissionRequest
                            ? RunStreamEventTypes.PermissionRequest
                            : RunStreamEventTypes.PermissionResolved,
                        EventData = PermissionEventData(evt),
                        Sequence = evt.Sequence
                    };

            // 状态播报、日志属于过程细节：只有显式要求更细粒度的调用方才收。
            case CliAgentEventType.Status:
            case CliAgentEventType.Log:
                return mode.HasFlag(StreamMode.Steps)
                    ? new AgentStreamChunk
                    {
                        Mode = StreamMode.Steps,
                        EventType = evt.Type.ToString(),
                        Text = evt.Content,
                        Sequence = evt.Sequence
                    }
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// 权限事件在聊天流上的 <c>eventData</c>（camelCase 键）：请求 id、工具、工具调用 id、入参、说明、选项、过期时间；结局另带选中的选项与回答的人。
    /// </summary>
    private static Dictionary<string, object> PermissionEventData(CliAgentEvent evt)
    {
        var permission = evt.Permission!;
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["requestId"] = permission.RequestId ?? Guid.Empty,
            ["status"] = permission.Status.ToString()
        };

        AddIfPresent(data, "tool", evt.Tool);
        AddIfPresent(data, "toolCallId", evt.CallId);
        AddIfPresent(data, "description", evt.Content);
        AddIfPresent(data, "title", permission.Title);
        AddIfPresent(data, "reason", permission.Reason);
        AddIfPresent(data, "blockedPath", permission.BlockedPath);
        AddIfPresent(data, "optionId", permission.OptionId);

        if (evt.Input is not null)
        {
            data["input"] = evt.Input;
        }

        if (permission.Options.Count > 0)
        {
            data["options"] = permission.Options;
        }

        if (permission.ExpiresAt is { } expiresAt)
        {
            data["expiresAt"] = expiresAt;
        }

        if (permission.AnsweredBy is { } answeredBy)
        {
            data["answeredBy"] = answeredBy;
        }

        return data;
    }

    private static void AddIfPresent(Dictionary<string, object> data, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            data[key] = value;
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

    /// <param name="RunId">入队的运行。</param>
    /// <param name="ThreadId">这一轮所在的线程。</param>
    /// <param name="UserMessageId">存下的用户消息。</param>
    private sealed record ExternalTurn(Guid RunId, Guid? ThreadId, Guid? UserMessageId);
}
