namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 外部 agent 调度入口的真实实现。
/// </summary>
public class CliAgentDispatcher : ApplicationService, ICliAgentDispatcher
{
    /// <summary>系统取消原因的长度上限：它写进运行的错误信息与结束事件。</summary>
    private const int MaxSystemCancelReasonLength = 500;

    /// <summary>拒绝说明的长度上限（与列宽一致）：它原样交给 agent。</summary>
    private const int MaxPermissionMessageLength = 2000;

    private readonly IRepository<CliRun, Guid> _runRepository;
    private readonly IRepository<CliRunMessage, Guid> _messageRepository;
    private readonly IRepository<CliAgentBinding, Guid> _bindingRepository;
    private readonly IRepository<Entities.CliRuntime, Guid> _runtimeRepository;
    private readonly IRepository<AgentThread, Guid> _threadRepository;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly CliRunSignalHub _signalHub;
    private readonly CliRunCancellationRegistry _cancellationRegistry;
    private readonly IOptionsMonitor<CliAgentOptions> _options;
    private readonly IBudgetService? _budgetService;
    private readonly IUsageLimitService? _usageLimits;
    private readonly ICliProviderRegistry? _providerRegistry;
    private readonly IAgentAttachmentService? _attachmentService;
    private readonly IRepository<AgentThreadMessage, Guid>? _threadMessageRepository;
    private readonly IOptionsMonitor<AiChatOptions>? _chatOptions;
    private readonly IUsagePolicyService? _usagePolicy;
    private readonly ICliRunPlacementResolver? _placementResolver;
    private readonly CliRunQueueSignal? _queueSignal;
    private readonly IRepository<CliRunPermissionRequest, Guid>? _permissionRepository;

    /// <summary>初始化调度器。</summary>
    public CliAgentDispatcher(
        IRepository<CliRun, Guid> runRepository,
        IRepository<CliRunMessage, Guid> messageRepository,
        IRepository<CliAgentBinding, Guid> bindingRepository,
        IRepository<Entities.CliRuntime, Guid> runtimeRepository,
        IRepository<AgentThread, Guid> threadRepository,
        IRepository<Agent, Guid> agentRepository,
        CliRunSignalHub signalHub,
        CliRunCancellationRegistry cancellationRegistry,
        IOptionsMonitor<CliAgentOptions> options,
        IServiceProvider serviceProvider,
        IBudgetService? budgetService = null,
        IUsageLimitService? usageLimits = null,
        ICliProviderRegistry? providerRegistry = null,
        IAgentAttachmentService? attachmentService = null,
        IRepository<AgentThreadMessage, Guid>? threadMessageRepository = null,
        IOptionsMonitor<AiChatOptions>? chatOptions = null,
        IUsagePolicyService? usagePolicy = null,
        ICliRunPlacementResolver? placementResolver = null,
        CliRunQueueSignal? queueSignal = null,
        IRepository<CliRunPermissionRequest, Guid>? permissionRepository = null)
        : base(serviceProvider)
    {
        _runRepository = Check.NotNull(runRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _bindingRepository = Check.NotNull(bindingRepository);
        _runtimeRepository = Check.NotNull(runtimeRepository);
        _threadRepository = Check.NotNull(threadRepository);
        _agentRepository = Check.NotNull(agentRepository);
        _signalHub = Check.NotNull(signalHub);
        _cancellationRegistry = Check.NotNull(cancellationRegistry);
        _options = Check.NotNull(options);
        _budgetService = budgetService;
        _usageLimits = usageLimits;
        // 下面四个缺省只为直接构造（测试）：附件与用户消息关联在那里一律按「做不到」拒绝，不会被静默跳过
        _providerRegistry = providerRegistry;
        _attachmentService = attachmentService;
        _threadMessageRepository = threadMessageRepository;
        _chatOptions = chatOptions;
        // 缺省只为直接构造（测试）：那里请求带的模型一律不采纳，而不是不经校验交给 CLI
        _usagePolicy = usagePolicy;
        // 缺省：一切按绑定（没有使用方的落点解析器）
        _placementResolver = placementResolver;
        _queueSignal = queueSignal;
        // 缺省只为直接构造（测试）：那里回答权限请求一律 501
        _permissionRepository = permissionRepository;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> EnqueueAsync(CliRunRequestDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (!_options.CurrentValue.Enabled)
        {
            return Fail<Guid>(
                "External CLI agent execution is disabled (AI:Cli:Enabled=false).", 501, ErrorCodes.CliDisabled);
        }

        var binding = await _bindingRepository.FirstOrDefaultAsync(b => b.AgentId == request.AgentId, cancellationToken);
        if (binding is null)
        {
            return Fail<Guid>(
                "The agent has no external CLI runtime binding.", 404, ErrorCodes.CliBindingNotFound);
        }

        // 没有落点解析器时运行时就是绑定的那个，照旧在这里就拦下不可用的；有解析器时等它给出落点再判
        Entities.CliRuntime? runtime = null;
        if (_placementResolver is null)
        {
            runtime = await FindUsableRuntimeAsync(binding.CliRuntimeId, cancellationToken);
            if (runtime is null)
            {
                return Fail<Guid>(
                    "The bound external CLI runtime is unavailable.", 409, ErrorCodes.CliRuntimeNotFound);
            }
        }

        // 停用开关与内建路径同一口径（AgentResolver）：外部执行不进管线，不在这里拦，
        // 管理员按下的「停用」对绑了运行时的 Agent 就不生效，而那恰恰是更贵的那条路。
        // 绑定比 Agent 活得久（Agent 已删、绑定行还在）同样当场拒绝，不排进队列再失败。
        var agent = await _agentRepository.GetAsync(request.AgentId, cancellationToken);
        if (agent is null)
        {
            return Fail<Guid>("Agent not found.", 404, ErrorCodes.AgentNotFound);
        }

        if (!agent.IsEnabled)
        {
            return Fail<Guid>("The agent is disabled.", 400, ErrorCodes.AgentDisabled);
        }

        var currentUserId = CurrentUser?.Id;
        if (request.ThreadId is { } requestedThreadId
            && !await OwnsThreadAsync(requestedThreadId, request.AgentId, currentUserId, cancellationToken))
        {
            return Fail<Guid>("Thread not found.", 404, ErrorCodes.ThreadNotFound);
        }

        if (request.UserMessageId is { } userMessageId
            && !await IsUserMessageOfThreadAsync(userMessageId, request.ThreadId, cancellationToken))
        {
            return Fail<Guid>("The user message does not belong to the thread.", 400, ErrorCodes.InvalidContent);
        }

        CliRunPlacement? placement = null;
        if (_placementResolver is not null)
        {
            placement = await _placementResolver.ResolveAsync(
                await BuildPlacementRequestAsync(request, binding, currentUserId, cancellationToken), cancellationToken);

            var placed = await ResolvePlacedRuntimeAsync(placement, binding, cancellationToken);
            if (!placed.Succeeded)
            {
                return Fail<Guid>(placed.Message!, placed.Code ?? 409, placed.ErrorCode);
            }

            runtime = placed.Data!;
        }

        var providerKey = runtime!.ProviderKey;
        if (UnsupportedVersion(runtime) is { } versionRejection)
        {
            return Fail<Guid>(versionRejection, 409, ErrorCodes.CliRuntimeVersionUnsupported);
        }

        var attachments = ReadAttachments(request, providerKey);
        if (!attachments.Succeeded)
        {
            return Fail<Guid>(attachments.Message!, attachments.Code ?? 400, attachments.ErrorCode);
        }

        // 排队前先问预算：让调用方当场知道，而不是排完队再失败。
        // 这不是唯一的门 —— 队列可能积压很久，真正的执法在认领之后（见 CliRunExecutor）。
        var budget = await CheckBudgetAsync(request, cancellationToken);
        if (budget is { IsAllowed: false })
        {
            return Fail<Guid>(
                budget.Reason ?? "The AI cost budget is exhausted.", 402, ErrorCodes.CliBudgetExceeded);
        }

        // 按用户的用量上限同理：入队这一次只查不开窗口（让调用方当场知道），开窗口与结算在执行器里。
        var usageRejection = await CheckUsageLimitAsync(request, cancellationToken);
        if (usageRejection is not null)
        {
            return usageRejection;
        }

        // 落点给了模型就是使用方的决定；没给时请求里的模型照常按允许列表校验
        var model = !string.IsNullOrWhiteSpace(placement?.Model)
            ? Ok<string?>(placement.Model.Trim())
            : await ResolveRequestedModelAsync(request, cancellationToken);
        if (!model.Succeeded)
        {
            return Fail<Guid>(model.Message!, model.Code ?? 403, model.ErrorCode ?? ErrorCodes.ModelNotAllowed);
        }

        // 续接指针（同一线程上一轮的 CLI 会话）不在这里定：执行器认领之后、线程已经串行化时才解析
        //（见 CliRunExecutor.ResolveResumePointerAsync）。入队这一刻前一轮可能还在排队或执行，它的会话还不存在。
        var trustedScheduler = await IsTrustedSchedulerAsync(request);

        var run = new CliRun
        {
            Id = SequentialGuid.NewGuid(),
            AgentId = request.AgentId,
            CliRuntimeId = runtime.Id,
            AgentRunId = trustedScheduler ? request.AgentRunId : null,
            ThreadId = request.ThreadId,
            UserMessageId = request.UserMessageId,
            Status = CliRunStatus.Queued,
            Priority = trustedScheduler ? request.Priority : 0,
            Prompt = request.Prompt ?? string.Empty,
            PerTurnContext = request.PerTurnContext,
            Model = model.Data,
            AssignedWorkDirectory = string.IsNullOrWhiteSpace(placement?.WorkDirectory) ? null : placement.WorkDirectory.Trim(),
            CreateAssignedWorkDirectory = !string.IsNullOrWhiteSpace(placement?.WorkDirectory) && placement.CreateWorkDirectory,
            Attended = request.Attended ?? false
        };

        // 附件先于排队行落库：排队行一旦提交就可能立刻被认领，执行器按运行 id 取附件，那时它们必须已经在了
        await StoreAttachmentsAsync(attachments.Data!, run, request.UserId ?? currentUserId, cancellationToken);

        await _runRepository.InsertAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);

        Logger.LogInformation(
            "Enqueued CLI run {RunId} for agent {AgentId} (thread={ThreadId}, runtime={RuntimeId} on {Target})",
            run.Id, request.AgentId, request.ThreadId, runtime.Id, runtime.HostId);

        // 远程执行目标：叫醒本进程的队列循环，好让它马上去唤起不在线的目标（或者认领已在线的），不再等一个轮询间隔
        if (runtime.Mode == CliRuntimeMode.RemoteDaemon)
        {
            _queueSignal?.Nudge();
        }

        return Ok(run.Id);
    }

    /// <summary>一个存在且没被停用的运行时；否则 null。</summary>
    private Task<Entities.CliRuntime?> FindUsableRuntimeAsync(Guid runtimeId, CancellationToken cancellationToken)
        => _runtimeRepository.AsQueryable()
            .FirstOrDefaultAsync(r => r.Id == runtimeId && r.Status != CliRuntimeStatus.Disabled, cancellationToken);

    /// <summary>交给落点解析器的这一轮：身份与这次入队用的同一套口径（显式 <c>UserId</c> 优先于环境上下文）。</summary>
    private async Task<CliRunPlacementRequest> BuildPlacementRequestAsync(
        CliRunRequestDto request, CliAgentBinding binding, Guid? currentUserId, CancellationToken cancellationToken)
    {
        var bindingProviderKey = await _runtimeRepository.AsQueryable()
            .Where(r => r.Id == binding.CliRuntimeId)
            .Select(r => r.ProviderKey)
            .FirstOrDefaultAsync(cancellationToken);

        return new CliRunPlacementRequest
        {
            AgentId = request.AgentId,
            ThreadId = request.ThreadId,
            UserMessageId = request.UserMessageId,
            UserId = request.UserId ?? currentUserId,
            TenantId = ServiceProvider.GetService<ICurrentTenant>()?.Id ?? CurrentUser?.TenantId,
            BindingRuntimeId = binding.CliRuntimeId,
            BindingProviderKey = bindingProviderKey ?? string.Empty,
            RequestedModel = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim()
        };
    }

    /// <summary>
    /// 落点指向的运行时：一个已登记的（<see cref="CliRunPlacement.RuntimeId"/>），或一个远程执行目标上的某家 CLI（没登记过就以离线状态建出来）；
    /// 都没给就是绑定的。不存在或已停用答 409。
    /// </summary>
    /// <remarks>落点自相矛盾（既给运行时又给目标、只给 provider 不给目标、目标标识不合法）是使用方代码的错误，答 500。</remarks>
    private async Task<Result<Entities.CliRuntime>> ResolvePlacedRuntimeAsync(
        CliRunPlacement? placement, CliAgentBinding binding, CancellationToken cancellationToken)
    {
        if (placement?.TargetId is { } target && !string.IsNullOrWhiteSpace(target))
        {
            if (placement.RuntimeId is not null)
            {
                return Fail<Entities.CliRuntime>(
                    "The run placement names both a runtime and an execution target.", 500, ErrorCodes.InternalError);
            }

            string targetId;
            try
            {
                targetId = CliRemoteRuntimeRegistry.ValidateTargetId(target);
            }
            catch (ArgumentException ex)
            {
                return Fail<Entities.CliRuntime>(ex.Message, 500, ErrorCodes.InternalError);
            }

            var providerKey = !string.IsNullOrWhiteSpace(placement.ProviderKey)
                ? placement.ProviderKey.Trim()
                : await _runtimeRepository.AsQueryable()
                    .Where(r => r.Id == binding.CliRuntimeId)
                    .Select(r => r.ProviderKey)
                    .FirstOrDefaultAsync(cancellationToken);
            if (providerKey is null || _providerRegistry?.Find(providerKey) is not { Enabled: true } provider)
            {
                return Fail<Entities.CliRuntime>(
                    $"Provider '{providerKey}' is unknown or disabled in this deployment.", 409, ErrorCodes.CliProviderNotAvailable);
            }

            var placed = await CliRemoteRuntimeRegistry.FindOrCreateAsync(
                _runtimeRepository, targetId, provider, _options.CurrentValue.MaxConcurrentRuns, cancellationToken);
            return placed.Status == CliRuntimeStatus.Disabled
                ? Fail<Entities.CliRuntime>(
                    "The external CLI runtime on this execution target has been disabled by an administrator.", 409, ErrorCodes.CliRuntimeNotFound)
                : Ok(placed);
        }

        if (!string.IsNullOrWhiteSpace(placement?.ProviderKey))
        {
            return Fail<Entities.CliRuntime>(
                "The run placement names a provider without an execution target; name a runtime instead.", 500, ErrorCodes.InternalError);
        }

        var runtime = await FindUsableRuntimeAsync(placement?.RuntimeId ?? binding.CliRuntimeId, cancellationToken);
        return runtime is null
            ? Fail<Entities.CliRuntime>("The bound external CLI runtime is unavailable.", 409, ErrorCodes.CliRuntimeNotFound)
            : Ok(runtime);
    }

    /// <summary>
    /// 运行时登记的 CLI 版本不在部署声明的范围内时的原因：入队就拒绝，让调用方当场知道（执行器认领后再判一次，版本可能在排队期间换了）。
    /// 还没登记过的远程运行时（离线占位、版本未知）不在这里判，等目标上线登记了版本由执行器判。
    /// </summary>
    private string? UnsupportedVersion(Entities.CliRuntime runtime)
    {
        if (runtime.Mode == CliRuntimeMode.RemoteDaemon && string.IsNullOrWhiteSpace(runtime.CliVersion))
        {
            return null;
        }

        return _providerRegistry?.Find(runtime.ProviderKey) is { } provider
            ? CliVersionSupport.GetRejection(provider, runtime.CliVersion, runtime.HostId)
            : null;
    }

    /// <summary>
    /// 读出并校验这次运行要交给 CLI 的附件：类型、大小、条数按 <c>AI:Chat:Attachments</c>，
    /// 并且绑定的 provider 得收得了附件。任何一项不合规都整体拒绝，绝不只发文本。
    /// </summary>
    private Result<IReadOnlyList<ChatAttachmentPayload>> ReadAttachments(CliRunRequestDto request, string providerKey)
    {
        var limits = _chatOptions?.CurrentValue.Attachments ?? new ChatAttachmentOptions();
        var attachments = ChatAttachmentRules.Read(request.Attachments, limits);
        if (!attachments.Succeeded || attachments.Data is not { Count: > 0 })
        {
            return attachments;
        }

        if (_providerRegistry?.Find(providerKey) is not { AcceptsAttachments: true })
        {
            return Result<IReadOnlyList<ChatAttachmentPayload>>.Failure(
                $"This agent runs on {providerKey}, which cannot receive attachments.", 415, ErrorCodes.UnsupportedMediaType);
        }

        if (_attachmentService is null)
        {
            return Result<IReadOnlyList<ChatAttachmentPayload>>.Failure(
                "Attachments cannot be stored in this deployment.", 501, ErrorCodes.UnsupportedMediaType);
        }

        return attachments;
    }

    private async Task StoreAttachmentsAsync(
        IReadOnlyList<ChatAttachmentPayload> attachments, CliRun run, Guid? ownerId, CancellationToken cancellationToken)
    {
        if (attachments.Count == 0)
        {
            return;
        }

        await _attachmentService!.SaveAsync(attachments.Select(a => new NewAgentAttachment
        {
            Origin = AttachmentOrigin.Upload,
            MediaType = a.MediaType,
            FileName = a.FileName,
            Content = a.Content,
            ThreadId = run.ThreadId,
            MessageId = run.UserMessageId,
            RunId = run.Id,
            OwnerId = ownerId
        }).ToList(), cancellationToken);
    }

    /// <summary>
    /// 调用方说「这次运行回答的是这条用户消息」时，它必须真是这条（已核过归属的）线程里的用户消息：
    /// 附件随它显示、回复写在它后面、编辑它时会话从它之前接着走，一个随手填的 id 会把附件挂到别人的对话里。
    /// </summary>
    private async Task<bool> IsUserMessageOfThreadAsync(Guid messageId, Guid? threadId, CancellationToken cancellationToken)
    {
        if (threadId is not { } thread || _threadMessageRepository is null)
        {
            return false;
        }

        return await _threadMessageRepository.AsQueryable()
            .AnyAsync(m => m.Id == messageId && m.ThreadId == thread && m.Role == MessageRole.User, cancellationToken);
    }

    /// <summary>
    /// 调用方给的调度字段（队列优先级、关联的 <c>AgentRunId</c>）这次能不能采纳。
    /// </summary>
    /// <remarks>
    /// 队列只有一个，所有租户共用，认领按优先级大者先。优先级因此是运维面的调度旋钮，不是用户的输入：
    /// 此前用户端请求体里的值原样落库，任何持有 <c>ai.agent.execute</c> 的用户带上 <c>int.MaxValue</c>，
    /// 他的运行就永远排在所有租户、所有人之前。<c>AgentRunId</c> 同理：它是这次外部执行挂在哪条框架
    /// <c>AgentRun</c> 名下的关联键，由门面这类进程内调用方填写，用户自填一个就能把运行挂到别人的记录下。
    /// 判定放在服务层而不是控制器：控制器是可被整体替换的默认控制器。
    /// <para>
    /// 采纳只有两种情形：没有登录用户的进程内调用方（后台任务、父 agent 派子任务，值是代码写的），
    /// 以及持有 <see cref="CliPermissions.CliRunExecute"/>（控制外部执行）的调用方。其余一律丢弃（优先级按 0 排队）。
    /// 两个字段都没给时不必查权限。门面在用户请求的作用域里代用户入队时同样按此判定；
    /// 今天带 <c>AgentRunId</c>（即 <c>AgentRunRequest.ExistingRunId</c>）的只有没有登录用户的后台派发。
    /// </para>
    /// </remarks>
    private async Task<bool> IsTrustedSchedulerAsync(CliRunRequestDto request)
    {
        if ((request.Priority == 0 && request.AgentRunId is null) || CurrentUser?.Id is null)
        {
            return true;
        }

        var checker = PermissionChecker;
        return checker is not null && await checker.IsGrantedAsync(CliPermissions.CliRunExecute);
    }

    /// <summary>
    /// 查预算。返回 null 表示这个部署没有预算服务，也就没有预算可超。
    /// </summary>
    /// <remarks>
    /// 外部执行按红线①不进中间件管线，因此内建路径上那道长在 <c>QuotaMiddleware</c> 里的预算门
    /// 对它不生效，必须在这里显式再设一道。缺了这道门，外部执行域就是绕过预算的免费通道，
    /// 而它恰恰是两条路里更贵的那条。
    /// <para>
    /// 服务缺失时放行：<c>IBudgetService</c> 由 <c>AIModule</c> 注册，正常情况下必然存在；
    /// 真的没有它，就是宿主根本没装预算能力，此时「允许」是正确答案而不是降级。
    /// 预算本身关着（<c>AI:Budget:Enabled=false</c>，默认）时该服务恒返回允许。
    /// </para>
    /// <para>
    /// ★ <b>归属口径必须与内建路径一致</b>：<c>QuotaMiddleware</c> 按
    /// <c>context.Request.UserId</c>（即 <c>AgentRunRequest.UserId</c>）算，而
    /// <c>AgentDispatchFacade</c> 把同一个值原样传进了 <c>CliRunRequestDto.UserId</c>。
    /// 这里若只读环境上下文里的 <c>CurrentUser</c>，那么任何「代某人执行」的场景
    /// （后台派发、父 agent 派子任务）在外部路径上都会算到另一个人（或没人）头上 ——
    /// 而外部路径恰恰是两条里更贵的那条。今天两者取值相同，所以这是<b>潜在</b>而非
    /// 已发生的偏差；写成显式优先级是为了它不要在某天悄悄成立。
    /// </para>
    /// </remarks>
    private async Task<BudgetCheckResult?> CheckBudgetAsync(CliRunRequestDto request, CancellationToken cancellationToken)
    {
        if (_budgetService is null) return null;

        var currentUser = CurrentUser;
        var tenantId = ServiceProvider.GetService<ICurrentTenant>()?.Id ?? currentUser?.TenantId;
        var userId = request.UserId ?? currentUser?.Id;

        return await _budgetService.CheckBudgetAsync(userId, tenantId, request.AgentId, cancellationToken);
    }

    /// <summary>
    /// 入队前查按用户的用量上限（只查不开窗口）。放行返回 null，否则返回要交给调用方的失败结果。
    /// </summary>
    /// <remarks>
    /// 与预算门同一个理由：内建路径上用量上限长在 <c>QuotaMiddleware</c> 里，外部执行不进管线就不受它管辖，
    /// 一个已经到上限的用户换一个绑了 CLI 运行时的 Agent 就能接着用，且用量从不计入他的窗口。
    /// 这里只是让直接调调度器的调用方当场收到反馈（聊天路径在门面里已经查过，在写下用户消息之前）；
    /// 开窗口与结算在执行器认领之后，理由同预算门，队列可能积压很久。归属口径与预算门相同
    /// （显式 <c>UserId</c> 优先于环境上下文），无用户时跳过，与 <c>QuotaMiddleware</c> 一致。
    /// 拒绝是 429 <c>AI_USAGE_LIMIT_REACHED</c>，错误详情是 <see cref="UsageLimitDenialDto"/>（哪个窗口、什么时候重置）。
    /// </remarks>
    /// <summary>
    /// 这一轮交给 CLI 的模型（<c>--model</c>）。请求体里的模型与聊天入口同一口径：没配模型清单（<c>AI:Chat:Models</c>）时不采纳，
    /// 配了就必须是这个用户在这个 Agent 上被允许的模型之一，否则 403 <c>AI_MODEL_NOT_ALLOWED</c>。
    /// </summary>
    /// <remarks>
    /// 用户端入队把请求体原样交到这里，不校验的话持有 <c>ai.agent.execute</c> 的用户点名哪个模型 CLI 就用哪个，允许列表形同虚设。
    /// 受信调度者（持 <c>ai.cliRun.execute</c>，或没有登录用户的进程内调用）原样采纳，与 Priority / AgentRunId 同一规则。
    /// 聊天门面交来的模型已在入口校验过，这里再判一次结果相同。
    /// </remarks>
    private async Task<Result<string?>> ResolveRequestedModelAsync(CliRunRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return Ok<string?>(null);
        }

        var requested = request.Model.Trim();
        var currentUserId = CurrentUser?.Id;
        if (currentUserId is null
            || (PermissionChecker is { } checker && await checker.IsGrantedAsync(CliPermissions.CliRunExecute)))
        {
            return Ok<string?>(requested);
        }

        if (_chatOptions?.CurrentValue.Models is not { Count: > 0 } || _usagePolicy is null)
        {
            Logger.LogInformation(
                "Ignored model {Model} requested for agent {AgentId}: no model list is configured (AI:Chat:Models), so the binding's model is used",
                requested, request.AgentId);
            return Ok<string?>(null);
        }

        var resolved = await _usagePolicy.ResolveChatModelAsync(
            request.UserId ?? currentUserId, request.AgentId, null, requested, cancellationToken);
        return resolved.Succeeded
            ? Ok<string?>(resolved.Data)
            : Fail<string?>(
                resolved.Message ?? $"The model '{requested}' is not available to you for this assistant.",
                resolved.Code ?? 403,
                resolved.ErrorCode ?? ErrorCodes.ModelNotAllowed);
    }

    private async Task<Result<Guid>?> CheckUsageLimitAsync(CliRunRequestDto request, CancellationToken cancellationToken)
    {
        if (_usageLimits is null) return null;

        var userId = request.UserId ?? CurrentUser?.Id;
        if (userId is null) return null;

        var denial = await _usageLimits.CheckAsync(userId.Value, cancellationToken);
        if (denial is null)
        {
            return null;
        }

        var window = denial.Window == UsageLimitWindow.Session ? "5-hour" : "weekly";
        var message = denial.ResetsAt is { } resetsAt
            ? $"You have reached your {window} usage limit. It resets at {resetsAt:yyyy-MM-dd HH:mm} UTC."
            : $"You have reached your {window} usage limit.";
        return Fail<Guid>(message, 429, ErrorCodes.UsageLimitReached, denial);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CliAgentEvent> StreamAsync(
        Guid runId, int fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // ★ 订阅同样要过归属判定。这里只能 yield break 而不是回 404 ——
        // 控制器在开始迭代之前已经写了 SSE 响应头。不可见与「运行不存在」表现一致
        // （都是一条事件都收不到），所以探测者仍然分不出这个 id 是不是真的。
        var subject = await FindRunAsync(runId, cancellationToken);
        if (subject is null || !await CanSeeAsync(subject))
        {
            yield break;
        }

        var lastSequence = fromSequence;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _messageRepository.AsQueryable()
                .Where(m => m.RunId == runId && m.Sequence > lastSequence)
                .OrderBy(m => m.Sequence)
                .Take(200)
                .ToListAsync(cancellationToken);

            foreach (var message in batch)
            {
                lastSequence = message.Sequence;
                yield return CliRunMessageConverter.ToEvent(message);
            }

            // 还有整批要补，先接着补完再谈等待。
            if (batch.Count == 200)
            {
                continue;
            }

            var status = await _runRepository.AsQueryable()
                .Where(r => r.Id == runId)
                .Select(r => (CliRunStatus?)r.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (status is null)
            {
                yield break;
            }

            if (IsTerminal(status.Value))
            {
                // 终态之后再查一次：最后几条事件可能在上一次查询与状态落库之间写入。
                var tail = await _messageRepository.AsQueryable()
                    .Where(m => m.RunId == runId && m.Sequence > lastSequence)
                    .OrderBy(m => m.Sequence)
                    .ToListAsync(cancellationToken);

                foreach (var message in tail)
                {
                    yield return CliRunMessageConverter.ToEvent(message);
                }

                yield break;
            }

            // 等信号或等超时都行 —— 信号只是提示，超时后重新查库同样正确
            //（多副本下执行者的信号根本到不了这个进程）。
            await _signalHub.WaitAsync(runId, _options.CurrentValue.PollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// 现读一条运行（不跟踪）。
    /// </summary>
    /// <remarks>
    /// ★ 不能用 <c>GetAsync</c>：它是 <c>DbSet.Find</c>，先查本作用域的身份映射。订阅一条运行时开头读过一次，
    /// 作用域就一直跟踪着那份实体；流结束后再取，拿回的还是开头那份：状态停在 Queued / Running，没有交付物、
    /// 没有回复消息 id。等着它的门面据此给出的终止块是「正常结束、没有回复」，而运行其实早已写下了结局。
    /// </remarks>
    private Task<CliRun?> FindRunAsync(Guid runId, CancellationToken cancellationToken)
        => _runRepository.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    /// <summary>
    /// 这一条运行该不该让当前调用者看到。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>为什么判定必须在这里而不在控制器里</b>：用户端与管理端两个控制器调用的是
    /// <b>同一批</b>方法（<c>Get</c>/<c>Stream</c>/<c>GetMessages</c>/<c>Cancel</c> 逐个对应），
    /// 而它们都是 <c>[DefaultController]</c>、消费应用可整体替换。把判定放控制器 =
    /// 换一个控制器就没有了。
    /// </para>
    /// <para>
    /// 判据沿 AI 模块既有的 house 模式（<c>AgentThreadService</c> / <c>MessageFeedbackService</c> /
    /// <c>AgentArtifactService</c> 一律用 <c>CreatorId == 当前用户</c>）：
    /// </para>
    /// <list type="number">
    /// <item>是自己派出的（<c>CreatorId</c> 匹配）→ 放行。</item>
    /// <item>否则要有管理端查看码 <c>ai.cliRun.view</c> → 管理台照常看全部。</item>
    /// </list>
    /// <para>
    /// ★ <c>CreatorId</c> 为空的运行（后台/系统派出）对<b>任何</b>已认证用户都不可见，
    /// 只有管理端能看 —— 与 <c>AgentThreadService</c> 对无主线程的处理逐字一致，
    /// 也是唯一安全的缺省方向。
    /// </para>
    /// <para>
    /// ★ 拒绝一律按 <b>404</b> 出（框架铁律）：把「不存在」与「不是你的」区分开，
    /// 等于告诉试探者哪些 id 是真的。
    /// </para>
    /// </remarks>
    private async Task<bool> CanSeeAsync(CliRun run)
    {
        var currentUserId = CurrentUser?.Id;
        if (currentUserId.HasValue && run.CreatorId == currentUserId)
        {
            return true;
        }

        var checker = PermissionChecker;
        return checker is not null && await checker.IsGrantedAsync(CliPermissions.CliRunView);
    }

    /// <summary>
    /// 调用方给的 ThreadId 是不是它自己的、绑在这个 Agent 上的线程。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>为什么这里必须自己核，而不是信门面</b>：<c>EnqueueAsync</c> 是用户端控制器
    /// （<c>POST ai/cli-runs</c>）直接调用的入口，请求体里的 ThreadId 原样进来。在补上这一条之前，
    /// 任何持有 <c>ai.agent.execute</c> 的用户只要拿到别人的线程 id，派出的运行就会
    /// <b>续接受害者的 CLI 会话</b>（<c>--resume</c> 受害者的 ProviderSessionId）、
    /// <b>跑在受害者的每线程工作区里</b>（PerThread 模式按 ThreadId 定目录），
    /// 而运行的 <c>CreatorId</c> 是攻击者自己，所以输出他能原样读走。
    /// 与 <c>a6f0bc93</c>（AI 客户端可指定任意 ThreadId）是同一形态，只是换到了外部路径。
    /// </para>
    /// <para>
    /// 判据逐字沿 <c>AgentThreadService.GetOrCreateThreadAsync</c>：线程存在、<c>AgentId</c> 相符、
    /// 已认证用户还要 <c>CreatorId</c> 相符（无主线程对任何已认证用户都不可用）。
    /// 没有当前用户（后台派发、IM 网关）时不做归属判定，与内建路径一致。
    /// 拒绝一律按 404 出：区分「不存在」与「不是你的」等于告诉试探者哪些 id 是真的。
    /// </para>
    /// </remarks>
    private async Task<bool> OwnsThreadAsync(Guid threadId, Guid agentId, Guid? currentUserId, CancellationToken cancellationToken)
    {
        var thread = await _threadRepository.AsQueryable()
            .Where(t => t.Id == threadId)
            .Select(t => new { t.AgentId, t.CreatorId })
            .FirstOrDefaultAsync(cancellationToken);

        if (thread is null || thread.AgentId != agentId)
        {
            Logger.LogWarning("CLI run rejected: thread {ThreadId} is missing or not bound to agent {AgentId}", threadId, agentId);
            return false;
        }

        if (currentUserId.HasValue && thread.CreatorId != currentUserId)
        {
            Logger.LogWarning(
                "CLI run rejected: thread {ThreadId} belongs to {CreatorId}, not to {CurrentUserId}",
                threadId, thread.CreatorId, currentUserId);
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<Result<CliRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await FindRunAsync(runId, cancellationToken);
        if (run is null || !await CanSeeAsync(run))
        {
            return Fail<CliRunDto>("CLI run not found.", 404, ErrorCodes.CliRunNotFound);
        }

        var dto = run.MapTo<CliRunDto>();
        dto.ProviderKey = await _runtimeRepository.AsQueryable()
            .Where(r => r.Id == run.CliRuntimeId)
            .Select(r => r.ProviderKey)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(dto);
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<CliRunDto>>> GetListAsync(
        CliRunQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        // 列表今天只挂在管理端控制器上，但把范围收紧写在服务里而不是「靠调用方只从管理端调」：
        // 控制器可被消费应用整体替换，也随时可能长出一个用户端「我的运行」列表。
        // 无管理码 → 只看自己派出的；CreatorId 为空的（后台派出）对任何用户都不出现。
        var checker = PermissionChecker;
        var canSeeAll = checker is not null && await checker.IsGrantedAsync(CliPermissions.CliRunView);
        var currentUserId = CurrentUser?.Id;

        var queryable = _runRepository
            .WhereIf(r => r.CreatorId == currentUserId && currentUserId != null, !canSeeAll)
            .WhereIf(r => r.AgentId == query.AgentId, query.AgentId.HasValue)
            .WhereIf(r => r.CliRuntimeId == query.CliRuntimeId, query.CliRuntimeId.HasValue)
            .WhereIf(r => r.Status == query.Status!.Value, query.Status.HasValue)
            .WhereIf(r => r.ThreadId == query.ThreadId, query.ThreadId.HasValue)
            .WhereIf(r => r.Status == CliRunStatus.Queued || r.Status == CliRunStatus.Dispatched || r.Status == CliRunStatus.Running,
                query.ActiveOnly)
            .WhereIf(r => r.CreationTime >= query.StartTime!.Value, query.StartTime.HasValue)
            .WhereIf(r => r.CreationTime <= query.EndTime!.Value, query.EndTime.HasValue)
            .OrderByDescending(r => r.CreationTime);

        var paged = await queryable.ProjectTo<CliRun, CliRunDto>().CreateAsync(query);
        return Ok(paged);
    }

    /// <inheritdoc />
    public async Task<Result<List<CliRunMessageDto>>> GetMessagesAsync(
        Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default)
    {
        var run = await FindRunAsync(runId, cancellationToken);
        if (run is null || !await CanSeeAsync(run))
        {
            return Fail<List<CliRunMessageDto>>("CLI run not found.", 404, ErrorCodes.CliRunNotFound);
        }

        var rows = await _messageRepository.AsQueryable()
            .Where(m => m.RunId == runId && m.Sequence > fromSequence)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);

        var messages = rows.Select(m =>
        {
            var dto = m.MapTo<CliRunMessageDto>();
            dto.OutputBlocks = CliRunMessageConverter.DeserializeBlocks(m.OutputBlocksJson)?.ToList();
            dto.Permission = CliRunMessageConverter.DeserializePermission(m.PermissionJson);
            return dto;
        }).ToList();

        return Ok(messages);
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await FindRunAsync(runId, cancellationToken);
        if (run is null || !await CanSeeAsync(run))
        {
            return Fail("CLI run not found.", 404, ErrorCodes.CliRunNotFound);
        }

        if (IsTerminal(run.Status))
        {
            return Fail("The CLI run has already finished.", 409, ErrorCodes.CliRunInvalidState);
        }

        await RequestCancellationAsync(run, acrossTenants: false, reason: null, cancellationToken);
        return Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 与 <see cref="CancelAsync"/> 共用同一段取消实现（<see cref="RequestCancellationAsync"/>），差别只有两处：
    /// 不判定可见性，以及按 Id <b>跨租户</b>定位与写入：调用方是后台任务，当前租户恒为 null，
    /// 多租户开着时带租户过滤的查询看不见任何租户的运行，于是每条都会被答成 404。
    /// </remarks>
    public async Task<Result> CancelAsSystemAsync(Guid runId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.AcrossTenants().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return Fail("CLI run not found.", 404, ErrorCodes.CliRunNotFound);
        }

        if (IsTerminal(run.Status))
        {
            return Fail("The CLI run has already finished.", 409, ErrorCodes.CliRunInvalidState);
        }

        await RequestCancellationAsync(run, acrossTenants: true, NormalizeReason(reason), cancellationToken);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<int>> CancelThreadRunsAsSystemAsync(
        Guid threadId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var active = await _runRepository.AcrossTenants()
            .Where(r => r.ThreadId == threadId
                        && (r.Status == CliRunStatus.Queued
                            || r.Status == CliRunStatus.Dispatched
                            || r.Status == CliRunStatus.Running))
            .ToListAsync(cancellationToken);

        var normalized = NormalizeReason(reason);
        foreach (var run in active)
        {
            await RequestCancellationAsync(run, acrossTenants: true, normalized, cancellationToken);
        }

        return Ok(active.Count);
    }

    /// <summary>
    /// 要求一条还没结束的运行停下。用户取消与系统取消共用这一段，规则因此只有一份。
    /// </summary>
    /// <param name="run">已经定位到的运行（调用方已判过可见性与是否已结束）。</param>
    /// <param name="acrossTenants">
    /// 写入是否摘掉租户过滤器。用户路径保持带过滤的查询（它定位运行时用的也是那个口径）；
    /// 系统路径没有当前租户，带过滤的写入会安静地匹配 0 行，而调用方得到的是「已取消」。
    /// </param>
    /// <param name="reason">系统取消的原因；null = 不写，保留运行上已有的错误信息或交给终态写通用说明。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task RequestCancellationAsync(
        CliRun run, bool acrossTenants, string? reason, CancellationToken cancellationToken)
    {
        var runId = run.Id;
        var target = (acrossTenants ? _runRepository.AcrossTenants() : _runRepository.AsQueryable())
            .Where(r => r.Id == runId);

        // 两处都要写：数据库是跨副本的权威事实，进程内登记让<b>本副本正在跑的</b>那条
        // 立刻中止而不是等下一个续期周期 —— 一个正在烧预算的进程，多跑几秒都是钱。
        // 原因与取消标记写在同一条语句里：执行器落终态时保留已有的错误信息，结束事件因此带的是这个原因。
        if (reason is null)
        {
            await target.ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true), cancellationToken);
        }
        else
        {
            await target.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CancelRequested, true)
                .SetProperty(r => r.Error, r => r.Error ?? reason), cancellationToken);
        }

        var cancelledLocally = _cancellationRegistry.TryCancel(runId);

        if (run.Status == CliRunStatus.Queued)
        {
            // 还没被认领，直接判终态，不用等任何宿主来处理。以「仍是 Queued」为条件：这一刻被认领了就交给执行者，
            // 它在续租时看到取消标记自己落终态。终态经唯一入口写（它本就跨租户定位），结束事件由它发。
            var finalizer = new CliRunFinalizer(_runRepository, EventBus, Logger);
            await finalizer.FinishAsync(
                runId,
                new CliRunTermination(CliRunStatus.Cancelled, CliRunFailureReason.Cancelled, reason) { KeepExistingError = true },
                r => r.Status == CliRunStatus.Queued);
        }

        _signalHub.Signal(runId);

        Logger.LogInformation(
            "Cancellation requested for CLI run {RunId} ({Origin}, cancelled locally: {Local})",
            runId, acrossTenants ? "system" : "caller", cancelledLocally);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// 能回答的人：派出这次运行的人（<c>CreatorId</c>），或持 <c>ai.cliRun.execute</c> 的人（管理端代答）。只能看不能代答：
    /// 持 <c>ai.cliRun.view</c> 的管理员看得到请求，但允许 agent 在别人的工作区里跑命令是另一件事。判定在服务层，因为控制器可被整体替换。
    /// </para>
    /// <para>
    /// 回答以「还在等」为条件写下（与超时、别人的回答赛跑时先到者为准），执行这次运行的副本（不一定是这一个）轮询到之后写给 CLI。
    /// </para>
    /// </remarks>
    public async Task<Result> AnswerPermissionAsync(
        Guid runId, Guid requestId, CliPermissionAnswerDto answer, CancellationToken cancellationToken = default)
    {
        Check.NotNull(answer);

        if (_permissionRepository is null)
        {
            return Fail("This deployment does not relay permission requests.", 501, ErrorCodes.CliPermissionNotSupported);
        }

        var run = await FindRunAsync(runId, cancellationToken);
        if (run is null || !await CanAnswerAsync(run))
        {
            return Fail("CLI run not found.", 404, ErrorCodes.CliRunNotFound);
        }

        var request = await _permissionRepository.AsQueryable()
            .FirstOrDefaultAsync(r => r.Id == requestId && r.RunId == runId, cancellationToken);
        if (request is null)
        {
            return Fail("Permission request not found.", 404, ErrorCodes.CliPermissionRequestNotFound);
        }

        if (IsTerminal(run.Status) || request.Status != CliPermissionStatus.Pending)
        {
            return Fail("The permission request is no longer waiting for an answer.", 409, ErrorCodes.CliPermissionRequestClosed);
        }

        var option = ReadPermissionOptions(request.OptionsJson)
            .FirstOrDefault(o => string.Equals(o.Id, answer.OptionId, StringComparison.Ordinal));
        if (option is null)
        {
            return Fail("The answer must be one of the options the permission request offered.", 400, ErrorCodes.CliPermissionOptionInvalid);
        }

        var status = CliPermissionOptionKinds.IsAllow(option.Kind) ? CliPermissionStatus.Allowed : CliPermissionStatus.Denied;
        var message = status == CliPermissionStatus.Denied && !string.IsNullOrWhiteSpace(answer.Message)
            ? CliRunExecutor.Truncate(answer.Message.Trim(), MaxPermissionMessageLength)
            : null;
        var answeredBy = CurrentUser?.Id;
        var now = DateTime.UtcNow;

        await _permissionRepository.EnsureTransactionStartedAsync(cancellationToken);
        var written = await _permissionRepository.AsQueryable()
            .Where(r => r.Id == requestId && r.Status == CliPermissionStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.OptionId, option.Id)
                .SetProperty(r => r.Message, message)
                .SetProperty(r => r.AnsweredById, answeredBy)
                .SetProperty(r => r.AnsweredAt, (DateTime?)now), cancellationToken);
        if (written == 0)
        {
            return Fail("The permission request is no longer waiting for an answer.", 409, ErrorCodes.CliPermissionRequestClosed);
        }

        // 执行它的是本副本时立刻写给 CLI；别的副本在下一个轮询间隔（AI:Cli:Approval:PollInterval）读到
        _signalHub.Signal(runId);

        Logger.LogInformation(
            "Permission request {RequestId} of CLI run {RunId} answered: {Status} ({OptionId})", requestId, runId, status, option.Id);
        return Ok();
    }

    /// <summary>这条运行的权限请求该不该让当前调用者回答（见 <see cref="AnswerPermissionAsync"/>）。</summary>
    private async Task<bool> CanAnswerAsync(CliRun run)
    {
        var currentUserId = CurrentUser?.Id;
        if (currentUserId.HasValue && run.CreatorId == currentUserId)
        {
            return true;
        }

        var checker = PermissionChecker;
        return checker is not null && await checker.IsGrantedAsync(CliPermissions.CliRunExecute);
    }

    private static List<CliPermissionOption> ReadPermissionOptions(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CliPermissionOption>>(json, TnziJsonDefaults.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>系统取消的原因：去掉首尾空白，空串当作没给，过长截断（它进的是一行错误信息，不是日志）。</summary>
    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var trimmed = reason.Trim();
        return trimmed.Length <= MaxSystemCancelReasonLength ? trimmed : trimmed[..MaxSystemCancelReasonLength];
    }

    private static bool IsTerminal(CliRunStatus status)
        => status is CliRunStatus.Completed or CliRunStatus.Failed
            or CliRunStatus.Cancelled or CliRunStatus.TimedOut;

}
