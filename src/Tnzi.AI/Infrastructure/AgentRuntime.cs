namespace Tnzi.AI.Infrastructure;

/// <summary>
/// Unified AI execution entry point - all AI execution (chat, workflow, agent run) goes through this.
/// Composes middleware pipeline + execution strategy + run tracking.
/// </summary>
public class AgentRuntime : IAgentRuntime
{
    private readonly IAgentResolver _agentResolver;
    private readonly IAgentFactory _agentFactory;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly IRunTracker _runTracker;
    private readonly IWorkflowDelegator _workflowDelegator;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<AgentRuntime> _logger;
    private readonly Lazy<List<IAiMiddleware>> _middlewares;
    private readonly IAgentThreadService? _threadService;
    private readonly ISubAgentRunCancellationRegistry? _cancellationRegistry;
    private readonly IAgentRunHeartbeat? _runHeartbeat;
    private readonly IToolRegistry? _toolRegistry;
    private AiMiddlewareDelegate? _cachedPipelineDelegate;
    private AiStreamingMiddlewareDelegate? _cachedStreamingPipelineDelegate;

    public AgentRuntime(
        IAgentResolver agentResolver,
        IAgentFactory agentFactory,
        IRepository<Agent, Guid> agentRepository,
        IRunTracker runTracker,
        IWorkflowDelegator workflowDelegator,
        IAgentExecutionContextAccessor executionContextAccessor,
        IServiceProvider serviceProvider,
        IOptionsMonitor<AIOptions> aiOptions,
        IEventPublisher eventPublisher,
        ILogger<AgentRuntime> logger,
        IAgentThreadService? threadService = null,
        ISubAgentRunCancellationRegistry? cancellationRegistry = null,
        IAgentRunHeartbeat? runHeartbeat = null,
        IToolRegistry? toolRegistry = null)
    {
        _threadService = threadService;
        _toolRegistry = toolRegistry;
        _cancellationRegistry = cancellationRegistry;
        _runHeartbeat = runHeartbeat;
        _agentResolver = Check.NotNull(agentResolver);
        _agentFactory = Check.NotNull(agentFactory);
        _agentRepository = Check.NotNull(agentRepository);
        _runTracker = Check.NotNull(runTracker);
        _workflowDelegator = Check.NotNull(workflowDelegator);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _serviceProvider = Check.NotNull(serviceProvider);
        _aiOptions = Check.NotNull(aiOptions);
        _eventPublisher = Check.NotNull(eventPublisher);
        _logger = Check.NotNull(logger);
        _middlewares = new Lazy<List<IAiMiddleware>>(() =>
            _serviceProvider.GetServices<IAiMiddleware>().OrderBy(m => m.Order).ToList());
    }

    /// <summary>Execute an AI run (non-streaming).</summary>
    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var previousRequest = _executionContextAccessor.CurrentRequest;
        var previousProperties = previousRequest != null
            ? _executionContextAccessor.CaptureProperties()
            : null;
        EnterRun(request);

        try
        {
            if (request.WorkflowId.HasValue)
            {
                var workflowStopwatch = Stopwatch.StartNew();
                var workflowResult = AgentRunStatusResolver.EnsureStatus(
                    await _workflowDelegator.ExecuteWorkflowAsync(request, cancellationToken));
                workflowStopwatch.Stop();

                await _eventPublisher.PublishRunCompletedEventAsync(
                    request, workflowResult, null,
                    workflowStopwatch.ElapsedMilliseconds, false, "Workflow");

                return workflowResult;
            }

            var sw = Stopwatch.StartNew();
            var setup = await SetupContextAndResolveAsync(request, isStreaming: false, cancellationToken);
            if (!setup.Resolution.IsSuccess)
            {
                throw CreateAgentResolutionException(setup.Resolution);
            }

            var resolution = setup.Resolution;
            var run = setup.Run;
            var context = setup.Context;
            await using var heartbeat = StartHeartbeat(run);

            // Get (or build) middleware pipeline delegate
            _cachedPipelineDelegate ??= BuildPipelineDelegate();

            // Execute pipeline
            AgentRunResult result;
            try
            {
                result = await _cachedPipelineDelegate(context, cancellationToken);
                ApplyEffectiveInputSummary(run, context);
                if (run != null)
                {
                    result = AgentRunStatusResolver.EnsureStatus(result);
                    sw.Stop();
                    await _runTracker.UpdateRunOnCompletionAsync(run, result, sw.ElapsedMilliseconds, cancellationToken);

                    if (result.FinishReason == FinishReasons.MaxToolIterations)
                    {
                        _logger.LogWarning(
                            "Agent run {RunId} reached MaxToolIterations limit - response may be incomplete",
                            run.Id);
                    }
                }

                await _eventPublisher.PublishRunCompletedEventAsync(
                    request, result, run,
                    sw.ElapsedMilliseconds, false,
                    context.EffectiveProvider ?? resolution.Provider);

                // 标题取模型实际收到的用户文本（守卫改写之后、带内容块时取内容块）：标题会落库，也会交给 LLM
                var titleText = UserTurn.EffectiveText(context);
                if (context.IsNewThread
                    && request.ThreadId.HasValue
                    && !string.IsNullOrWhiteSpace(titleText)
                    && AgentRunStatusResolver.ShouldGenerateThreadTitle(result.FinishReason))
                {
                    await _eventPublisher.HandleNewThreadTitleAsync(request, result, titleText);
                }
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested
                && ResolveCancellation(run) is { Kind: RunCancellationKind.TimedOut } timedOut)
            {
                // 超时不是用户的决定，是运行没干完：按 Failed 收尾（可续跑），但 Error 要说清是超时，
                // 不能与 kill / 真失败共用一句 "The operation was canceled."
                sw.Stop();
                ApplyEffectiveInputSummary(run, context);
                var timeout = new TimeoutException(timedOut.Reason, ex);
                _logger.LogWarning("Agent run {RunId} timed out: {Reason}", run?.Id, timedOut.Reason);

                await _eventPublisher.PublishRunFailedEventAsync(request, run, timeout, sw.ElapsedMilliseconds, false);
                if (run != null)
                {
                    await _runTracker.UpdateRunOnFailureAsync(run, timeout, sw.ElapsedMilliseconds, CancellationToken.None);
                }

                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // kill_agent / 管理端 cancel 已把行写成 Cancelled 再触发 CTS；调用方自己的令牌（HTTP 断连）同理。
                // 此前落进下面的 catch (Exception)，Cancelled 被 UpdateRunOnFailureAsync 覆盖成 Failed 并发 RunFailed 事件，
                // 管理端看到 Failed / CanResume=true。流式路径早有 FinalizeStreamingCancelledAsync，这里是非流式的那一半。
                sw.Stop();
                ApplyEffectiveInputSummary(run, context);
                var reason = ResolveCancellation(run)?.Reason ?? "The run was cancelled by the caller";
                _logger.LogInformation("Agent run {RunId} cancelled: {Reason}", run?.Id, reason);

                await _eventPublisher.PublishRunCancelledEventAsync(request, run, reason, sw.ElapsedMilliseconds, false);
                if (run != null)
                {
                    await _runTracker.UpdateRunOnCancelledAsync(run, reason, sw.ElapsedMilliseconds, CancellationToken.None);
                }

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentRuntime execution failed for request AgentId={AgentId}", request.AgentId);
                ApplyEffectiveInputSummary(run, context);

                await _eventPublisher.PublishRunFailedEventAsync(request, run, ex, sw.ElapsedMilliseconds, false);

                if (run != null)
                {
                    sw.Stop();
                    await _runTracker.UpdateRunOnFailureAsync(run, ex, sw.ElapsedMilliseconds, CancellationToken.None);
                }

                throw;
            }

            if (run != null)
            {
                return result.CloneWith(runId: run.Id, status: run.Status);
            }

            return result;
        }
        finally
        {
            if (previousRequest != null)
            {
                _executionContextAccessor.RestoreProperties(previousProperties);
            }
            else
            {
                _executionContextAccessor.ClearProperties();
            }
            _executionContextAccessor.CurrentRequest = previousRequest;
        }
    }

    /// <summary>
    /// 把本次运行装进当前执行上下文：当前请求、一个新的空属性包、子 Agent 标记。
    /// </summary>
    /// <remarks>
    /// 属性包在这里就建好，而不是清成 null 等第一次读取时惰性创建：惰性创建若发生在某个普通 async 方法里
    /// （例如 <c>SetupContextAndResolveAsync</c> 写入 <c>CurrentRunId</c>），那份包只存在于该方法自己的上下文，
    /// 传不回运行本身，之后的中间件与工具读到的是另一个空包，运行 id 就这样对工具不可见。
    /// 在运行自己的上下文里先建好，所有层写的都是同一个包；流式路径再由 <see cref="ExecutionContextFlow"/>
    /// 让这个包跨 yield 保持可见。
    /// </remarks>
    private void EnterRun(AgentRunRequest request)
    {
        _executionContextAccessor.CurrentRequest = request;
        _executionContextAccessor.RestoreProperties(new Dictionary<string, object>());
        MarkSubAgentRun(request);
    }

    /// <summary>
    /// 后台起的子 Agent 运行在这里打上子 Agent 标记。
    /// </summary>
    /// <remarks>
    /// 判据必须来自请求本身：<c>spawn_agent</c> 起的运行在新作用域、新执行流里跑，父级的属性包不会流过去，
    /// 而本方法上一行的 <c>ClearProperties</c> 也会抹掉调用前设的任何标记。
    /// 带 <c>ParentRunId</c> 的运行按定义就是别人起的子运行；标了 <see cref="AgentRunRequest.IsBackground"/> 的
    /// 根 spawn（没有父运行）同样是 <c>SpawnAsync</c> 起的子 Agent 运行 ——
    /// 少了这一步，<c>ToolPermissionRule.IsSubAgentOnly</c> 在最常用的那条起子 Agent 路径上一条都不生效。
    /// </remarks>
    private void MarkSubAgentRun(AgentRunRequest request)
    {
        if (request.ParentRunId.HasValue || request.IsBackground)
        {
            SubAgentContext.Mark(_executionContextAccessor, _serviceProvider, request.SubAgentName);
        }
    }

    /// <summary>
    /// 无 AgentId 的请求自选的工具组 / 工具名必须逐个在 <c>AI:AdHocTools</c> 允许列表里。
    /// 有 AgentId 时解析器只看实体授权，请求体的组根本不参与，不在此拦；
    /// 进程内调用方经 <see cref="AgentRunRequest.TrustedToolSelection"/> 放行。
    /// 不允许的组一律拒绝而不是静默丢掉 —— 丢掉后调用方以为拿到了工具，模型只是回答「我没有那个工具」。
    /// </summary>
    private void EnsureAdHocToolSelectionAllowed(AgentRunRequest request)
    {
        var hasSelection = request.ToolGroups is { Count: > 0 } || request.ToolNames is { Count: > 0 };
        if (!hasSelection || request.TrustedToolSelection || request.AgentId.HasValue)
        {
            return;
        }

        var allowList = _aiOptions.CurrentValue.AdHocTools;

        var disallowedGroup = request.ToolGroups?
            .FirstOrDefault(g => !allowList.AllowedGroups.Contains(g, StringComparer.OrdinalIgnoreCase));
        if (disallowedGroup is not null)
        {
            _logger.LogWarning(
                "Rejected ad-hoc tool group '{ToolGroup}' requested without an AgentId (UserId={UserId}); " +
                "add it to AI:AdHocTools:AllowedGroups to permit client-selected use",
                disallowedGroup, request.UserId);
            throw new BusinessException(
                $"Tool group '{disallowedGroup}' is not allowed for requests without an agent.",
                ErrorCodes.ToolGroupNotAllowed, 403);
        }

        var disallowedTool = request.ToolNames?
            .FirstOrDefault(t => !allowList.AllowedTools.Contains(t, StringComparer.OrdinalIgnoreCase));
        if (disallowedTool is not null)
        {
            _logger.LogWarning(
                "Rejected ad-hoc tool '{ToolName}' requested without an AgentId (UserId={UserId}); " +
                "add it to AI:AdHocTools:AllowedTools to permit client-selected use",
                disallowedTool, request.UserId);
            throw new BusinessException(
                $"Tool '{disallowedTool}' is not allowed for requests without an agent.",
                ErrorCodes.ToolNameNotAllowed, 403);
        }
    }

    private static BusinessException CreateAgentResolutionException(AgentResolution resolution)
    {
        return resolution.ErrorCode switch
        {
            ErrorCodes.AgentNotFound => new BusinessException("Agent not found", ErrorCodes.AgentNotFound, 404),
            ErrorCodes.AgentDisabled => new BusinessException("Agent is disabled", ErrorCodes.AgentDisabled, 400),
            _ => new BusinessException(
                $"Agent resolution failed: {resolution.ErrorCode ?? ErrorCodes.AgentRunFailed}",
                resolution.ErrorCode ?? ErrorCodes.AgentRunFailed,
                500)
        };
    }

    /// <summary>Execute an AI run (streaming).</summary>
    public async IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var previousRequest = _executionContextAccessor.CurrentRequest;
        var previousProperties = previousRequest != null
            ? _executionContextAccessor.CaptureProperties()
            : null;
        EnterRun(request);

        try
        {
            if (request.WorkflowId.HasValue)
            {
                await foreach (var chunk in StreamWorkflowAsync(request, cancellationToken))
                {
                    yield return chunk;
                }
                yield break;
            }

            var setup = await SetupContextAndResolveAsync(request, isStreaming: true, cancellationToken);
            if (!setup.Resolution.IsSuccess)
            {
                yield return new AgentStreamChunk
                {
                    Error = $"Agent resolution failed: {setup.Resolution.ErrorCode}",
                    FinishReason = FinishReasons.Error
                };
                yield break;
            }

            await foreach (var chunk in StreamPipelineAsync(setup, request, cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            if (previousRequest != null)
            {
                _executionContextAccessor.RestoreProperties(previousProperties);
            }
            else
            {
                _executionContextAccessor.ClearProperties();
            }
            _executionContextAccessor.CurrentRequest = previousRequest;
        }
    }

    /// <summary>
    /// Streaming workflow path - delegates to IWorkflowDelegator.ExecuteWorkflowStreamingAsync
    /// and publishes a completion event when the stream terminates.
    /// </summary>
    private async IAsyncEnumerable<AgentStreamChunk> StreamWorkflowAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var workflowStopwatch = Stopwatch.StartNew();
        AgentStreamChunk? lastChunk = null;

        await foreach (var chunk in _workflowDelegator.ExecuteWorkflowStreamingAsync(request, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            lastChunk = chunk;
            yield return chunk;
        }

        workflowStopwatch.Stop();

        if (lastChunk != null)
        {
            await _eventPublisher.PublishRunCompletedEventAsync(
                request,
                new AgentRunResult
                {
                    Response = lastChunk.Text ?? string.Empty,
                    FinishReason = lastChunk.FinishReason,
                    Status = AgentRunStatusResolver.Resolve(lastChunk.FinishReason)
                },
                null,
                workflowStopwatch.ElapsedMilliseconds,
                true,
                "Workflow");
        }
    }

    /// <summary>
    /// Streaming pipeline path - runs the middleware pipeline, aggregates tokens/usage,
    /// and finalizes run tracking + events in a robust finally block.
    /// </summary>
    private async IAsyncEnumerable<AgentStreamChunk> StreamPipelineAsync(
        RunSetupResult setup,
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resolution = setup.Resolution;
        var run = setup.Run;
        var context = setup.Context;
        await using var heartbeat = StartHeartbeat(run);

        _cachedStreamingPipelineDelegate ??= BuildStreamingPipelineDelegate();

        var sw = Stopwatch.StartNew();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        string? lastFinishReason = null;
        string? lastModel = null;
        var completedNormally = false;
        var responseBuilder = new StringBuilder();
        var defaultModel = context.EffectiveModel ?? resolution.Model;

        // Streaming glues narration and answer into one string (the non-streaming path does not
        // - see DeliverableTracker); this pulls the answer back out.
        var deliverableTracker = new DeliverableTracker();

        try
        {
            await foreach (var chunk in _cachedStreamingPipelineDelegate(context, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                chunk.Model ??= defaultModel;
                lastModel = chunk.Model ?? lastModel;

                if (chunk.Usage != null)
                {
                    totalInputTokens += chunk.Usage.InputTokens;
                    totalOutputTokens += chunk.Usage.OutputTokens;
                }
                if (chunk.FinishReason != null)
                {
                    lastFinishReason = chunk.FinishReason;
                }
                deliverableTracker.Observe(chunk);

                if (!string.IsNullOrEmpty(chunk.Text))
                {
                    responseBuilder.Append(chunk.Text);
                }

                if (request.StreamMode.HasFlag(chunk.Mode))
                {
                    yield return chunk;
                }
            }
            completedNormally = true;
        }
        finally
        {
            sw.Stop();

            var fullText = responseBuilder.ToString();

            await FinalizeStreamingAsync(
                setup, request, sw.ElapsedMilliseconds,
                completedNormally, cancellationToken.IsCancellationRequested,
                totalInputTokens, totalOutputTokens, lastFinishReason, lastModel,
                fullText, deliverableTracker.Resolve(fullText));
        }
    }

    /// <summary>
    /// Finalize streaming run state - persists run, records traces, publishes events.
    /// All exceptions logged and swallowed so they don't propagate through yield/finally.
    /// </summary>
    private async Task FinalizeStreamingAsync(
        RunSetupResult setup,
        AgentRunRequest request,
        long durationMs,
        bool completedNormally,
        bool cancelled,
        int totalInputTokens,
        int totalOutputTokens,
        string? lastFinishReason,
        string? lastModel,
        string response,
        string? deliverable)
    {
        var resolution = setup.Resolution;
        var run = setup.Run;
        var context = setup.Context;

        try
        {
            ApplyEffectiveInputSummary(run, context);
            AgentRunResult? streamResult = null;

            // run 只决定要不要落运行记录；终态事件与 RunStarted 一样每次运行都发，与非流式路径同口径。
            // 聊天与管理端的流式运行都不开运行追踪（run 为 null），此前它们只有 Started 没有终态事件，
            // 订阅 Completed / Failed 做统计与告警的宿主看到的是一批永远没结束的运行。
            if (completedNormally)
            {
                streamResult = AgentRunStatusResolver.EnsureStatus(new AgentRunResult
                {
                    Response = response,
                    Deliverable = deliverable,
                    Usage = new TokenUsageDto { InputTokens = totalInputTokens, OutputTokens = totalOutputTokens },
                    FinishReason = lastFinishReason,
                    Model = lastModel,
                    Provider = context.EffectiveProvider ?? resolution.Provider
                });

                if (lastFinishReason == FinishReasons.MaxToolIterations)
                {
                    _logger.LogWarning(
                        "Agent run {RunId} reached MaxToolIterations limit - response may be incomplete",
                        run?.Id);
                }

                if (run != null)
                {
                    await _runTracker.FinalizeStreamingCompletedAsync(
                        run, streamResult, totalInputTokens, totalOutputTokens, durationMs, CancellationToken.None);
                }

                await _eventPublisher.PublishRunCompletedEventAsync(
                    request, streamResult, run,
                    durationMs, true,
                    context.EffectiveProvider ?? resolution.Provider);
            }
            else if (cancelled)
            {
                if (run != null)
                {
                    await _runTracker.FinalizeStreamingCancelledAsync(run, lastFinishReason, durationMs, CancellationToken.None);
                }

                await _eventPublisher.PublishRunCancelledEventAsync(
                    request, run, "Streaming was cancelled by the caller", durationMs, true);
            }
            else
            {
                if (run != null)
                {
                    await _runTracker.FinalizeStreamingFailedAsync(run, lastFinishReason, durationMs, CancellationToken.None);
                }

                await _eventPublisher.PublishRunFailedEventAsync(
                    request, run,
                    new InvalidOperationException("Streaming execution failed"),
                    durationMs, true);
            }

            // 与非流式同一口径：标题取模型实际收到的用户文本
            var titleText = UserTurn.EffectiveText(context);
            if (completedNormally
                && context.IsNewThread
                && request.ThreadId.HasValue
                && !string.IsNullOrWhiteSpace(titleText)
                && AgentRunStatusResolver.ShouldGenerateThreadTitle(lastFinishReason))
            {
                await _eventPublisher.HandleNewThreadTitleAsync(request, streamResult!, titleText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to finalize streaming run for RunId={RunId}", run?.Id);
        }
    }

    /// <summary>
    /// 在跑期间定期刷新运行行的心跳（见 <see cref="AgentRunRecoveryService"/>）；不追踪运行或没有心跳服务时什么都不做。
    /// </summary>
    private IAsyncDisposable StartHeartbeat(AgentRun? run)
        => run != null && _runHeartbeat != null ? _runHeartbeat.Start(run.Id) : NoHeartbeat.Instance;

    private sealed class NoHeartbeat : IAsyncDisposable
    {
        public static readonly NoHeartbeat Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Resume an interrupted run.</summary>
    public async Task<AgentRunResult> ResumeAsync(Guid runId, ResumeRunInput? input = null, CancellationToken cancellationToken = default)
    {
        var run = await _runTracker.GetWithNodesAsync(runId, cancellationToken);
        if (run == null)
        {
            throw new BusinessException($"Run {runId} not found", ErrorCodes.RunNotFound, 404);
        }

        if (run.Status != AgentRunStatus.AwaitingApproval
            && run.Status != AgentRunStatus.Failed
            && run.Status != AgentRunStatus.RequiresClarification)
        {
            throw new BusinessException(
                $"Run {runId} is in {run.Status} state and cannot be resumed",
                ErrorCodes.RunInvalidState, 400);
        }

        if (!string.IsNullOrWhiteSpace(run.WorkflowExecutionId) && run.WorkflowDefinitionId.HasValue)
        {
            return await _workflowDelegator.ResumeWorkflowRunAsync(run, input, cancellationToken);
        }

        // 授权在改状态之前：线程必须归运行归属人所有。此前先写 Running 再进管线，管线里的归属校验
        // 抛 404 后 catch 块把运行写成 Failed / Error="Thread not found"，一次误点就毁掉了可续跑状态。
        if (run.ThreadId.HasValue && run.CreatorId.HasValue && _threadService != null
            && !await _threadService.IsOwnerAsync(run.ThreadId.Value, run.CreatorId.Value))
        {
            _logger.LogWarning("Resume rejected: thread {ThreadId} is not owned by run {RunId}'s owner {OwnerId}",
                run.ThreadId, run.Id, run.CreatorId);
            throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
        }

        // 快照也在改状态之前解析：解析不了（旧行没有 AgentId、或内容损坏）要在行还原样时拒绝，
        // 不能先写 Running 再发现没法重建请求。
        var snapshot = ParseResumeSnapshot(run);

        var previousStatus = run.Status;
        run.Status = AgentRunStatus.Running;
        run.LastHeartbeatAt = DateTime.UtcNow;
        await _runTracker.UpdateAsync(run, cancellationToken);
        await using var heartbeat = StartHeartbeat(run);

        await _runTracker.RecordTraceAsync(run.Id, null, AgentTraceEventTypes.RunResumed,
            new { previousStatus = previousStatus.ToString(), approvalDecision = input?.ApprovalDecision },
            0, cancellationToken);

        if (input?.ApprovalDecision != null)
        {
            var awaitingNode = run.Nodes
                .FirstOrDefault(n => n.Status == AgentRunNodeStatus.AwaitingApproval);

            if (awaitingNode != null)
            {
                var isApproved = input.ApprovalDecision.Equals("approve", StringComparison.OrdinalIgnoreCase);
                awaitingNode.Status = isApproved ? AgentRunNodeStatus.Approved : AgentRunNodeStatus.Rejected;
                awaitingNode.Output = input.ApprovalComment;
                await _runTracker.UpdateNodeAsync(awaitingNode, cancellationToken);
            }
        }

        if (input?.RetryNodeId.HasValue == true)
        {
            var retryNode = run.Nodes.FirstOrDefault(n => n.Id == input.RetryNodeId.Value);
            if (retryNode != null)
            {
                retryNode.Status = AgentRunNodeStatus.Pending;
                retryNode.RetryCount++;
                retryNode.Error = null;
                await _runTracker.UpdateNodeAsync(retryNode, cancellationToken);
            }
        }

        // 按建行时的快照重建请求（工具选择、Provider/Model、子 Agent 标记、完整用户消息），
        // 父子链与归属人从行上回填。此前只带 AgentId / ThreadId / InputSummary：模板 spawn 的运行续跑成
        // 无工具的默认 agent，DB agent 的续跑丢掉 IsSubAgent 标记（子 Agent 裁剪整体失效），消息被截到 500 字。
        var resumeRequest = new AgentRunRequest
        {
            OperationType = snapshot?.OperationType ?? AIOperationType.AgentRun,
            AgentId = run.AgentId ?? snapshot?.AgentId,
            Provider = snapshot?.Provider,
            Model = snapshot?.Model,
            ThreadId = run.ThreadId,
            UserMessage = input?.UserMessage ?? snapshot?.UserMessage ?? run.InputSummary,
            ToolGroups = snapshot?.ToolGroups,
            ToolNames = snapshot?.ToolNames,
            TrustedToolSelection = snapshot?.TrustedToolSelection ?? false,
            ParentRunId = run.ParentRunId,
            RootRunId = run.RootRunId,
            IsBackground = snapshot?.IsBackground ?? false,
            SubAgentName = snapshot?.SubAgentName,
            SubAgentInstructions = snapshot?.SubAgentInstructions,
            MaxToolIterations = snapshot?.MaxToolIterations,
            ExcludedToolGroups = snapshot?.ExcludedToolGroups,
            AgentVersionNumber = snapshot?.AgentVersionNumber,
            ReasoningEffort = snapshot?.ReasoningEffort,
            Attachments = snapshot?.Attachments,
            Metadata = snapshot?.Metadata,
            PlanMode = snapshot?.PlanMode ?? false,
            // 以归属人的身份续跑：配额、权限规则与线程归属都按发起人认，不按点 Resume 的管理员认
            UserId = run.CreatorId,
            EnableRunTracking = false
        };

        // 续跑与后台首轮同样登记进取消注册表：kill_agent / 管理端取消先写行、再经注册表触发令牌。
        // 此前续跑不在表里，取消只改了行，续跑照样把工具跑完。登记不上（同一 runId 上还挂着上一段执行的令牌）
        // 时不撤销别人的登记。
        using var resumeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var registered = _cancellationRegistry?.TryRegister(run.Id, resumeCts) ?? false;

        var sw = Stopwatch.StartNew();
        AgentRunResult result;
        try
        {
            result = AgentRunStatusResolver.EnsureStatus(await RunAsync(resumeRequest, resumeCts.Token));
            sw.Stop();

            // 共享收尾先读一次存储里的状态：取消落在续跑已不再看令牌的那一刻时以取消为准，
            // 而不是用开始时读到的那份实体把 Cancelled 覆盖回 Completed
            await _runTracker.UpdateRunOnCompletionAsync(run, result, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (OperationCanceledException) when (resumeCts.IsCancellationRequested)
        {
            // 续跑被取消（调用方令牌或 kill）：与首轮同口径，写 Cancelled 而不是 Failed，kill 的原因随行留下
            sw.Stop();
            var reason = ResolveCancellation(run)?.Reason ?? "The resumed run was cancelled by the caller";
            await _runTracker.UpdateRunOnCancelledAsync(run, reason, sw.ElapsedMilliseconds, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _runTracker.UpdateRunOnFailureAsync(run, ex, sw.ElapsedMilliseconds, CancellationToken.None);
            throw;
        }
        finally
        {
            if (registered)
            {
                _cancellationRegistry!.Unregister(run.Id);
            }
        }

        return result.CloneWith(runId: run.Id, status: run.Status);
    }

    /// <summary>
    /// 续跑用的请求快照。没有快照的是迁移前的旧行：有 AgentId 的照常续跑（工具由 grants 重建），
    /// 没有 AgentId 的（模板 spawn）拒绝 —— 静默续跑成无工具的默认 agent 比拒绝更糟。快照损坏同样拒绝。
    /// </summary>
    private static AgentRunRequestSnapshot? ParseResumeSnapshot(AgentRun run)
    {
        AgentRunRequestSnapshot? snapshot;
        try
        {
            snapshot = AgentRunRequestSnapshot.Parse(run.RequestSnapshot);
        }
        catch (JsonException ex)
        {
            throw new BusinessException(
                $"Run {run.Id} cannot be resumed: its request snapshot is unreadable",
                ErrorCodes.RunInvalidState, 400).WithData("reason", ex.Message);
        }

        if (snapshot == null && !run.AgentId.HasValue)
        {
            throw new BusinessException(
                $"Run {run.Id} cannot be resumed: it was started without an agent and no request snapshot was recorded",
                ErrorCodes.RunInvalidState, 400);
        }

        return snapshot;
    }

    /// <summary>
    /// 这次取消是谁发的：kill_agent / 管理端 cancel 与后台超时都经 <see cref="ISubAgentRunCancellationRegistry"/>
    /// 触发并留下原因；调用方自己的令牌（HTTP 断连等）不经注册表，返回 null。
    /// </summary>
    private RunCancellation? ResolveCancellation(AgentRun? run)
        => run != null ? _cancellationRegistry?.GetCancellation(run.Id) : null;

    // ------------------------------------------------------------------------
    // AgentRuntime - core executor + helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// 运行摘要（<see cref="AgentRun.InputSummary"/>）取模型实际收到的用户文本：管道里有人改写过本轮输入
    /// （输入守卫的脱敏等）时，用改写后的文本覆盖建行时记下的原文；没人改写时不动。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 运行行在管道之前建好，那时只有请求原文。守卫把卡号、身份证号遮掉之后，线程历史与标题取的都是遮蔽后的文本，
    /// 运行摘要却还是原文，于是遮掉的内容原样出现在管理端的运行列表与详情里。
    /// 这里只改内存里这一份实体，随后紧跟着的收尾（完成、失败、取消、流式三种结局）整行落库时一并写入。
    /// </para>
    /// <para>
    /// 续跑用的 <see cref="AgentRun.RequestSnapshot"/> 不动：它要按原请求重建，守卫在续跑时会再跑一遍。
    /// </para>
    /// </remarks>
    private static void ApplyEffectiveInputSummary(AgentRun? run, AiMiddlewareContext context)
    {
        if (run == null || (context.EffectiveUserMessage == null && context.EffectiveContentParts == null))
            return;

        run.InputSummary = RunTracker.SummarizeInput(UserTurn.EffectiveText(context));
    }

    /// <summary>
    /// Setup result returned by <see cref="SetupContextAndResolveAsync"/>.
    /// Captures the per-run context shared between RunAsync and RunStreamingAsync.
    /// </summary>
    private sealed record RunSetupResult(
        AgentResolution Resolution,
        AgentRun? Run,
        AiMiddlewareContext Context);

    /// <summary>
    /// Shared prelude for both RunAsync and RunStreamingAsync:
    /// resolve thinking model → resolve agent → get/create run → publish started event → build context.
    /// On resolution failure the returned result carries the failed <see cref="AgentResolution"/> with
    /// no Run and no Context; callers MUST check <c>Resolution.IsSuccess</c> before touching Context.
    /// </summary>
    private async Task<RunSetupResult> SetupContextAndResolveAsync(
        AgentRunRequest request,
        bool isStreaming,
        CancellationToken ct)
    {
        // think 别名按「这次真正用的提供商与模型」判定。无 AgentId 时它们就是请求自己的，解析前即可切换；
        // 有 AgentId 时它们属于 Agent，解析前不知道，解析后再判定（SwitchAgentToThinkModelAsync）。
        // 解析前拿默认提供商的别名去套 Agent，会把别家的模型名发给 Agent 自己的提供商，或盖掉 Agent 已配的推理模型。
        var effectiveModel = request.AgentId.HasValue
            ? request.Model
            : ResolveThinkingModel(request, request.Provider, request.Model);

        EnsureAdHocToolSelectionAllowed(request);

        var resolution = await _agentResolver.ResolveAgentAsync(
            request.AgentId, request.Provider, effectiveModel, request.ToolGroups, ct, request.ToolNames);

        if (!resolution.IsSuccess)
        {
            return new RunSetupResult(resolution, null, null!);
        }

        if (request.AgentId.HasValue)
        {
            resolution = await SwitchAgentToThinkModelAsync(request, resolution, ct);
        }

        AgentRun? run = null;
        if (request.EnableRunTracking)
        {
            run = await _runTracker.GetOrCreateRunAsync(request, resolution, ct);
            _executionContextAccessor.Properties[ContextPropertyKeys.CurrentRunId] = run.Id;
        }

        await _eventPublisher.PublishRunStartedEventAsync(
            request, run, isStreaming, resolution.Provider, resolution.Model, resolution.ExecutionMode);

        var context = new AiMiddlewareContext
        {
            Request = request,
            Agent = resolution,
            Run = run,
            ServiceProvider = _serviceProvider
        };
        ExcludeRequestedToolGroups(context);

        return new RunSetupResult(resolution, run, context);
    }

    /// <summary>
    /// Core executor (non-streaming) - innermost pipeline layer, delegates to execution strategy.
    /// </summary>
    private async Task<AgentRunResult> ExecuteCoreAsync(AiMiddlewareContext context, CancellationToken ct)
    {
        var resolution = context.Agent;

        var agent = ApplyRequestedRunSettings(await ApplyModelOverrideAsync(resolution, context, ct), context.Request);

        var messages = new List<ChatMessage>(context.Messages);
        await AppendUserTurnAsync(messages, context, ct);

        agent = MergeAdditionalTools(agent, context);
        agent = ApplyToolExclusions(agent, context);

        var strategy = ExecutionStrategyResolver.Resolve(resolution.ExecutionMode, resolution.AgentConfiguration);
        var strategyContext = new ExecutionStrategyContext
        {
            AgentFactory = _agentFactory,
            AgentRepository = _agentRepository,
            ServiceProvider = _serviceProvider,
            ExecutionContextAccessor = _executionContextAccessor,
            Logger = _logger,
            StartingAgentId = resolution.AgentId
        };

        using (ToolContext.Establish(_serviceProvider, ct))
        {
            var executionResult = await strategy.ExecuteAsync(agent, messages, strategyContext, ct);
            var response = executionResult.Response;
            var actualModel = context.EffectiveModel ?? resolution.Model;
            var actualProvider = context.EffectiveProvider ?? resolution.Provider;

            return new AgentRunResult
            {
                Response = response.Text ?? string.Empty,
                ThreadId = context.Request.ThreadId,
                Usage = executionResult.AggregatedUsage ?? response.Usage,
                Citations = context.Citations.Count > 0 ? context.Citations : null,
                FinishReason = response.FinishReason,
                Model = actualModel,
                Provider = actualProvider,
                HandoffPath = executionResult.HandoffPath,
                FinalAgentName = executionResult.FinalAgentName,
                Reasoning = response.Reasoning
            };
        }
    }

    /// <summary>
    /// Core executor (streaming) - innermost pipeline layer, delegates to execution strategy.
    /// </summary>
    private async IAsyncEnumerable<AgentStreamChunk> ExecuteCoreStreamingAsync(
        AiMiddlewareContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var resolution = context.Agent;

        var agent = ApplyRequestedRunSettings(await ApplyModelOverrideAsync(resolution, context, ct), context.Request);

        var messages = new List<ChatMessage>(context.Messages);
        await AppendUserTurnAsync(messages, context, ct);

        agent = MergeAdditionalTools(agent, context);
        agent = ApplyToolExclusions(agent, context);

        var strategy = ExecutionStrategyResolver.Resolve(resolution.ExecutionMode, resolution.AgentConfiguration);
        var pendingEvents = new ConcurrentQueue<AgentStreamChunk>();
        var strategyContext = new ExecutionStrategyContext
        {
            AgentFactory = _agentFactory,
            AgentRepository = _agentRepository,
            ServiceProvider = _serviceProvider,
            ExecutionContextAccessor = _executionContextAccessor,
            Logger = _logger,
            StartingAgentId = resolution.AgentId,
            EmitEvent = chunk => pendingEvents.Enqueue(chunk)
        };

        using var scope = ToolContext.Establish(_serviceProvider, ct);

        // 策略与执行器在后续每一步都要看到此刻的上下文（当前请求、属性包、ToolContext 与各中间件的 AsyncLocal），
        // 否则 yield 之后执行的工具跑在消费方的上下文里，见 ExecutionContextFlow
        await foreach (var chunk in ExecutionContextFlow.Bind(strategy.ExecuteStreamingAsync(agent, messages, strategyContext, ct)).WithCancellation(ct))
        {
            while (pendingEvents.TryDequeue(out var evt))
                yield return evt;

            yield return chunk;
        }

        while (pendingEvents.TryDequeue(out var remaining))
            yield return remaining;
    }

    /// <summary>
    /// Append the current user turn exactly as the input guardrails approved it: the guardrail-modified
    /// message / content parts when present (<see cref="AiMiddlewareContext.EffectiveUserMessage"/>,
    /// <see cref="AiMiddlewareContext.EffectiveContentParts"/>), otherwise the request's own.
    /// A content-only request (no Message) is a user turn too; skipping it would answer without the question.
    /// </summary>
    private async Task AppendUserTurnAsync(List<ChatMessage> messages, AiMiddlewareContext context, CancellationToken ct)
    {
        if (!UserTurn.IsPresent(context))
            return;

        var userMessage = await _agentResolver.BuildChatMessageAsync(
            UserTurn.EffectiveMessage(context), UserTurn.EffectiveParts(context), ct);
        messages.Add(userMessage);
    }

    /// <summary>
    /// Apply EffectiveModel/Provider override set by SkillConstraintMiddleware.
    /// Rebuilds the executor if Model or Provider changed; returns original otherwise.
    /// Operates entirely on <see cref="IAgentExecutor"/> - custom resolver/factory
    /// implementations returning their own executors flow through without casts.
    /// </summary>
    private async Task<IAgentExecutor> ApplyModelOverrideAsync(AgentResolution resolution, AiMiddlewareContext context, CancellationToken ct)
    {
        var originalAgent = resolution.Agent!;
        var effectiveModel = context.EffectiveModel;
        var effectiveProvider = context.EffectiveProvider;

        if (effectiveModel == null && effectiveProvider == null)
            return originalAgent;

        var modelChanged = effectiveModel != null && !string.Equals(effectiveModel, resolution.Model, StringComparison.OrdinalIgnoreCase);
        var providerChanged = effectiveProvider != null && !string.Equals(effectiveProvider, resolution.Provider, StringComparison.OrdinalIgnoreCase);

        if (!modelChanged && !providerChanged)
            return originalAgent;

        if (resolution.CreationParameters == null)
        {
            _logger.LogWarning(
                "SkillConstraintMiddleware requested model/provider override (Model={Model}, Provider={Provider}) " +
                "but AgentResolution has no CreationParameters. Override skipped.",
                effectiveModel, effectiveProvider);
            return originalAgent;
        }

        var p = resolution.CreationParameters;
        var newProvider = effectiveProvider ?? resolution.Provider;
        var newModel = effectiveModel ?? resolution.Model;

        _logger.LogInformation(
            "Skill constraint override: rebuilding AgentExecutor with Provider={Provider}, Model={Model}",
            newProvider, newModel);

        return await _agentFactory.CreateAgentAsync(
            newProvider, newModel, p.Instructions, p.Name, p.ToolGroups,
            p.Temperature, p.MaxTokens, options: null, userPermissions: p.UserPermissions,
            toolNames: p.ToolNames, agentId: resolution.AgentId, ct: ct);
    }

    /// <summary>
    /// Merge middleware-injected tools into Agent, deduplicating by name.
    /// </summary>
    private static IAgentExecutor MergeAdditionalTools(IAgentExecutor agent, AiMiddlewareContext context)
    {
        if (context.AdditionalTools.Count == 0)
            return agent;

        var existingNames = agent.Tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newTools = context.AdditionalTools.Where(t => !existingNames.Contains(t.Name)).ToList();

        return newTools.Count > 0 ? agent.WithAdditionalTools(newTools) : agent;
    }

    /// <summary>
    /// Withhold the tools a middleware excluded for this turn (skill constraints) from the model.
    /// Runs after <see cref="MergeAdditionalTools"/> so the agent's own tools and the injected ones
    /// are treated alike. Execution-time enforcement lives in the tool middleware pipeline and does
    /// not depend on this step.
    /// </summary>
    private static IAgentExecutor ApplyToolExclusions(IAgentExecutor agent, AiMiddlewareContext context)
    {
        return context.ExcludedToolNames.Count == 0 ? agent : agent.WithoutTools(context.ExcludedToolNames);
    }

    /// <summary>
    /// 子 Agent 模板随请求带来的运行设置：模板运行（没有自己的 Agent）的系统指令、工具循环上限。
    /// </summary>
    /// <remarks>
    /// 套在模型覆盖之后，技能约束触发重建执行器时同样生效。spawn 只在没有 AgentId 时填系统指令，
    /// 这里照单执行；上限对两种 spawn 都生效。
    /// </remarks>
    private static IAgentExecutor ApplyRequestedRunSettings(IAgentExecutor agent, AgentRunRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SubAgentInstructions))
        {
            agent = agent.WithInstructions(request.SubAgentInstructions);
        }

        if (request.MaxToolIterations is > 0)
        {
            agent = agent.WithMaxToolIterations(request.MaxToolIterations.Value);
        }

        return agent;
    }

    /// <summary>
    /// 请求排除的工具组（子 Agent 模板的 ExcludedToolGroups）展开成工具名，并进本次运行的 <see cref="AiMiddlewareContext.ExcludedToolNames"/>。
    /// </summary>
    /// <remarks>
    /// 走运行时这一层而不是工具解析：Agent 自带的（DB Agent 的授权）、请求自选的、中间件注入的工具在
    /// <see cref="ApplyToolExclusions"/> 处一并去掉，默认执行器上被去掉的工具连调用都找不到。
    /// 组名按工具注册表（<c>[AIToolGroup]</c>）展开，与 ToolGroups 同一套名字。
    /// </remarks>
    private void ExcludeRequestedToolGroups(AiMiddlewareContext context)
    {
        var groups = context.Request.ExcludedToolGroups;
        if (groups is not { Count: > 0 })
        {
            return;
        }

        if (_toolRegistry == null)
        {
            _logger.LogWarning(
                "Run requested excluded tool groups [{Groups}] but no IToolRegistry is available to expand them; the groups stay available",
                string.Join(", ", groups));
            return;
        }

        foreach (var tool in _toolRegistry.GetToolsByGroups(groups))
        {
            context.ExcludedToolNames.Add(tool.Name);
        }
    }

    /// <summary>
    /// Resolve all registered middlewares from DI, ordered by Order.
    /// List is cached on first access.
    /// </summary>
    private List<IAiMiddleware> ResolveMiddlewares() => _middlewares.Value;

    /// <summary>
    /// Build non-streaming pipeline delegate (scope-cached).
    /// </summary>
    private AiMiddlewareDelegate BuildPipelineDelegate()
    {
        var pipeline = new AiMiddlewarePipeline();
        foreach (var middleware in ResolveMiddlewares())
        {
            pipeline.Use(middleware);
        }
        return pipeline.Build(ExecuteCoreAsync);
    }

    /// <summary>
    /// Build streaming pipeline delegate (scope-cached).
    /// </summary>
    private AiStreamingMiddlewareDelegate BuildStreamingPipelineDelegate()
    {
        var pipeline = new AiMiddlewarePipeline();
        foreach (var middleware in ResolveMiddlewares())
        {
            pipeline.Use(middleware);
        }
        return pipeline.BuildStreaming(ExecuteCoreStreamingAsync);
    }

    /// <summary>
    /// Auto-resolve effective model based on ReasoningEffort.
    /// When ReasoningEffort != None and <paramref name="model"/> doesn't support reasoning,
    /// look up the "think" model alias of <paramref name="providerName"/> (the provider the run actually uses).
    /// </summary>
    private string? ResolveThinkingModel(AgentRunRequest request, string? providerName, string? model)
    {
        if (request.ReasoningEffort is null or ReasoningEffort.None) return model;

        if (ModelCapabilities.SupportsReasoning(model) || ModelCapabilities.IsAlwaysOnReasoning(model))
            return model;

        var options = _aiOptions.CurrentValue;
        if (string.IsNullOrWhiteSpace(providerName))
            providerName = options.DefaultProvider;

        if (string.IsNullOrWhiteSpace(providerName) || !options.Providers.TryGetValue(providerName, out var providerOptions))
            return model;

        if (providerOptions.Models?.TryGetValue("think", out var thinkModel) == true)
        {
            _logger.LogDebug(
                "Auto-switching to think model '{ThinkModel}' for provider '{Provider}' (ReasoningEffort={Effort})",
                thinkModel, providerName, request.ReasoningEffort);
            return thinkModel;
        }

        return model;
    }

    /// <summary>
    /// 已定义 Agent 的 think 别名切换：按 Agent 自己的提供商与解析出的模型判定，需要切换时用解析时的创建参数
    /// 在同一个提供商上重建执行器。模型已支持推理、该提供商没配 think 别名、或解析结果不带创建参数（自定义解析器）时原样返回。
    /// </summary>
    private async Task<AgentResolution> SwitchAgentToThinkModelAsync(AgentRunRequest request, AgentResolution resolution, CancellationToken ct)
    {
        var thinkModel = ResolveThinkingModel(request, resolution.Provider, resolution.Model);
        if (thinkModel == null || string.Equals(thinkModel, resolution.Model, StringComparison.OrdinalIgnoreCase))
            return resolution;

        if (resolution.CreationParameters is not { } p)
        {
            _logger.LogDebug(
                "Think model '{ThinkModel}' not applied to agent {AgentId}: the resolution carries no creation parameters",
                thinkModel, resolution.AgentId);
            return resolution;
        }

        var executor = await _agentFactory.CreateAgentAsync(
            resolution.Provider, thinkModel, p.Instructions, p.Name, p.ToolGroups,
            p.Temperature, p.MaxTokens, options: null, userPermissions: p.UserPermissions,
            toolNames: p.ToolNames, agentId: resolution.AgentId, ct: ct);

        return resolution.WithExecutor(executor, thinkModel);
    }

}
