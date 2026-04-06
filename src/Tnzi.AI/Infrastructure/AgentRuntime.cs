namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 统一 AI 运行入口 — 所有 AI 执行（chat、workflow、agent run）都通过此入口。
/// 整合中间件管道 + 执行策略 + Run 追踪。
/// </summary>
public partial class AgentRuntime : IAgentRuntime
{
    private readonly IAgentResolver _agentResolver;
    private readonly IAgentFactory _agentFactory;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly IRunStore _runStore;
    private readonly ITraceStore _traceStore;
    private readonly IWorkflowService _workflowService;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<AgentRuntime> _logger;
    private readonly Lazy<List<IAiMiddleware>> _middlewares;

    public AgentRuntime(
        IAgentResolver agentResolver,
        IAgentFactory agentFactory,
        IRepository<Agent, Guid> agentRepository,
        IRunStore runStore,
        ITraceStore traceStore,
        IWorkflowService workflowService,
        IAgentExecutionContextAccessor executionContextAccessor,
        IServiceProvider serviceProvider,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AIOptions> aiOptions,
        ILogger<AgentRuntime> logger,
        IEventBus? eventBus = null)
    {
        _agentResolver = Check.NotNull(agentResolver);
        _agentFactory = Check.NotNull(agentFactory);
        _agentRepository = Check.NotNull(agentRepository);
        _runStore = Check.NotNull(runStore);
        _traceStore = Check.NotNull(traceStore);
        _workflowService = Check.NotNull(workflowService);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _serviceProvider = Check.NotNull(serviceProvider);
        _scopeFactory = Check.NotNull(scopeFactory);
        _aiOptions = Check.NotNull(aiOptions);
        _eventBus = eventBus;
        _logger = Check.NotNull(logger);
        _middlewares = new Lazy<List<IAiMiddleware>>(() =>
            _serviceProvider.GetServices<IAiMiddleware>().OrderBy(m => m.Order).ToList());
    }

    /// <summary>执行一次 AI 运行（非流式）</summary>
    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var previousRequest = _executionContextAccessor.CurrentRequest;
        var previousProperties = previousRequest != null
            ? _executionContextAccessor.CaptureProperties()
            : null;
        _executionContextAccessor.CurrentRequest = request;
        _executionContextAccessor.ClearProperties();

        try
        {
            if (request.WorkflowId.HasValue)
            {
                var workflowStopwatch = Stopwatch.StartNew();
                var workflowResult = EnsureResultStatus(await ExecuteWorkflowAsync(request, cancellationToken));
                workflowStopwatch.Stop();

                await PublishRunCompletedEventAsync(
                    request,
                    workflowResult,
                    null,
                    workflowStopwatch.ElapsedMilliseconds,
                    false,
                    "Workflow");

                return workflowResult;
            }

            var sw = Stopwatch.StartNew();

            // 0. 自动模型切换（当 ReasoningEffort != None 且当前模型不支持推理时，查找 "think" 别名）
            var effectiveModel = ResolveThinkingModel(request);

            // 1. 解析 Agent
            var resolution = await _agentResolver.ResolveAgentAsync(
                request.AgentId, request.Provider, effectiveModel, request.ToolGroups, cancellationToken);

            if (!resolution.IsSuccess)
            {
                throw CreateAgentResolutionException(resolution);
            }

            // 2. 创建 Run（如果启用追踪）
            AgentRun? run = null;
            if (request.EnableRunTracking)
            {
                run = await GetOrCreateRunAsync(request, resolution, cancellationToken);
                _executionContextAccessor.Properties[ContextPropertyKeys.CurrentRunId] = run.Id;
            }

            // 发布运行开始事件
            await PublishRunStartedEventAsync(request, run, false, resolution.Provider, resolution.Model, resolution.ExecutionMode);

            // 3. 构建中间件上下文
            var context = new AiMiddlewareContext
            {
                Request = request,
                Agent = resolution,
                Run = run,
                ServiceProvider = _serviceProvider
            };

            // 4. 构建中间件管道
            var middlewares = ResolveMiddlewares();
            var pipeline = new AiMiddlewarePipeline();
            foreach (var middleware in middlewares)
            {
                pipeline.Use(middleware);
            }

            // 5. 定义核心执行器（管道最内层）
            AiMiddlewareDelegate coreExecutor = async (ctx, ct) =>
            {
                return await ExecuteCoreAsync(ctx, ct);
            };

            // 6. 执行管道
            AgentRunResult result;
            try
            {
                var pipelineDelegate = pipeline.Build(coreExecutor);
                result = await pipelineDelegate(context, cancellationToken);
                if (run != null)
                {
                    result = EnsureResultStatus(result);
                }

                // 7. 更新 Run 状态为完成
                if (run != null)
                {
                    sw.Stop();
                    run.Status = result.Status!.Value;
                    run.Error = run.Status == AgentRunStatus.Failed ? result.Response : null;
                    run.OutputSummary = Truncate(result.Response, 500);
                    run.DurationMs = sw.ElapsedMilliseconds;
                    run.LastHeartbeatAt = DateTime.UtcNow;
                    if (result.Usage != null)
                    {
                        run.TotalInputTokens = result.Usage.InputTokens;
                        run.TotalOutputTokens = result.Usage.OutputTokens;
                    }
                    await _runStore.UpdateAsync(run, cancellationToken);

                    if (result.FinishReason == "max_tool_iterations")
                    {
                        _logger.LogWarning(
                            "Agent run {RunId} reached MaxToolIterations limit — response may be incomplete",
                            run.Id);
                    }

                    // 记录 Trace
                    await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.RunCompleted, result, sw.ElapsedMilliseconds, cancellationToken);
                }

                // 发布运行完成事件
                await PublishRunCompletedEventAsync(
                    request,
                    result,
                    run,
                    sw.ElapsedMilliseconds,
                    false,
                    context.EffectiveProvider ?? resolution.Provider);

                // 仅新线程首轮对话：应用 fallback 标题 + 发布标题生成事件
                if (context.IsNewThread
                    && request.ThreadId.HasValue
                    && !string.IsNullOrWhiteSpace(request.UserMessage)
                    && ShouldGenerateThreadTitle(result.FinishReason))
                {
                    await HandleNewThreadTitleAsync(request, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentRuntime execution failed for request AgentId={AgentId}", request.AgentId);

                // 发布运行失败事件
                await PublishRunFailedEventAsync(request, run, ex, sw.ElapsedMilliseconds, false);

                // 更新 Run 状态为失败
                if (run != null)
                {
                    sw.Stop();
                    run.Status = AgentRunStatus.Failed;
                    run.Error = ex.Message;
                    run.DurationMs = sw.ElapsedMilliseconds;
                    run.LastHeartbeatAt = DateTime.UtcNow;
                    await _runStore.UpdateAsync(run, CancellationToken.None);

                    await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.Error,
                        new { error = ex.Message, type = ex.GetType().Name },
                        sw.ElapsedMilliseconds, CancellationToken.None);
                }

                throw;
            }

            // 如果启用了 Run 追踪，创建包含 RunId 和 Status 的新结果
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

    private static AgentRunResult EnsureResultStatus(
        AgentRunResult result,
        AgentRunStatus defaultStatus = AgentRunStatus.Completed)
    {
        return result.Status.HasValue
            ? result
            : result.CloneWith(status: ResolveRunStatus(result.FinishReason, defaultStatus));
    }

    private static AgentRunStatus ResolveRunStatus(
        string? finishReason,
        AgentRunStatus defaultStatus = AgentRunStatus.Completed)
    {
        return finishReason switch
        {
            FinishReasons.AwaitingApproval => AgentRunStatus.AwaitingApproval,
            FinishReasons.RequiresClarification => AgentRunStatus.RequiresClarification,
            FinishReasons.Error or
            FinishReasons.Failed or
            FinishReasons.GuardrailRejected or
            FinishReasons.QuotaExceeded or
            FinishReasons.Rejected or
            FinishReasons.MaxHandoffs => AgentRunStatus.Failed,
            _ => defaultStatus
        };
    }

    private static bool ShouldGenerateThreadTitle(string? finishReason)
    {
        return finishReason switch
        {
            FinishReasons.GuardrailRejected or
            FinishReasons.QuotaExceeded or
            FinishReasons.Error or
            FinishReasons.Failed or
            FinishReasons.Rejected or
            FinishReasons.MaxHandoffs or
            FinishReasons.MaxToolIterations => false,
            _ => true
        };
    }

    /// <summary>执行一次 AI 运行（流式）</summary>
    public async IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var previousRequest = _executionContextAccessor.CurrentRequest;
        var previousProperties = previousRequest != null
            ? _executionContextAccessor.CaptureProperties()
            : null;
        _executionContextAccessor.CurrentRequest = request;
        _executionContextAccessor.ClearProperties();

        try
        {
            if (request.WorkflowId.HasValue)
            {
                var workflowStopwatch = Stopwatch.StartNew();
                WorkflowExecutionResultDto? lastWorkflowEvent = null;

                await foreach (var workflowEvent in _workflowService.RunStreamingAsync(
                    request.WorkflowId.Value,
                    request.UserMessage ?? string.Empty,
                    request.UserId,
                    cancellationToken).WithCancellation(cancellationToken))
                {
                    lastWorkflowEvent = workflowEvent;
                    if (!string.IsNullOrWhiteSpace(workflowEvent.Output))
                    {
                        var isCompleted = IsWorkflowTerminalStatus(workflowEvent.Status);
                        yield return new AgentStreamChunk
                        {
                            Text = workflowEvent.Output,
                            FinishReason = isCompleted
                                ? MapWorkflowFinishReason(workflowEvent.Status, streaming: true)
                                : null
                        };
                    }
                }

                workflowStopwatch.Stop();
                if (lastWorkflowEvent != null)
                {
                    await PublishRunCompletedEventAsync(
                        request,
                        new AgentRunResult
                        {
                            Response = lastWorkflowEvent.Output,
                            RunId = lastWorkflowEvent.RunId,
                            FinishReason = MapWorkflowFinishReason(lastWorkflowEvent.Status, streaming: true),
                            Status = MapWorkflowStatus(lastWorkflowEvent.Status)
                        },
                        null,
                        workflowStopwatch.ElapsedMilliseconds,
                        true,
                        "Workflow");
                }

                yield break;
            }

            var sw = Stopwatch.StartNew();

            // 0. 自动模型切换（当 ReasoningEffort != None 且当前模型不支持推理时，查找 "think" 别名）
            var effectiveModel = ResolveThinkingModel(request);

            // 1. 解析 Agent
            var resolution = await _agentResolver.ResolveAgentAsync(
                request.AgentId, request.Provider, effectiveModel, request.ToolGroups, cancellationToken);

            if (!resolution.IsSuccess)
            {
                yield return new AgentStreamChunk
                {
                    Error = $"Agent resolution failed: {resolution.ErrorCode}",
                    FinishReason = FinishReasons.Error
                };
                yield break;
            }

            // 2. 创建 Run（如果启用追踪）
            AgentRun? run = null;
            if (request.EnableRunTracking)
            {
                run = await GetOrCreateRunAsync(request, resolution, cancellationToken);
                _executionContextAccessor.Properties[ContextPropertyKeys.CurrentRunId] = run.Id;
            }

            // 发布运行开始事件
            await PublishRunStartedEventAsync(request, run, true, resolution.Provider, resolution.Model, resolution.ExecutionMode);

            // 3. 构建中间件上下文
            var context = new AiMiddlewareContext
            {
                Request = request,
                Agent = resolution,
                Run = run,
                ServiceProvider = _serviceProvider
            };

            // 4. 构建流式中间件管道
            var middlewares = ResolveMiddlewares();
            var pipeline = new AiMiddlewarePipeline();
            foreach (var middleware in middlewares)
            {
                pipeline.Use(middleware);
            }

            AiStreamingMiddlewareDelegate coreExecutor = (ctx, ct) =>
            {
                return ExecuteCoreStreamingAsync(ctx, ct);
            };

            var streamDelegate = pipeline.BuildStreaming(coreExecutor);

            // 5. 执行流式管道
            var totalInputTokens = 0;
            var totalOutputTokens = 0;
            string? lastFinishReason = null;
            string? lastModel = null;
            var completedNormally = false;
            var responseBuilder = new StringBuilder();

            try
            {
                await foreach (var chunk in streamDelegate(context, cancellationToken).WithCancellation(cancellationToken))
                {
                    chunk.Model ??= context.EffectiveModel ?? resolution.Model;
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
                    if (!string.IsNullOrEmpty(chunk.Text))
                    {
                        responseBuilder.Append(chunk.Text);
                    }

                    // StreamMode 过滤：仅输出客户端请求的粒度级别
                    if (request.StreamMode.HasFlag(chunk.Mode))
                    {
                        yield return chunk;
                    }
                }
                completedNormally = true;
            }
            finally
            {
                // 6. 更新 Run 状态（无论成功或失败都确保更新）
                // 使用 CancellationToken.None，因为原始 token 可能已取消
                // 整个 finally 块 try/catch 包裹，防止 DB 异常传播到调用方覆盖原始异常
                try
                {
                    if (run != null)
                    {
                        sw.Stop();
                        run.DurationMs = sw.ElapsedMilliseconds;
                        run.TotalInputTokens = totalInputTokens;
                        run.TotalOutputTokens = totalOutputTokens;
                        run.LastHeartbeatAt = DateTime.UtcNow;

                        if (completedNormally)
                        {
                            var streamResult = EnsureResultStatus(new AgentRunResult
                            {
                                Response = responseBuilder.ToString(),
                                Usage = new TokenUsageDto { InputTokens = totalInputTokens, OutputTokens = totalOutputTokens },
                                FinishReason = lastFinishReason,
                                Model = lastModel,
                                Provider = context.EffectiveProvider ?? resolution.Provider
                            });

                            if (lastFinishReason == "max_tool_iterations")
                            {
                                _logger.LogWarning(
                                    "Agent run {RunId} reached MaxToolIterations limit — response may be incomplete",
                                    run.Id);
                            }

                            run.Status = streamResult.Status!.Value;
                            run.Error = run.Status == AgentRunStatus.Failed ? streamResult.Response : null;
                            run.OutputSummary = Truncate(streamResult.Response, 500);
                            await _runStore.UpdateAsync(run, CancellationToken.None);

                            await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.StreamCompleted,
                                new { finishReason = lastFinishReason, inputTokens = totalInputTokens, outputTokens = totalOutputTokens },
                                sw.ElapsedMilliseconds, CancellationToken.None);

                            // 发布流式运行完成事件
                            await PublishRunCompletedEventAsync(
                                request,
                                streamResult,
                                run,
                                sw.ElapsedMilliseconds,
                                true,
                                context.EffectiveProvider ?? resolution.Provider);
                        }
                        else if (cancellationToken.IsCancellationRequested)
                        {
                            run.Status = AgentRunStatus.Cancelled;
                            run.Error = "Streaming was cancelled by the caller";
                            await _runStore.UpdateAsync(run, CancellationToken.None);

                            await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.StreamCancelled,
                                new { finishReason = lastFinishReason },
                                sw.ElapsedMilliseconds, CancellationToken.None);
                        }
                        else
                        {
                            run.Status = AgentRunStatus.Failed;
                            run.Error = "Streaming execution failed";
                            await _runStore.UpdateAsync(run, CancellationToken.None);

                            await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.Error,
                                new { error = run.Error, finishReason = lastFinishReason },
                                sw.ElapsedMilliseconds, CancellationToken.None);

                            // 发布流式运行失败事件
                            await PublishRunFailedEventAsync(
                                request, run,
                                new InvalidOperationException("Streaming execution failed"),
                                sw.ElapsedMilliseconds, true);
                        }
                    }

                    // 标题生成独立于 Run 追踪 — 即使 EnableRunTracking=false 也应执行
                    if (completedNormally
                        && context.IsNewThread
                        && request.ThreadId.HasValue
                        && !string.IsNullOrWhiteSpace(request.UserMessage)
                        && ShouldGenerateThreadTitle(lastFinishReason))
                    {
                        await HandleNewThreadTitleAsync(request, new AgentRunResult
                        {
                            Response = responseBuilder.ToString(),
                            Usage = new TokenUsageDto { InputTokens = totalInputTokens, OutputTokens = totalOutputTokens },
                            FinishReason = lastFinishReason,
                            Model = lastModel,
                            Provider = context.EffectiveProvider ?? resolution.Provider
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update streaming run status for RunId={RunId}", run?.Id);
                }
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

    /// <summary>恢复被中断的运行</summary>
    public async Task<AgentRunResult> ResumeAsync(Guid runId, ResumeRunInput? input = null, CancellationToken cancellationToken = default)
    {
        var run = await _runStore.GetWithNodesAsync(runId, cancellationToken);
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
            return await ResumeWorkflowRunAsync(run, input, cancellationToken);
        }

        var previousStatus = run.Status;
        run.Status = AgentRunStatus.Running;
        await _runStore.UpdateAsync(run, cancellationToken);

        await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.RunResumed,
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
                await _runStore.UpdateNodeAsync(awaitingNode, cancellationToken);
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
                await _runStore.UpdateNodeAsync(retryNode, cancellationToken);
            }
        }

        var resumeRequest = new AgentRunRequest
        {
            OperationType = AIOperationType.AgentRun,
            AgentId = run.AgentId,
            ThreadId = run.ThreadId,
            UserMessage = input?.UserMessage ?? run.InputSummary,
            EnableRunTracking = false
        };

        var sw = Stopwatch.StartNew();
        AgentRunResult result;
        try
        {
            result = EnsureResultStatus(await RunAsync(resumeRequest, cancellationToken));
            sw.Stop();

            // 更新原始 Run 为完成状态
            run.Status = result.Status!.Value;
            run.Error = run.Status == AgentRunStatus.Failed ? result.Response : null;
            run.OutputSummary = Truncate(result.Response, 500);
            run.DurationMs = sw.ElapsedMilliseconds;
            if (result.Usage != null)
            {
                run.TotalInputTokens = result.Usage.InputTokens;
                run.TotalOutputTokens = result.Usage.OutputTokens;
            }
            await _runStore.UpdateAsync(run, cancellationToken);
            await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.RunCompleted, result, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();

            // 更新原始 Run 为失败状态
            run.Status = AgentRunStatus.Failed;
            run.Error = ex.Message;
            run.DurationMs = sw.ElapsedMilliseconds;
            await _runStore.UpdateAsync(run, CancellationToken.None);
            await RecordTraceAsync(run.Id, null, AgentTraceEventTypes.Error,
                new { error = ex.Message, type = ex.GetType().Name },
                sw.ElapsedMilliseconds, CancellationToken.None);
            throw;
        }

        return result.CloneWith(runId: run.Id, status: run.Status);
    }

}
