using System.Threading.Channels;

namespace Tnzi.AI.Infrastructure.Engine;

/// <summary>
/// 工作流执行器 — 支持顺序、并行和 DAG 三种编排模式
/// </summary>
public static class WorkflowRunner
{
    /// <summary>
    /// 顺序执行多个 Agent（链式传递）
    /// </summary>
    public static async Task<AgentResponse> RunSequentialAsync(IReadOnlyList<AgentExecutor> agents, string input, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(agents);
        Check.NotNullOrEmpty(input);

        var currentInput = input;
        AgentResponse? lastResponse = null;

        for (var i = 0; i < agents.Count; i++)
        {
            var agent = agents[i];
            var messages = new List<ChatMessage> { new(ChatRole.User, currentInput) };

            try
            {
                lastResponse = await agent.ExecuteAsync(messages, ct);
                currentInput = lastResponse.Text ?? string.Empty;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastResponse = new AgentResponse
                {
                    Text = currentInput,
                    FinishReason = $"Step {i + 1}/{agents.Count} (Agent '{agent.Name}') failed: {ex.Message}",
                    Messages = messages
                };
                break;
            }
        }

        return lastResponse ?? new AgentResponse { Text = string.Empty };
    }

    /// <summary>
    /// 并行执行多个 Agent（相同输入，合并输出）
    /// </summary>
    public static async Task<AgentResponse> RunParallelAsync(IReadOnlyList<AgentExecutor> agents, string input, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(agents);
        Check.NotNullOrEmpty(input);

        var tasks = agents.Select(async agent =>
        {
            var messages = new List<ChatMessage> { new(ChatRole.User, input) };
            try
            {
                return (agentName: agent.Name, response: await agent.ExecuteAsync(messages, ct), error: (string?)null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (agentName: agent.Name, response: (AgentResponse?)null, error: ex.Message);
            }
        }).ToList();

        var taskResults = await Task.WhenAll(tasks);

        var combinedText = new StringBuilder();
        int totalPromptTokens = 0, totalCompletionTokens = 0;
        var failedAgents = new List<string>();

        foreach (var (agentName, response, error) in taskResults)
        {
            if (error != null)
            {
                failedAgents.Add($"Agent '{agentName}': {error}");
                continue;
            }
            if (!string.IsNullOrEmpty(response!.Text))
            {
                if (combinedText.Length > 0) combinedText.AppendLine();
                combinedText.AppendLine($"[{agentName}]");
                combinedText.AppendLine(response.Text);
            }
            if (response.Usage != null)
            {
                totalPromptTokens += response.Usage.PromptTokens;
                totalCompletionTokens += response.Usage.CompletionTokens;
            }
        }

        return new AgentResponse
        {
            Text = combinedText.ToString().TrimEnd(),
            Usage = new TokenUsageDto
            {
                PromptTokens = totalPromptTokens,
                CompletionTokens = totalCompletionTokens,
                TotalTokens = totalPromptTokens + totalCompletionTokens
            },
            FinishReason = failedAgents.Count > 0 ? $"partial_failure: {string.Join("; ", failedAgents)}" : "stop"
        };
    }

    /// <summary>
    /// DAG 执行：按依赖关系拓扑排序，同层无依赖步骤自动并行
    /// </summary>
    /// <param name="steps">DAG 步骤定义（含 Agent 和依赖关系）</param>
    /// <param name="input">工作流初始输入</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>DAG 执行结果，含各步骤的输出</returns>
    public static Task<DagExecutionResult> RunDagAsync(IReadOnlyList<DagStep> steps, string input, CancellationToken ct = default)
    {
        return RunDagAsync(steps, input, options: null, ct);
    }

    /// <summary>
    /// DAG 执行（支持检查点）：按依赖关系拓扑排序，同层无依赖步骤自动并行，可选断点续执行
    /// </summary>
    /// <param name="steps">DAG 步骤定义（含 Agent 和依赖关系）</param>
    /// <param name="input">工作流初始输入</param>
    /// <param name="options">执行选项（检查点存储、断点续执行等），为 null 时行为与无选项版本一致</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>DAG 执行结果，含各步骤的输出</returns>
    [ExperimentalApi(Reason = "Workflow checkpointing is in preview")]
    public static async Task<DagExecutionResult> RunDagAsync(IReadOnlyList<DagStep> steps, string input, WorkflowExecutionOptions? options, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(steps);

        var executionId = options?.ExecutionId ?? Guid.NewGuid().ToString("N");
        var checkpointStore = options?.CheckpointStore;

        // 从检查点恢复或创建新状态
        WorkflowState state;
        HashSet<string> completed;
        List<WorkflowStepResultDto> stepResults;

        if (options?.Resume == true && checkpointStore != null)
        {
            var checkpoint = await checkpointStore.GetCheckpointAsync(executionId, ct);
            if (checkpoint != null)
            {
                state = WorkflowState.FromCheckpoint(checkpoint);
                completed = new HashSet<string>(checkpoint.CompletedStepIds, StringComparer.OrdinalIgnoreCase);
                // 从检查点中的已完成步骤重建 stepResults
                stepResults = checkpoint.CompletedStepIds
                    .Select(id => new WorkflowStepResultDto
                    {
                        StepId = id,
                        Output = checkpoint.StepOutputs.GetValueOrDefault(id, string.Empty)
                    })
                    .ToList();
            }
            else
            {
                state = new WorkflowState(input);
                completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                stepResults = [];
            }
        }
        else
        {
            state = new WorkflowState(input);
            completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            stepResults = [];
        }

        // 验证 DAG 结构
        ValidateDagStructure(steps);

        // 拓扑执行：逐层并行
        var failed = false;
        var interruptHandler = options?.InterruptHandler;
        var awaitingApproval = false;
        string? awaitingApprovalStepId = null;

        while (completed.Count < steps.Count)
        {
            // 找出所有依赖已完成且尚未执行的步骤（当前层）
            var ready = steps
                .Where(s => !completed.Contains(s.StepId)
                    && (s.DependsOn == null || s.DependsOn.Count == 0 || s.DependsOn.All(d => completed.Contains(d))))
                .ToList();

            if (ready.Count == 0)
            {
                // 不应发生（无环图保证），防御性退出
                break;
            }

            // 当前层的步骤并行执行
            var layerTasks = ready.Select(step => ExecuteStepAsync(step, state, ct)).ToList();
            var results = await Task.WhenAll(layerTasks);

            foreach (var result in results)
            {
                completed.Add(result.StepId);
                state.SetOutput(result.StepId, result.Output);
                stepResults.Add(result);

                // 检查是否有步骤失败（输出以 [Error: 开头）
                if (result.Output.StartsWith("[Error:", StringComparison.Ordinal))
                {
                    failed = true;
                }
            }

            // HITL：检查本层是否有步骤需要人工审批
            var approvalSteps = ready.Where(s => s.RequiresApproval && !failed).ToList();
            if (approvalSteps.Count > 0)
            {
                foreach (var approvalStep in approvalSteps)
                {
                    var stepOutput = state.GetOutput(approvalStep.StepId) ?? string.Empty;

                    if (interruptHandler != null)
                    {
                        // 有中断处理器：同步等待审批结果
                        var interruptContext = new WorkflowInterruptContext
                        {
                            ExecutionId = executionId,
                            StepId = approvalStep.StepId,
                            AgentName = approvalStep.Agent.Name,
                            StepOutput = stepOutput
                        };

                        var interruptResult = await interruptHandler.HandleInterruptAsync(interruptContext, ct);

                        if (!interruptResult.Approved)
                        {
                            // 拒绝：标记步骤输出为拒绝信息，标记失败
                            var rejectionOutput = $"[Rejected: {interruptResult.Feedback ?? "No feedback provided"}]";
                            state.SetOutput(approvalStep.StepId, rejectionOutput);
                            // 更新 stepResults 中对应步骤的输出
                            var existingResult = stepResults.FirstOrDefault(r => r.StepId == approvalStep.StepId);
                            if (existingResult != null)
                            {
                                existingResult.Output = rejectionOutput;
                            }
                            failed = true;
                        }
                        else if (interruptResult.ModifiedInput != null)
                        {
                            // 批准并修改输出：替换步骤输出
                            state.SetOutput(approvalStep.StepId, interruptResult.ModifiedInput);
                            var existingResult = stepResults.FirstOrDefault(r => r.StepId == approvalStep.StepId);
                            if (existingResult != null)
                            {
                                existingResult.Output = interruptResult.ModifiedInput;
                            }
                        }
                        // 批准且无修改：保持原输出，继续执行
                    }
                    else if (checkpointStore != null)
                    {
                        // 无中断处理器但有检查点存储：保存 awaiting_approval 状态并暂停
                        awaitingApproval = true;
                        awaitingApprovalStepId = approvalStep.StepId;

                        var awaitingCheckpoint = new WorkflowCheckpoint
                        {
                            ExecutionId = executionId,
                            CompletedStepIds = new HashSet<string>(completed),
                            StepOutputs = state.ToDictionary(),
                            InitialInput = state.InitialInput,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            Status = "awaiting_approval",
                            StepsAwaitingApproval = [approvalStep.StepId]
                        };
                        await checkpointStore.SaveCheckpointAsync(awaitingCheckpoint, ct);
                        break; // 暂停工作流
                    }
                    // 无中断处理器且无检查点存储：继续执行（忽略审批要求）
                }

                if (awaitingApproval) break; // 退出外层循环
            }

            // 每层完成后保存检查点
            if (checkpointStore != null && !awaitingApproval)
            {
                var checkpoint = new WorkflowCheckpoint
                {
                    ExecutionId = executionId,
                    CompletedStepIds = new HashSet<string>(completed),
                    StepOutputs = state.ToDictionary(),
                    InitialInput = state.InitialInput,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Status = failed ? "failed" : (completed.Count >= steps.Count ? "completed" : "running")
                };
                await checkpointStore.SaveCheckpointAsync(checkpoint, ct);
            }
        }

        // 最终保存检查点（completed 或 failed）
        if (checkpointStore != null && !awaitingApproval)
        {
            var finalCheckpoint = new WorkflowCheckpoint
            {
                ExecutionId = executionId,
                CompletedStepIds = new HashSet<string>(completed),
                StepOutputs = state.ToDictionary(),
                InitialInput = state.InitialInput,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Status = failed ? "failed" : "completed"
            };
            await checkpointStore.SaveCheckpointAsync(finalCheckpoint, ct);
        }

        // 取最后完成的非跳过步骤的输出作为最终输出
        var finalOutput = stepResults
            .Where(r => !r.Skipped)
            .LastOrDefault()?.Output ?? input;

        return new DagExecutionResult
        {
            FinalOutput = finalOutput,
            StepResults = stepResults,
            State = state,
            AwaitingApproval = awaitingApproval,
            AwaitingApprovalStepId = awaitingApprovalStepId
        };
    }

    /// <summary>
    /// 顺序流式执行多个 Agent（链式传递，逐步 yield 进度事件）
    /// </summary>
    /// <param name="agents">Agent 执行器列表，按顺序依次执行</param>
    /// <param name="input">工作流初始输入</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步进度事件流</returns>
    public static async IAsyncEnumerable<WorkflowStepProgress> RunSequentialStreamingAsync(
        IReadOnlyList<AgentExecutor> agents,
        string input,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(agents);
        Check.NotNullOrEmpty(input);

        var currentInput = input;
        var failed = false;

        for (var i = 0; i < agents.Count && !failed; i++)
        {
            var agent = agents[i];
            var stepId = $"step-{i + 1}";

            // 使用 Channel 收集单步骤的进度事件（避免在 try-catch 中 yield）
            var channel = Channel.CreateBounded<WorkflowStepProgress>(new BoundedChannelOptions(64)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            var stepTask = ExecuteSequentialStepStreamingAsync(agent, stepId, i, agents.Count, currentInput, channel.Writer, ct);

            // 从 channel 读取并 yield 所有进度事件
            await foreach (var progress in channel.Reader.ReadAllAsync(ct))
            {
                yield return progress;

                // 记录步骤完成后的输出，供下一步骤使用
                if (progress.Status == WorkflowStepStatus.Completed && progress.Output != null)
                {
                    currentInput = progress.Output;
                }
                else if (progress.Status == WorkflowStepStatus.Failed)
                {
                    failed = true;
                }
            }

            // 等待步骤任务完成并传播 OperationCanceledException
            await stepTask;
        }
    }

    /// <summary>
    /// 流式执行单个顺序步骤，将进度事件写入 Channel
    /// </summary>
    private static async Task ExecuteSequentialStepStreamingAsync(
        AgentExecutor agent,
        string stepId,
        int stepIndex,
        int totalSteps,
        string input,
        ChannelWriter<WorkflowStepProgress> writer,
        CancellationToken ct)
    {
        try
        {
            // 通知步骤开始
            await writer.WriteAsync(new WorkflowStepProgress
            {
                StepId = stepId,
                AgentName = agent.Name,
                Status = WorkflowStepStatus.Started
            }, ct);

            var responseText = new StringBuilder();
            var messages = new List<ChatMessage> { new(ChatRole.User, input) };

            await foreach (var chunk in agent.ExecuteStreamingAsync(messages, ct))
            {
                if (chunk.Text != null)
                {
                    responseText.Append(chunk.Text);

                    await writer.WriteAsync(new WorkflowStepProgress
                    {
                        StepId = stepId,
                        AgentName = agent.Name,
                        Status = WorkflowStepStatus.Streaming,
                        Text = chunk.Text
                    }, ct);
                }
            }

            var fullOutput = responseText.ToString();

            // 通知步骤完成
            await writer.WriteAsync(new WorkflowStepProgress
            {
                StepId = stepId,
                AgentName = agent.Name,
                Status = WorkflowStepStatus.Completed,
                Output = fullOutput
            }, ct);

            writer.Complete();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            writer.TryComplete();
            throw;
        }
        catch (Exception ex)
        {
            // 通知步骤失败
            try
            {
                await writer.WriteAsync(new WorkflowStepProgress
                {
                    StepId = stepId,
                    AgentName = agent.Name,
                    Status = WorkflowStepStatus.Failed,
                    Error = $"Step {stepIndex + 1}/{totalSteps} (Agent '{agent.Name}') failed: {ex.Message}"
                }, ct);
            }
            catch
            {
                // Channel 可能已关闭，忽略
            }

            writer.TryComplete();
        }
    }

    /// <summary>
    /// DAG 流式执行：按依赖关系拓扑排序，同层并行步骤通过 Channel 合并进度事件
    /// </summary>
    /// <param name="steps">DAG 步骤定义（含 Agent 和依赖关系）</param>
    /// <param name="input">工作流初始输入</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步进度事件流</returns>
    public static IAsyncEnumerable<WorkflowStepProgress> RunDagStreamingAsync(
        IReadOnlyList<DagStep> steps,
        string input,
        CancellationToken ct = default)
    {
        return RunDagStreamingAsync(steps, input, options: null, ct);
    }

    /// <summary>
    /// DAG 流式执行（支持检查点）：按依赖关系拓扑排序，同层并行步骤通过 Channel 合并进度事件，可选断点续执行
    /// </summary>
    /// <param name="steps">DAG 步骤定义（含 Agent 和依赖关系）</param>
    /// <param name="input">工作流初始输入</param>
    /// <param name="options">执行选项（检查点存储、断点续执行等），为 null 时行为与无选项版本一致</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步进度事件流</returns>
    [ExperimentalApi(Reason = "Workflow checkpointing is in preview")]
    public static async IAsyncEnumerable<WorkflowStepProgress> RunDagStreamingAsync(
        IReadOnlyList<DagStep> steps,
        string input,
        WorkflowExecutionOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(steps);

        var executionId = options?.ExecutionId ?? Guid.NewGuid().ToString("N");
        var checkpointStore = options?.CheckpointStore;

        // 从检查点恢复或创建新状态
        WorkflowState state;
        HashSet<string> completed;

        if (options?.Resume == true && checkpointStore != null)
        {
            var checkpoint = await checkpointStore.GetCheckpointAsync(executionId, ct);
            if (checkpoint != null)
            {
                state = WorkflowState.FromCheckpoint(checkpoint);
                completed = new HashSet<string>(checkpoint.CompletedStepIds, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                state = new WorkflowState(input);
                completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        else
        {
            state = new WorkflowState(input);
            completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // 验证 DAG 结构
        ValidateDagStructure(steps);

        // 拓扑执行：逐层并行，通过 Channel 合并进度事件
        while (completed.Count < steps.Count)
        {
            var ready = steps
                .Where(s => !completed.Contains(s.StepId)
                    && (s.DependsOn == null || s.DependsOn.Count == 0 || s.DependsOn.All(d => completed.Contains(d))))
                .ToList();

            if (ready.Count == 0)
            {
                break;
            }

            // 使用 Channel 合并本层所有并行步骤的进度事件
            var channel = Channel.CreateBounded<WorkflowStepProgress>(new BoundedChannelOptions(128)
            {
                SingleWriter = false,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            // 本层步骤的输出收集（步骤完成后更新 state）
            var layerOutputs = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 启动本层所有并行步骤
            var layerTasks = ready.Select(step => ExecuteStepStreamingAsync(step, state, channel.Writer, layerOutputs, ct)).ToList();

            // 当所有步骤完成后关闭 channel
            _ = Task.WhenAll(layerTasks).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

            // 从 channel 读取并 yield 所有进度事件
            await foreach (var progress in channel.Reader.ReadAllAsync(ct))
            {
                yield return progress;
            }

            // 更新状态：将本层所有步骤的输出注册到 state
            foreach (var step in ready)
            {
                completed.Add(step.StepId);
                if (layerOutputs.TryGetValue(step.StepId, out var output))
                {
                    state.SetOutput(step.StepId, output);
                }
            }

            // 每层完成后保存检查点
            if (checkpointStore != null)
            {
                var checkpoint = new WorkflowCheckpoint
                {
                    ExecutionId = executionId,
                    CompletedStepIds = new HashSet<string>(completed),
                    StepOutputs = state.ToDictionary(),
                    InitialInput = state.InitialInput,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Status = completed.Count >= steps.Count ? "completed" : "running"
                };
                await checkpointStore.SaveCheckpointAsync(checkpoint, ct);
            }
        }

        // 最终保存检查点
        if (checkpointStore != null)
        {
            var finalCheckpoint = new WorkflowCheckpoint
            {
                ExecutionId = executionId,
                CompletedStepIds = new HashSet<string>(completed),
                StepOutputs = state.ToDictionary(),
                InitialInput = state.InitialInput,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Status = "completed"
            };
            await checkpointStore.SaveCheckpointAsync(finalCheckpoint, ct);
        }
    }

    /// <summary>
    /// 流式执行单个 DAG 步骤，将进度事件写入 Channel
    /// </summary>
    private static async Task ExecuteStepStreamingAsync(
        DagStep step,
        WorkflowState state,
        ChannelWriter<WorkflowStepProgress> writer,
        ConcurrentDictionary<string, string> layerOutputs,
        CancellationToken ct)
    {
        // 评估条件
        if (!string.IsNullOrWhiteSpace(step.Condition))
        {
            var evaluatedCondition = state.ResolveTemplate(step.Condition);
            if (!EvaluateCondition(evaluatedCondition))
            {
                await writer.WriteAsync(new WorkflowStepProgress
                {
                    StepId = step.StepId,
                    AgentName = step.Agent.Name,
                    Status = WorkflowStepStatus.Skipped
                }, ct);
                layerOutputs[step.StepId] = string.Empty;
                return;
            }
        }

        // 构建输入
        var stepInput = state.ResolveTemplate(BuildStepInput(step, state));
        var messages = new List<ChatMessage> { new(ChatRole.User, stepInput) };

        // 通知步骤开始
        await writer.WriteAsync(new WorkflowStepProgress
        {
            StepId = step.StepId,
            AgentName = step.Agent.Name,
            Status = WorkflowStepStatus.Started
        }, ct);

        var responseText = new StringBuilder();

        try
        {
            await foreach (var chunk in step.Agent.ExecuteStreamingAsync(messages, ct))
            {
                if (chunk.Text != null)
                {
                    responseText.Append(chunk.Text);

                    await writer.WriteAsync(new WorkflowStepProgress
                    {
                        StepId = step.StepId,
                        AgentName = step.Agent.Name,
                        Status = WorkflowStepStatus.Streaming,
                        Text = chunk.Text
                    }, ct);
                }
            }

            var fullOutput = responseText.ToString();
            layerOutputs[step.StepId] = fullOutput;

            await writer.WriteAsync(new WorkflowStepProgress
            {
                StepId = step.StepId,
                AgentName = step.Agent.Name,
                Status = WorkflowStepStatus.Completed,
                Output = fullOutput
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            layerOutputs[step.StepId] = $"[Error: {ex.Message}]";

            await writer.WriteAsync(new WorkflowStepProgress
            {
                StepId = step.StepId,
                AgentName = step.Agent.Name,
                Status = WorkflowStepStatus.Failed,
                Error = ex.Message
            }, ct);
        }
    }

    /// <summary>
    /// 执行单个 DAG 步骤
    /// </summary>
    private static async Task<WorkflowStepResultDto> ExecuteStepAsync(DagStep step, WorkflowState state, CancellationToken ct)
    {
        // 评估条件
        if (!string.IsNullOrWhiteSpace(step.Condition))
        {
            var evaluatedCondition = state.ResolveTemplate(step.Condition);
            if (!EvaluateCondition(evaluatedCondition))
            {
                return new WorkflowStepResultDto
                {
                    StepId = step.StepId,
                    Output = string.Empty,
                    Skipped = true
                };
            }
        }

        // 构建输入：优先使用依赖步骤的输出组合，否则使用工作流初始输入
        // 同时解析输入中的模板变量 {{stepId}}，让步骤可以引用前置步骤的输出
        var stepInput = state.ResolveTemplate(BuildStepInput(step, state));

        var messages = new List<ChatMessage> { new(ChatRole.User, stepInput) };

        try
        {
            var response = await step.Agent.ExecuteAsync(messages, ct);
            return new WorkflowStepResultDto
            {
                StepId = step.StepId,
                Output = response.Text ?? string.Empty
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WorkflowStepResultDto
            {
                StepId = step.StepId,
                Output = $"[Error: {ex.Message}]"
            };
        }
    }

    /// <summary>
    /// 构建步骤输入：如果有依赖步骤，将它们的输出组合成输入；否则使用工作流初始输入
    /// </summary>
    private static string BuildStepInput(DagStep step, WorkflowState state)
    {
        if (step.DependsOn == null || step.DependsOn.Count == 0)
        {
            return state.InitialInput;
        }

        if (step.DependsOn.Count == 1)
        {
            // 单一依赖：直接使用其输出
            return state.GetOutput(step.DependsOn[0]) ?? state.InitialInput;
        }

        // 多依赖：组合所有依赖步骤的输出
        var sb = new StringBuilder();
        foreach (var depId in step.DependsOn)
        {
            var output = state.GetOutput(depId);
            if (!string.IsNullOrEmpty(output))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine($"[{depId}]");
                sb.AppendLine(output);
            }
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : state.InitialInput;
    }

    /// <summary>
    /// 验证 DAG 结构：检查依赖引用和循环依赖
    /// </summary>
    private static void ValidateDagStructure(IReadOnlyList<DagStep> steps)
    {
        var stepMap = new Dictionary<string, DagStep>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            stepMap[step.StepId] = step;
        }

        foreach (var step in steps)
        {
            if (step.DependsOn == null) continue;
            foreach (var dep in step.DependsOn)
            {
                if (!stepMap.ContainsKey(dep))
                {
                    throw new InvalidOperationException($"Step '{step.StepId}' depends on unknown step '{dep}'");
                }
            }
        }

        if (HasCycle(steps))
        {
            throw new InvalidOperationException("Workflow contains circular dependencies");
        }
    }

    /// <summary>
    /// 简单条件评估：非空且不为 "false"/"0"/"skip" 视为 true
    /// </summary>
    private static bool EvaluateCondition(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        var trimmed = condition.Trim().ToLowerInvariant();
        return trimmed is not ("false" or "0" or "skip" or "no");
    }

    /// <summary>
    /// 检测 DAG 是否有环（Kahn's algorithm）
    /// </summary>
    private static bool HasCycle(IReadOnlyList<DagStep> steps)
    {
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            inDegree.TryAdd(step.StepId, 0);
            adjacency.TryAdd(step.StepId, []);
        }

        foreach (var step in steps)
        {
            if (step.DependsOn == null) continue;
            foreach (var dep in step.DependsOn)
            {
                if (!adjacency.ContainsKey(dep)) adjacency[dep] = [];
                adjacency[dep].Add(step.StepId);
                inDegree[step.StepId] = inDegree.GetValueOrDefault(step.StepId) + 1;
            }
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var processed = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            processed++;
            foreach (var next in adjacency.GetValueOrDefault(current, []))
            {
                inDegree[next]--;
                if (inDegree[next] == 0) queue.Enqueue(next);
            }
        }

        return processed != steps.Count;
    }
}

/// <summary>
/// DAG 步骤（包含 Agent 和依赖关系）
/// </summary>
public class DagStep
{
    /// <summary>步骤唯一 ID</summary>
    public string StepId { get; set; } = string.Empty;
    /// <summary>此步骤的 AgentExecutor</summary>
    public AgentExecutor Agent { get; set; } = null!;
    /// <summary>前置步骤 ID 列表</summary>
    public List<string>? DependsOn { get; set; }
    /// <summary>执行条件表达式</summary>
    public string? Condition { get; set; }
    /// <summary>步骤完成后是否需要人工审批才能继续（默认 false）</summary>
    [ExperimentalApi(Reason = "Workflow HITL is in preview")]
    public bool RequiresApproval { get; set; }
}

/// <summary>
/// 工作流状态 — 管理步骤间的输入/输出传递
/// </summary>
public class WorkflowState
{
    private readonly ConcurrentDictionary<string, string> _outputs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>工作流初始输入</summary>
    public string InitialInput { get; }

    public WorkflowState(string initialInput)
    {
        InitialInput = Check.NotNullOrEmpty(initialInput);
    }

    /// <summary>设置步骤输出</summary>
    public void SetOutput(string stepId, string output) => _outputs[stepId] = output;

    /// <summary>获取步骤输出</summary>
    public string? GetOutput(string stepId) => _outputs.GetValueOrDefault(stepId);

    /// <summary>
    /// 将所有步骤输出导出为 Dictionary（用于检查点序列化）
    /// </summary>
    public Dictionary<string, string> ToDictionary() => new(_outputs, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 从检查点恢复 WorkflowState
    /// </summary>
    /// <param name="checkpoint">检查点数据</param>
    /// <returns>恢复后的 WorkflowState（含已完成步骤的输出）</returns>
    public static WorkflowState FromCheckpoint(WorkflowCheckpoint checkpoint)
    {
        Check.NotNull(checkpoint);

        var state = new WorkflowState(checkpoint.InitialInput);
        foreach (var (stepId, output) in checkpoint.StepOutputs)
        {
            state.SetOutput(stepId, output);
        }
        return state;
    }

    /// <summary>
    /// 解析模板变量：将 {{stepId}} 替换为对应步骤的输出
    /// </summary>
    public string ResolveTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return template;

        return Regex.Replace(template, @"\{\{(\w+)\}\}", match =>
        {
            var key = match.Groups[1].Value;
            if (key.Equals("input", StringComparison.OrdinalIgnoreCase))
                return InitialInput;
            return _outputs.GetValueOrDefault(key, match.Value);
        });
    }
}

/// <summary>
/// DAG 执行结果
/// </summary>
public class DagExecutionResult
{
    /// <summary>最终输出文本</summary>
    public string FinalOutput { get; set; } = string.Empty;
    /// <summary>各步骤执行结果</summary>
    public List<WorkflowStepResultDto> StepResults { get; set; } = [];
    /// <summary>工作流状态（包含所有步骤的输出）</summary>
    public WorkflowState State { get; set; } = null!;
    /// <summary>工作流是否因等待审批而暂停（HITL 场景）</summary>
    [ExperimentalApi(Reason = "Workflow HITL is in preview")]
    public bool AwaitingApproval { get; set; }
    /// <summary>等待审批的步骤 ID（HITL 场景）</summary>
    [ExperimentalApi(Reason = "Workflow HITL is in preview")]
    public string? AwaitingApprovalStepId { get; set; }
}

/// <summary>
/// 工作流步骤执行进度（流式输出）
/// </summary>
public class WorkflowStepProgress
{
    /// <summary>步骤标识（Sequential 模式为 "step-N"，DAG 模式为 StepId）</summary>
    public string StepId { get; init; } = string.Empty;

    /// <summary>Agent 名称</summary>
    public string AgentName { get; init; } = string.Empty;

    /// <summary>步骤状态</summary>
    public WorkflowStepStatus Status { get; init; }

    /// <summary>流式文本片段（仅 Status == Streaming 时有值）</summary>
    public string? Text { get; init; }

    /// <summary>步骤完成时的完整输出（仅 Status == Completed 时有值）</summary>
    public string? Output { get; init; }

    /// <summary>错误信息（仅 Status == Failed 时有值）</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 工作流步骤状态
/// </summary>
public enum WorkflowStepStatus
{
    /// <summary>步骤开始执行</summary>
    Started,
    /// <summary>正在流式输出</summary>
    Streaming,
    /// <summary>步骤执行完成</summary>
    Completed,
    /// <summary>步骤执行失败</summary>
    Failed,
    /// <summary>步骤被跳过（条件不满足）</summary>
    Skipped
}
