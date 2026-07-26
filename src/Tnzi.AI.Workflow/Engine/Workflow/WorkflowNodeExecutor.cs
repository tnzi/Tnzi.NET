namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// 工作流节点执行器 - 负责执行单个节点（含重试、超时、Trace 记录）
/// </summary>
public class WorkflowNodeExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkflowNodeExecutor> _logger;

    public WorkflowNodeExecutor(IServiceProvider serviceProvider, ILogger<WorkflowNodeExecutor> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 执行单个工作流节点
    /// </summary>
    /// <param name="step">步骤定义</param>
    /// <param name="state">工作流状态</param>
    /// <param name="run">关联的运行实例（可选，用于 Trace）</param>
    /// <param name="resumeData">中断恢复数据（非 null 表示本次为恢复执行，跳过中断检查）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>节点执行结果</returns>
    public async Task<WorkflowNodeResult> ExecuteAsync(
        WorkflowStepDto step,
        WorkflowState state,
        AgentRun? run = null,
        Dictionary<string, object>? resumeData = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(step);
        Check.NotNull(state);

        var stepId = step.StepId ?? "unknown";
        var sw = Stopwatch.StartNew();

        // 构建依赖输出
        var dependencyOutputs = BuildDependencyOutputs(step, state);

        // 构建节点执行上下文
        var context = new WorkflowNodeContext
        {
            Step = step,
            State = state,
            DependencyOutputs = dependencyOutputs,
            ServiceProvider = _serviceProvider,
            Run = run,
            ResumeData = resumeData
        };

        // 查找匹配的 IWorkflowNode 实现
        var node = ResolveNode(step);

        // 通用中断检查：非恢复状态下，在执行前询问节点是否需要中断
        if (!context.IsResuming)
        {
            var interrupt = await node.CheckInterruptAsync(context, cancellationToken);
            if (interrupt != null)
            {
                sw.Stop();
                _logger.LogInformation("Workflow node '{StepId}' requested interrupt: {Reason} (type={InterruptType})",
                    stepId, interrupt.Reason, interrupt.Type);

                await RecordTraceAsync(run, stepId, AgentTraceEventTypes.NodeExecute, new
                {
                    nodeType = node.NodeType,
                    durationMs = sw.ElapsedMilliseconds,
                    interrupted = true,
                    interruptType = interrupt.Type.ToString(),
                    interruptReason = interrupt.Reason
                }, sw.ElapsedMilliseconds, cancellationToken);

                return new WorkflowNodeResult
                {
                    Output = new WorkflowStepOutput
                    {
                        Text = $"[Awaiting {interrupt.Type}: {interrupt.Reason}]",
                        Metadata = new Dictionary<string, string>
                        {
                            ["status"] = "awaiting_input",
                            ["interrupt_type"] = interrupt.Type.ToString(),
                            ["step_id"] = stepId
                        }
                    },
                    IsSuccess = true,
                    AwaitingInterrupt = interrupt,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }

        // 执行（含重试 + 超时）
        var maxAttempts = step.MaxRetries + 1;
        Exception? lastException = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                WorkflowNodeResult result;

                if (step.TimeoutSeconds.HasValue)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(step.TimeoutSeconds.Value));
                    result = await node.ExecuteAsync(context, timeoutCts.Token);
                }
                else
                {
                    result = await node.ExecuteAsync(context, cancellationToken);
                }

                sw.Stop();

                // 记录 Trace
                await RecordTraceAsync(run, stepId, AgentTraceEventTypes.NodeExecute, new
                {
                    nodeType = node.NodeType,
                    durationMs = sw.ElapsedMilliseconds,
                    isSuccess = result.IsSuccess,
                    attempt = attempt + 1
                }, sw.ElapsedMilliseconds, cancellationToken);

                return new WorkflowNodeResult
                {
                    Output = result.Output,
                    Usage = result.Usage,
                    IsSuccess = result.IsSuccess,
                    Error = result.Error,
                    RouteTo = result.RouteTo,
                    AwaitingApproval = result.AwaitingApproval,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // 外部取消，不重试
            }
            catch (OperationCanceledException ex) when (step.TimeoutSeconds.HasValue)
            {
                lastException = new TimeoutException($"Node '{stepId}' timed out after {step.TimeoutSeconds}s", ex);
                _logger.LogWarning("Workflow node '{StepId}' timed out (attempt {Attempt}/{MaxAttempts})",
                    stepId, attempt + 1, maxAttempts);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Workflow node '{StepId}' failed (attempt {Attempt}/{MaxAttempts})",
                    stepId, attempt + 1, maxAttempts);
            }

            // 每次 attempt 失败写一条轻量 Trace（含 attempt 序号），让重试中间态可观测，
            // 而不是被吞进 LogWarning。最终全部耗尽后另写 NodeError（见下方）。
            var willRetry = attempt < maxAttempts - 1;
            await RecordTraceAsync(run, stepId, AgentTraceEventTypes.NodeRetryRequested, new
            {
                nodeType = node.NodeType,
                attempt = attempt + 1,
                maxAttempts,
                willRetry,
                error = lastException?.Message
            }, sw.ElapsedMilliseconds, cancellationToken);

            // 指数退避重试
            if (willRetry)
            {
                var delay = step.RetryDelaySeconds * Math.Pow(2, Math.Min(attempt, 20));
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, 300)), cancellationToken);
            }
        }

        sw.Stop();

        // 所有重试均失败
        var errorMessage = $"Node '{stepId}' failed after {maxAttempts} attempt(s): {lastException?.Message}";
        _logger.LogError(lastException, "Workflow node '{StepId}' exhausted all retries", stepId);

        await RecordTraceAsync(run, stepId, AgentTraceEventTypes.NodeError, new
        {
            error = lastException?.Message,
            attempts = maxAttempts,
            durationMs = sw.ElapsedMilliseconds
        }, sw.ElapsedMilliseconds, cancellationToken);

        return new WorkflowNodeResult
        {
            IsSuccess = false,
            Error = errorMessage,
            Output = $"[Error: {lastException?.Message}]",
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// 解析节点实现：优先按 nodeType 查找，否则回退到已注册的默认 agent 节点
    /// </summary>
    private IWorkflowNode ResolveNode(WorkflowStepDto step)
    {
        var registeredNodes = _serviceProvider.GetServices<IWorkflowNode>().ToList();

        // 检查步骤是否有配置中的 nodeType（通过 Configuration 字典传递）
        string? nodeType = null;
        if (step.Configuration != null && step.Configuration.TryGetValue("nodeType", out var nt))
        {
            nodeType = nt;
        }

        if (nodeType != null)
        {
            var matchedNode = registeredNodes.FirstOrDefault(n =>
                string.Equals(n.NodeType, nodeType, StringComparison.OrdinalIgnoreCase));

            if (matchedNode != null) return matchedNode;

            throw new InvalidOperationException($"No IWorkflowNode is registered for node type '{nodeType}'.");
        }

        var defaultAgentNode = registeredNodes.FirstOrDefault(n =>
            string.Equals(n.NodeType, WorkflowNodeTypes.Agent, StringComparison.OrdinalIgnoreCase));

        if (defaultAgentNode != null) return defaultAgentNode;

        throw new InvalidOperationException("No default 'agent' workflow node is registered.");
    }

    /// <summary>
    /// 构建依赖输出字典
    /// </summary>
    private static Dictionary<string, WorkflowStepOutput> BuildDependencyOutputs(WorkflowStepDto step, WorkflowState state)
    {
        var outputs = new Dictionary<string, WorkflowStepOutput>(StringComparer.OrdinalIgnoreCase);

        if (step.DependsOn == null) return outputs;

        foreach (var depId in step.DependsOn)
        {
            var output = state.GetOutput(depId);
            if (output != null)
            {
                outputs[depId] = output;
            }
        }

        return outputs;
    }

    /// <summary>
    /// 记录 Trace（如果有 Run）
    /// </summary>
    private async Task RecordTraceAsync(AgentRun? run, string stepId, string eventType, object eventData, long durationMs, CancellationToken ct)
    {
        if (run == null) return;

        var traceStore = _serviceProvider.GetService<ITraceStore>();
        if (traceStore == null) return;

        try
        {
            await traceStore.AddAsync(new AgentRunTrace
            {
                RunId = run.Id,
                EventType = eventType,
                EventData = JsonSerializer.Serialize(eventData, TnziJsonDefaults.Options),
                DurationMs = durationMs
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to record trace for step '{StepId}'", stepId);
        }
    }
}
