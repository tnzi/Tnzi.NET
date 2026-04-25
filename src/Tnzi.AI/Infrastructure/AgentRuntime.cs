namespace Tnzi.AI.Infrastructure;

/// <summary>
/// Unified AI execution entry point — all AI execution (chat, workflow, agent run) goes through this.
/// Composes middleware pipeline + execution strategy + run tracking.
/// </summary>
public partial class AgentRuntime : IAgentRuntime
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
        ILogger<AgentRuntime> logger)
    {
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
        _executionContextAccessor.CurrentRequest = request;
        _executionContextAccessor.ClearProperties();

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
            if (!setup!.Resolution.IsSuccess)
            {
                throw CreateAgentResolutionException(setup.Resolution);
            }

            var resolution = setup.Resolution;
            var run = setup.Run;
            var context = setup.Context;

            // Get (or build) middleware pipeline delegate
            _cachedPipelineDelegate ??= BuildPipelineDelegate();

            // Execute pipeline
            AgentRunResult result;
            try
            {
                result = await _cachedPipelineDelegate(context, cancellationToken);
                if (run != null)
                {
                    result = AgentRunStatusResolver.EnsureStatus(result);
                    sw.Stop();
                    await _runTracker.UpdateRunOnCompletionAsync(run, result, sw.ElapsedMilliseconds, cancellationToken);

                    if (result.FinishReason == FinishReasons.MaxToolIterations)
                    {
                        _logger.LogWarning(
                            "Agent run {RunId} reached MaxToolIterations limit — response may be incomplete",
                            run.Id);
                    }
                }

                await _eventPublisher.PublishRunCompletedEventAsync(
                    request, result, run,
                    sw.ElapsedMilliseconds, false,
                    context.EffectiveProvider ?? resolution.Provider);

                if (context.IsNewThread
                    && request.ThreadId.HasValue
                    && !string.IsNullOrWhiteSpace(request.UserMessage)
                    && AgentRunStatusResolver.ShouldGenerateThreadTitle(result.FinishReason))
                {
                    await _eventPublisher.HandleNewThreadTitleAsync(request, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentRuntime execution failed for request AgentId={AgentId}", request.AgentId);

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
        _executionContextAccessor.CurrentRequest = request;
        _executionContextAccessor.ClearProperties();

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
            if (!setup!.Resolution.IsSuccess)
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
    /// Streaming workflow path — delegates to IWorkflowDelegator.ExecuteWorkflowStreamingAsync
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
                    Response = lastChunk.Text,
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
    /// Streaming pipeline path — runs the middleware pipeline, aggregates tokens/usage,
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

        _cachedStreamingPipelineDelegate ??= BuildStreamingPipelineDelegate();

        var sw = Stopwatch.StartNew();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        string? lastFinishReason = null;
        string? lastModel = null;
        var completedNormally = false;
        var responseBuilder = new StringBuilder();
        var defaultModel = context.EffectiveModel ?? resolution.Model;

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
            await FinalizeStreamingAsync(
                setup, request, sw.ElapsedMilliseconds,
                completedNormally, cancellationToken.IsCancellationRequested,
                totalInputTokens, totalOutputTokens, lastFinishReason, lastModel,
                responseBuilder.ToString());
        }
    }

    /// <summary>
    /// Finalize streaming run state — persists run, records traces, publishes events.
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
        string response)
    {
        var resolution = setup.Resolution;
        var run = setup.Run;
        var context = setup.Context;

        try
        {
            AgentRunResult? streamResult = null;

            if (run != null)
            {
                if (completedNormally)
                {
                    streamResult = AgentRunStatusResolver.EnsureStatus(new AgentRunResult
                    {
                        Response = response,
                        Usage = new TokenUsageDto { InputTokens = totalInputTokens, OutputTokens = totalOutputTokens },
                        FinishReason = lastFinishReason,
                        Model = lastModel,
                        Provider = context.EffectiveProvider ?? resolution.Provider
                    });

                    if (lastFinishReason == FinishReasons.MaxToolIterations)
                    {
                        _logger.LogWarning(
                            "Agent run {RunId} reached MaxToolIterations limit — response may be incomplete",
                            run.Id);
                    }

                    await _runTracker.FinalizeStreamingCompletedAsync(
                        run, streamResult, totalInputTokens, totalOutputTokens, durationMs, CancellationToken.None);

                    await _eventPublisher.PublishRunCompletedEventAsync(
                        request, streamResult, run,
                        durationMs, true,
                        context.EffectiveProvider ?? resolution.Provider);
                }
                else if (cancelled)
                {
                    await _runTracker.FinalizeStreamingCancelledAsync(run, lastFinishReason, durationMs, CancellationToken.None);
                }
                else
                {
                    await _runTracker.FinalizeStreamingFailedAsync(run, lastFinishReason, durationMs, CancellationToken.None);
                    await _eventPublisher.PublishRunFailedEventAsync(
                        request, run,
                        new InvalidOperationException("Streaming execution failed"),
                        durationMs, true);
                }
            }

            if (completedNormally
                && context.IsNewThread
                && request.ThreadId.HasValue
                && !string.IsNullOrWhiteSpace(request.UserMessage)
                && AgentRunStatusResolver.ShouldGenerateThreadTitle(lastFinishReason))
            {
                streamResult ??= new AgentRunResult
                {
                    Response = response,
                    Usage = new TokenUsageDto { InputTokens = totalInputTokens, OutputTokens = totalOutputTokens },
                    FinishReason = lastFinishReason,
                    Model = lastModel,
                    Provider = context.EffectiveProvider ?? resolution.Provider
                };
                await _eventPublisher.HandleNewThreadTitleAsync(request, streamResult);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to finalize streaming run for RunId={RunId}", run?.Id);
        }
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

        var previousStatus = run.Status;
        run.Status = AgentRunStatus.Running;
        await _runTracker.UpdateAsync(run, cancellationToken);

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
            result = AgentRunStatusResolver.EnsureStatus(await RunAsync(resumeRequest, cancellationToken));
            sw.Stop();

            run.Status = result.Status!.Value;
            run.Error = run.Status == AgentRunStatus.Failed ? result.Response : null;
            run.OutputSummary = StringTruncator.Truncate(result.Response, 500);
            run.DurationMs = sw.ElapsedMilliseconds;
            if (result.Usage != null)
            {
                run.TotalInputTokens = result.Usage.InputTokens;
                run.TotalOutputTokens = result.Usage.OutputTokens;
            }
            await _runTracker.UpdateAsync(run, cancellationToken);
            await _runTracker.RecordTraceAsync(run.Id, null, AgentTraceEventTypes.RunCompleted, result, sw.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();

            run.Status = AgentRunStatus.Failed;
            run.Error = ex.Message;
            run.DurationMs = sw.ElapsedMilliseconds;
            await _runTracker.UpdateAsync(run, CancellationToken.None);
            await _runTracker.RecordTraceAsync(run.Id, null, AgentTraceEventTypes.Error,
                new { error = ex.Message, type = ex.GetType().Name },
                sw.ElapsedMilliseconds, CancellationToken.None);
            throw;
        }

        return result.CloneWith(runId: run.Id, status: run.Status);
    }
}
