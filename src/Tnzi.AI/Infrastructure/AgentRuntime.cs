namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 统一 AI 运行入口 — 所有 AI 执行（chat、workflow、agent run）都通过此入口。
/// 整合中间件管道 + 执行策略 + Run 追踪。
/// </summary>
public class AgentRuntime : IAgentRuntime
{
    private readonly IAgentResolver _agentResolver;
    private readonly IRunStore _runStore;
    private readonly ITraceStore _traceStore;
    private readonly IWorkflowService _workflowService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentRuntime> _logger;

    public AgentRuntime(
        IAgentResolver agentResolver,
        IRunStore runStore,
        ITraceStore traceStore,
        IWorkflowService workflowService,
        IServiceProvider serviceProvider,
        ILogger<AgentRuntime> logger)
    {
        _agentResolver = Check.NotNull(agentResolver);
        _runStore = Check.NotNull(runStore);
        _traceStore = Check.NotNull(traceStore);
        _workflowService = Check.NotNull(workflowService);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    /// <summary>执行一次 AI 运行（非流式）</summary>
    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (request.WorkflowId.HasValue)
        {
            return await ExecuteWorkflowAsync(request, cancellationToken);
        }

        var sw = Stopwatch.StartNew();

        // 1. 解析 Agent
        var resolution = await _agentResolver.ResolveAgentAsync(
            request.AgentId, request.Provider, request.Model, request.ToolGroups, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return new AgentRunResult
            {
                Response = $"Agent resolution failed: {resolution.ErrorCode}",
                FinishReason = "error"
            };
        }

        // 2. 创建 Run（如果启用追踪）
        AgentRun? run = null;
        if (request.EnableRunTracking)
        {
            run = await CreateRunAsync(request, resolution, cancellationToken);
        }

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

            // 7. 更新 Run 状态为完成
            if (run != null)
            {
                sw.Stop();
                run.Status = AgentRunStatus.Completed;
                run.OutputSummary = Truncate(result.Response, 500);
                run.DurationMs = sw.ElapsedMilliseconds;
                if (result.Usage != null)
                {
                    run.TotalInputTokens = result.Usage.PromptTokens;
                    run.TotalOutputTokens = result.Usage.CompletionTokens;
                }
                await _runStore.UpdateAsync(run, cancellationToken);

                // 记录 Trace
                await RecordTraceAsync(run.Id, null, "run_completed", result, sw.ElapsedMilliseconds, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AgentRuntime execution failed for request AgentId={AgentId}", request.AgentId);

            // 更新 Run 状态为失败
            if (run != null)
            {
                sw.Stop();
                run.Status = AgentRunStatus.Failed;
                run.Error = ex.Message;
                run.DurationMs = sw.ElapsedMilliseconds;
                await _runStore.UpdateAsync(run, CancellationToken.None);

                await RecordTraceAsync(run.Id, null, "error",
                    new { error = ex.Message, type = ex.GetType().Name },
                    sw.ElapsedMilliseconds, CancellationToken.None);
            }

            throw;
        }

        // 如果启用了 Run 追踪，创建包含 RunId 和 Status 的新结果
        if (run != null)
        {
            return new AgentRunResult
            {
                Response = result.Response,
                RunId = run.Id,
                ThreadId = result.ThreadId,
                Usage = result.Usage,
                Citations = result.Citations,
                FinishReason = result.FinishReason,
                Status = run.Status
            };
        }

        return result;
    }

    /// <summary>执行一次 AI 运行（流式）</summary>
    public async IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (request.WorkflowId.HasValue)
        {
            await foreach (var chunk in ExecuteWorkflowStreamingAsync(request, cancellationToken).WithCancellation(cancellationToken))
            {
                yield return chunk;
            }
            yield break;
        }

        var sw = Stopwatch.StartNew();

        // 1. 解析 Agent
        var resolution = await _agentResolver.ResolveAgentAsync(
            request.AgentId, request.Provider, request.Model, request.ToolGroups, cancellationToken);

        if (!resolution.IsSuccess)
        {
            yield return new AgentStreamChunk
            {
                Error = $"Agent resolution failed: {resolution.ErrorCode}",
                FinishReason = "error"
            };
            yield break;
        }

        // 2. 创建 Run（如果启用追踪）
        AgentRun? run = null;
        if (request.EnableRunTracking)
        {
            run = await CreateRunAsync(request, resolution, cancellationToken);
        }

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
        var completedNormally = false;

        try
        {
            await foreach (var chunk in streamDelegate(context, cancellationToken).WithCancellation(cancellationToken))
            {
                if (chunk.Usage != null)
                {
                    totalInputTokens += chunk.Usage.PromptTokens;
                    totalOutputTokens += chunk.Usage.CompletionTokens;
                }
                if (chunk.FinishReason != null)
                {
                    lastFinishReason = chunk.FinishReason;
                }

                yield return chunk;
            }
            completedNormally = true;
        }
        finally
        {
            // 6. 更新 Run 状态（无论成功或失败都确保更新）
            // 使用 CancellationToken.None，因为原始 token 可能已取消
            if (run != null)
            {
                sw.Stop();
                run.DurationMs = sw.ElapsedMilliseconds;
                run.TotalInputTokens = totalInputTokens;
                run.TotalOutputTokens = totalOutputTokens;

                if (completedNormally)
                {
                    run.Status = AgentRunStatus.Completed;
                    await _runStore.UpdateAsync(run, CancellationToken.None);

                    await RecordTraceAsync(run.Id, null, "stream_completed",
                        new { finishReason = lastFinishReason, inputTokens = totalInputTokens, outputTokens = totalOutputTokens },
                        sw.ElapsedMilliseconds, CancellationToken.None);
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    run.Status = AgentRunStatus.Cancelled;
                    run.Error = "Streaming was cancelled by the caller";
                    await _runStore.UpdateAsync(run, CancellationToken.None);

                    await RecordTraceAsync(run.Id, null, "stream_cancelled",
                        new { finishReason = lastFinishReason },
                        sw.ElapsedMilliseconds, CancellationToken.None);
                }
                else
                {
                    run.Status = AgentRunStatus.Failed;
                    run.Error = "Streaming execution failed";
                    await _runStore.UpdateAsync(run, CancellationToken.None);

                    await RecordTraceAsync(run.Id, null, "error",
                        new { error = run.Error, finishReason = lastFinishReason },
                        sw.ElapsedMilliseconds, CancellationToken.None);
                }
            }
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

        if (run.Status != AgentRunStatus.AwaitingApproval && run.Status != AgentRunStatus.Failed)
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

        await RecordTraceAsync(run.Id, null, "run_resumed",
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
            AgentId = run.AgentId,
            ThreadId = run.ThreadId,
            UserMessage = input?.UserMessage ?? run.InputSummary,
            EnableRunTracking = false
        };

        var sw = Stopwatch.StartNew();
        AgentRunResult result;
        try
        {
            result = await RunAsync(resumeRequest, cancellationToken);
            sw.Stop();

            // 更新原始 Run 为完成状态
            run.Status = AgentRunStatus.Completed;
            run.OutputSummary = Truncate(result.Response, 500);
            run.DurationMs = sw.ElapsedMilliseconds;
            if (result.Usage != null)
            {
                run.TotalInputTokens = result.Usage.PromptTokens;
                run.TotalOutputTokens = result.Usage.CompletionTokens;
            }
            await _runStore.UpdateAsync(run, cancellationToken);
            await RecordTraceAsync(run.Id, null, "run_completed", result, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();

            // 更新原始 Run 为失败状态
            run.Status = AgentRunStatus.Failed;
            run.Error = ex.Message;
            run.DurationMs = sw.ElapsedMilliseconds;
            await _runStore.UpdateAsync(run, CancellationToken.None);
            await RecordTraceAsync(run.Id, null, "error",
                new { error = ex.Message, type = ex.GetType().Name },
                sw.ElapsedMilliseconds, CancellationToken.None);
            throw;
        }

        return new AgentRunResult
        {
            Response = result.Response,
            RunId = run.Id,
            ThreadId = result.ThreadId,
            Usage = result.Usage,
            Citations = result.Citations,
            FinishReason = result.FinishReason,
            Status = run.Status
        };
    }

    private async Task<AgentRunResult> ResumeWorkflowRunAsync(AgentRun run, ResumeRunInput? input, CancellationToken cancellationToken)
    {
        var executionId = run.WorkflowExecutionId!;
        var awaitingNodes = run.Nodes
            .Where(n => n.Status == AgentRunNodeStatus.AwaitingApproval)
            .ToList();

        if (input?.ApprovalDecision != null)
        {
            var isApproved = input.ApprovalDecision.Equals("approve", StringComparison.OrdinalIgnoreCase);

            if (!isApproved)
            {
                foreach (var node in awaitingNodes)
                {
                    var rejectResult = await _workflowService.RejectStepAsync(
                        executionId,
                        node.NodeName,
                        input.ApprovalComment ?? "Rejected by reviewer",
                        cancellationToken);

                    if (!rejectResult.Succeeded)
                    {
                        throw new BusinessException(
                            rejectResult.Message ?? "Failed to reject workflow step",
                            rejectResult.ErrorCode ?? ErrorCodes.WorkflowFailed,
                            rejectResult.Code ?? 500);
                    }

                    node.Status = AgentRunNodeStatus.Rejected;
                    node.Error = input.ApprovalComment;
                    await _runStore.UpdateNodeAsync(node, cancellationToken);
                }

                run.Status = AgentRunStatus.Failed;
                run.Error = input.ApprovalComment ?? "Rejected by reviewer";
                await _runStore.UpdateAsync(run, cancellationToken);

                await RecordTraceAsync(run.Id, null, "run_rejected",
                    new { workflowExecutionId = executionId, feedback = input.ApprovalComment },
                    0, cancellationToken);

                return new AgentRunResult
                {
                    Response = run.Error,
                    RunId = run.Id,
                    ThreadId = run.ThreadId,
                    FinishReason = "rejected",
                    Status = run.Status
                };
            }

            foreach (var node in awaitingNodes)
            {
                var approveResult = await _workflowService.ApproveStepAsync(
                    executionId,
                    node.NodeName,
                    input.ApprovalComment,
                    cancellationToken);

                if (!approveResult.Succeeded)
                {
                    throw new BusinessException(
                        approveResult.Message ?? "Failed to approve workflow step",
                        approveResult.ErrorCode ?? ErrorCodes.WorkflowFailed,
                        approveResult.Code ?? 500);
                }

                node.Status = AgentRunNodeStatus.Approved;
                node.Output = input.ApprovalComment ?? node.Output;
                await _runStore.UpdateNodeAsync(node, cancellationToken);
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

                await RecordTraceAsync(run.Id, retryNode.Id, "node_retry_requested",
                    new { workflowExecutionId = executionId, nodeName = retryNode.NodeName, retryCount = retryNode.RetryCount },
                    0, cancellationToken);
            }
        }

        run.Status = AgentRunStatus.Running;
        await _runStore.UpdateAsync(run, cancellationToken);

        var resumeResult = await _workflowService.ResumeAsync(executionId, cancellationToken);
        if (!resumeResult.Succeeded || resumeResult.Data == null)
        {
            run.Status = AgentRunStatus.Failed;
            run.Error = resumeResult.Message ?? "Failed to resume workflow execution";
            await _runStore.UpdateAsync(run, CancellationToken.None);

            throw new BusinessException(
                run.Error,
                resumeResult.ErrorCode ?? ErrorCodes.WorkflowFailed,
                resumeResult.Code ?? 500);
        }

        run.Status = string.Equals(resumeResult.Data.Status, "AwaitingApproval", StringComparison.OrdinalIgnoreCase)
            ? AgentRunStatus.AwaitingApproval
            : AgentRunStatus.Completed;
        run.OutputSummary = Truncate(resumeResult.Data.Output, 500);
        run.Error = null;
        await _runStore.UpdateAsync(run, cancellationToken);

        await RecordTraceAsync(run.Id, null, "run_resumed",
            new { workflowExecutionId = executionId, status = resumeResult.Data.Status },
            0, cancellationToken);

        return new AgentRunResult
        {
            Response = resumeResult.Data.Output,
            RunId = run.Id,
            ThreadId = run.ThreadId,
            FinishReason = run.Status == AgentRunStatus.AwaitingApproval ? "awaiting_approval" : "completed",
            Status = run.Status
        };
    }

    /// <summary>
    /// 通过 WorkflowService 执行工作流运行。
    /// </summary>
    private async Task<AgentRunResult> ExecuteWorkflowAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var workflowId = request.WorkflowId!.Value;
        var input = request.UserMessage ?? string.Empty;
        var result = await _workflowService.RunAsync(workflowId, input, request.UserId, cancellationToken);

        if (!result.Succeeded || result.Data == null)
        {
            throw new BusinessException(
                result.Message ?? "Workflow execution failed",
                result.ErrorCode ?? ErrorCodes.WorkflowFailed,
                result.Code ?? 500);
        }

        return new AgentRunResult
        {
            Response = result.Data.Output,
            RunId = result.Data.RunId,
            FinishReason = string.Equals(result.Data.Status, "AwaitingApproval", StringComparison.OrdinalIgnoreCase)
                ? "awaiting_approval"
                : string.Equals(result.Data.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                    ? "failed"
                    : "completed",
            Status = string.Equals(result.Data.Status, "AwaitingApproval", StringComparison.OrdinalIgnoreCase)
                ? AgentRunStatus.AwaitingApproval
                : string.Equals(result.Data.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                    ? AgentRunStatus.Failed
                    : AgentRunStatus.Completed
        };
    }

    /// <summary>
    /// 通过 WorkflowService 执行流式工作流运行。
    /// </summary>
    private async IAsyncEnumerable<AgentStreamChunk> ExecuteWorkflowStreamingAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var workflowId = request.WorkflowId!.Value;
        var input = request.UserMessage ?? string.Empty;

        await foreach (var evt in _workflowService.RunStreamingAsync(workflowId, input, request.UserId, cancellationToken).WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(evt.Output))
            {
                var isCompleted = string.Equals(evt.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(evt.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                    || evt.Status.StartsWith("PartialFailure", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(evt.Status, "AwaitingApproval", StringComparison.OrdinalIgnoreCase);

                yield return new AgentStreamChunk
                {
                    Text = evt.Output,
                    FinishReason = isCompleted
                        ? (string.Equals(evt.Status, "AwaitingApproval", StringComparison.OrdinalIgnoreCase)
                            ? "awaiting_approval"
                            : string.Equals(evt.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                                ? "failed"
                            : "stop")
                        : null
                };
            }
        }
    }

    /// <summary>
    /// 核心执行器（非流式）— 管道最内层，委托给执行策略
    /// </summary>
    private async Task<AgentRunResult> ExecuteCoreAsync(AiMiddlewareContext context, CancellationToken ct)
    {
        var resolution = context.Agent;
        var agent = resolution.Agent!;

        // 构建消息列表（包含中间件注入的消息）
        var messages = new List<ChatMessage>(context.Messages);
        if (!string.IsNullOrWhiteSpace(context.Request.UserMessage))
        {
            var userMessage = await _agentResolver.BuildChatMessageAsync(
                context.Request.UserMessage, context.Request.ContentParts, ct);
            messages.Add(userMessage);
        }

        // 解析并执行策略
        var strategy = ExecutionStrategyResolver.Resolve(resolution.ExecutionMode, resolution.AgentConfiguration);
        var strategyContext = new ExecutionStrategyContext
        {
            AgentFactory = _serviceProvider.GetRequiredService<IAgentFactory>(),
            AgentRepository = _serviceProvider.GetRequiredService<IRepository<Agent, Guid>>(),
            ServiceProvider = _serviceProvider,
            Logger = _logger
        };

        using (ToolContext.Establish(_serviceProvider, ct))
        {
            var executionResult = await strategy.ExecuteAsync(agent, messages, strategyContext, ct);
            var response = executionResult.Response;

            return new AgentRunResult
            {
                Response = response.Text ?? string.Empty,
                ThreadId = context.Request.ThreadId,
                Usage = executionResult.AggregatedUsage ?? response.Usage,
                Citations = context.Citations.Count > 0 ? context.Citations : null,
                FinishReason = response.FinishReason,
                HandoffPath = executionResult.HandoffPath,
                FinalAgentName = executionResult.FinalAgentName
            };
        }
    }

    /// <summary>
    /// 核心执行器（流式）— 管道最内层，委托给执行策略
    /// </summary>
    private async IAsyncEnumerable<AgentStreamChunk> ExecuteCoreStreamingAsync(
        AiMiddlewareContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var resolution = context.Agent;
        var agent = resolution.Agent!;

        // 构建消息列表
        var messages = new List<ChatMessage>(context.Messages);
        if (!string.IsNullOrWhiteSpace(context.Request.UserMessage))
        {
            var userMessage = await _agentResolver.BuildChatMessageAsync(
                context.Request.UserMessage, context.Request.ContentParts, ct);
            messages.Add(userMessage);
        }

        var strategy = ExecutionStrategyResolver.Resolve(resolution.ExecutionMode, resolution.AgentConfiguration);
        var strategyContext = new ExecutionStrategyContext
        {
            AgentFactory = _serviceProvider.GetRequiredService<IAgentFactory>(),
            AgentRepository = _serviceProvider.GetRequiredService<IRepository<Agent, Guid>>(),
            ServiceProvider = _serviceProvider,
            Logger = _logger
        };

        using var scope = ToolContext.Establish(_serviceProvider, ct);

        await foreach (var chunk in strategy.ExecuteStreamingAsync(agent, messages, strategyContext, ct).WithCancellation(ct))
        {
            yield return chunk;
        }
    }

    /// <summary>创建 Run 记录</summary>
    private async Task<AgentRun> CreateRunAsync(AgentRunRequest request, AgentResolution resolution, CancellationToken ct)
    {
        var run = new AgentRun
        {
            AgentId = request.AgentId ?? resolution.AgentId,
            ThreadId = request.ThreadId,
            WorkflowDefinitionId = request.WorkflowId,
            Status = AgentRunStatus.Running,
            ExecutionMode = resolution.ExecutionMode,
            InputSummary = Truncate(request.UserMessage, 500)
        };

        return await _runStore.CreateAsync(run, ct);
    }

    /// <summary>记录 Trace 条目</summary>
    private async Task RecordTraceAsync(Guid runId, Guid? nodeId, string eventType, object? eventData, long durationMs, CancellationToken ct)
    {
        try
        {
            var trace = new AgentRunTrace
            {
                RunId = runId,
                NodeId = nodeId,
                EventType = eventType,
                EventData = eventData?.ToJsonString(camelCase: true),
                DurationMs = durationMs
            };
            await _traceStore.AddAsync(trace, ct);
        }
        catch (Exception ex)
        {
            // Trace 记录失败不影响主流程
            _logger.LogWarning(ex, "Failed to record trace for Run {RunId}", runId);
        }
    }

    /// <summary>
    /// 从 DI 解析所有已注册的中间件，按 Order 排序。
    /// 中间件按具体类型注册（框架程序集不使用自动注册），因此逐一解析。
    /// </summary>
    private List<IAiMiddleware> ResolveMiddlewares()
    {
        var middlewares = new List<IAiMiddleware>();

        // 按具体类型解析（与 AIModule 注册方式一致）
        ResolveAndAdd<QuotaMiddleware>(middlewares);
        ResolveAndAdd<InputGuardrailMiddleware>(middlewares);
        ResolveAndAdd<HistoryMiddleware>(middlewares);
        ResolveAndAdd<ContextInjectionMiddleware>(middlewares);
        ResolveAndAdd<UsageLoggingMiddleware>(middlewares);
        ResolveAndAdd<OutputGuardrailMiddleware>(middlewares);

        // 同时获取通过 IAiMiddleware 接口注册的中间件（用户自定义）
        var interfaceMiddlewares = _serviceProvider.GetServices<IAiMiddleware>();
        foreach (var m in interfaceMiddlewares)
        {
            if (!middlewares.Contains(m))
            {
                middlewares.Add(m);
            }
        }

        return middlewares.OrderBy(m => m.Order).ToList();
    }

    private void ResolveAndAdd<T>(List<IAiMiddleware> middlewares) where T : class, IAiMiddleware
    {
        var middleware = _serviceProvider.GetService<T>();
        if (middleware != null)
        {
            middlewares.Add(middleware);
        }
    }

    /// <summary>截断字符串</summary>
    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
