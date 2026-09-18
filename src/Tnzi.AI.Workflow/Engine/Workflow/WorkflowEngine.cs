namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// 工作流引擎 - 统一协调器，基于 WorkflowGraph 拓扑排序 + 就绪队列模型执行工作流
/// </summary>
/// <remarks>
/// <para>
/// 职责：遍历 WorkflowGraph，逐层执行就绪节点（委托给 WorkflowNodeExecutor），
/// 处理条件边路由、循环检测、检查点和人工审批中断。
/// </para>
/// <para>
/// 条件边和循环场景自动启用 Run tracking（强制 Trace）。
/// </para>
/// </remarks>
public class WorkflowEngine
{
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(ILogger<WorkflowEngine> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 执行工作流图
    /// </summary>
    /// <param name="graph">工作流图</param>
    /// <param name="initialInput">初始输入</param>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="options">执行选项（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    public async Task<WorkflowEngineResult> ExecuteAsync(
        WorkflowGraph graph,
        string initialInput,
        IServiceProvider serviceProvider,
        WorkflowExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(graph);
        Check.NotNullOrEmpty(initialInput);
        Check.NotNull(serviceProvider);

        var executionId = options?.ExecutionId ?? Guid.NewGuid().ToString("N");
        var checkpointStore = options?.CheckpointStore;
        var interruptHandler = options?.InterruptHandler;

        // 执行期间持续给 WorkflowExecution 行发心跳（所有模式、与检查点无关），否则看门狗会把一次
        // 跑得久的执行当成崩溃回收。只在调用方给了 ExecutionId（即有行可保活）时启动；释放即停。
        await using var heartbeat = WorkflowExecutionHeartbeatLoop.Start(serviceProvider, options?.ExecutionId, _logger, cancellationToken);

        // 复杂工作流默认启用 Run tracking：条件边、循环、检查点/HITL 都需要可观测的运行实例
        var hasConditionalEdges = graph.ConditionalEdges.Count > 0;
        var hasLoops = graph.Loops.Count > 0;
        var requiresApproval = graph.Nodes.Any(RequiresHumanApproval);
        var shouldTrackRun = hasConditionalEdges || hasLoops || checkpointStore != null || requiresApproval;
        AgentRun? run = null;
        IRunStore? runStore = null;

        if (shouldTrackRun)
        {
            runStore = serviceProvider.GetService<IRunStore>();
            if (runStore != null)
            {
                run = await GetOrCreateRunAsync(runStore, options, executionId, initialInput, cancellationToken);
            }
        }

        // 从检查点恢复或创建新状态
        var (state, completed, stepResults) = await RestoreOrCreateStateAsync(
            executionId, initialInput, options, checkpointStore, cancellationToken);

        // 跟踪循环迭代次数
        var loopIterations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int totalInputTokens = 0, totalOutputTokens = 0;
        var failed = false;
        var cancelled = false;
        var awaitingApproval = false;
        string? awaitingApprovalStepId = null;
        var awaitingApprovalStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WorkflowInterrupt? awaitingInterrupt = null;
        DateTime? checkpointCreatedAt = null;

        var nodeOrderIndex = graph.Nodes
            .Select((node, index) => new { node.StepId, Index = index })
            .Where(x => !string.IsNullOrWhiteSpace(x.StepId))
            .ToDictionary(x => x.StepId!, x => x.Index, StringComparer.OrdinalIgnoreCase);

        while (completed.Count < graph.Nodes.Count)
        {
            var signalResult = await ApplyPendingSignalsAsync(
                executionId,
                serviceProvider,
                checkpointStore,
                state,
                completed,
                checkpointCreatedAt,
                run,
                runStore,
                cancellationToken);

            checkpointCreatedAt = signalResult.CheckpointCreatedAt ?? checkpointCreatedAt;
            if (signalResult.Cancelled)
            {
                cancelled = true;
                break;
            }

            // 获取就绪节点
            var readyNodes = graph.GetReadyNodes(completed);
            if (readyNodes.Count == 0) break;

            // Phase 1 (sequential, outer scope) - create/track node records, evaluate conditions.
            // All run-store mutations happen here on a single DbContext, never under Task.WhenAll.
            var prepared = new List<NodePreparation>(readyNodes.Count);
            foreach (var step in readyNodes)
            {
                var stepId = step.StepId!;
                var nodeRecord = await EnsureRunNodeAsync(
                    runStore,
                    run,
                    step,
                    nodeOrderIndex.GetValueOrDefault(stepId),
                    BuildNodeInputSummary(step, state),
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(step.Condition))
                {
                    var evaluatedCondition = state.ResolveTemplate(step.Condition);
                    if (!EvaluateCondition(evaluatedCondition))
                    {
                        var skippedResult = new WorkflowNodeResult
                        {
                            Output = string.Empty,
                            IsSuccess = true
                        };
                        await UpdateRunNodeAsync(runStore, nodeRecord, AgentRunNodeStatus.Skipped, skippedResult, null, cancellationToken);
                        prepared.Add(new NodePreparation(step, stepId, nodeRecord, ResumeData: null, Skipped: true, SkippedResult: skippedResult));
                        continue;
                    }
                }

                if (nodeRecord != null)
                {
                    nodeRecord.Status = AgentRunNodeStatus.Running;
                    await runStore!.UpdateNodeAsync(nodeRecord, cancellationToken);
                }

                var resumeData = ResolveResumeData(options, stepId);

                prepared.Add(new NodePreparation(step, stepId, nodeRecord, resumeData, Skipped: false, SkippedResult: null));
            }

            // Phase 2 (parallel) - execute the actual node logic.
            // Each task gets its own DI scope so that scoped dependencies (DbContext, ChatClient,
            // tool middlewares, etc.) cannot interfere across concurrent fan-out nodes.
            var executionTasks = prepared
                .Where(p => !p.Skipped)
                .Select(async p =>
                {
                    using var scope = serviceProvider.CreateScope();
                    var scopedExecutor = scope.ServiceProvider.GetRequiredService<WorkflowNodeExecutor>();
                    var result = await scopedExecutor.ExecuteAsync(p.Step, state, run, p.ResumeData, cancellationToken);
                    return (p.StepId, p.NodeRecord, result, skipped: false);
                })
                .ToList();

            var executedResults = await Task.WhenAll(executionTasks);
            var executedById = executedResults.ToDictionary(r => r.StepId, StringComparer.OrdinalIgnoreCase);

            // Phase 3 (sequential) - merge skipped + executed in ready-order for deterministic
            // post-processing (state writes, conditional edges, loops, run-store updates).
            var results = prepared
                .Select(p => p.Skipped
                    ? (p.StepId, p.NodeRecord, p.SkippedResult!, skipped: true)
                    : executedById[p.StepId])
                .ToArray();

            foreach (var (stepId, nodeRecord, result, skipped) in results)
            {
                // ★ 被中断的节点没有执行过，不能算完成：它的输出只是执行器的 `[Awaiting ...]` 占位符。
                // 此前它被一并写进检查点的 CompletedStepIds，恢复时 GetReadyNodes 直接跳过它，
                // ResumeData 永远送不到节点手里 —— 人工输入被丢弃、审批节点把截断的占位符转给下游，
                // 而 Resume / ResumeWithInput 都报 Completed。留在 completed 之外，恢复时它会重新就绪。
                var interrupted = result.AwaitingInterrupt != null;
                if (!interrupted)
                {
                    completed.Add(stepId);
                }

                // 节点级错误策略：在失败时决定输出内容与是否中止
                var step = prepared.First(p => string.Equals(p.StepId, stepId, StringComparison.OrdinalIgnoreCase)).Step;
                var effectiveResult = result;
                if (!result.IsSuccess && !skipped)
                {
                    switch (step.OnError)
                    {
                        case NodeErrorPolicy.Skip:
                            // 跳过：空输出，不中止工作流
                            effectiveResult = new WorkflowNodeResult
                            {
                                Output = string.Empty,
                                IsSuccess = true,
                                Error = result.Error,
                                DurationMs = result.DurationMs
                            };
                            _logger.LogWarning("Workflow node '{StepId}' failed (OnError=Skip, continuing): {Error}", stepId, result.Error);
                            break;

                        case NodeErrorPolicy.Continue:
                            // Continue：以错误文本作为输出，不中止工作流
                            effectiveResult = new WorkflowNodeResult
                            {
                                Output = result.Output.Text.Length > 0 ? result.Output : new WorkflowStepOutput { Text = result.Error ?? string.Empty },
                                IsSuccess = true,
                                Error = result.Error,
                                DurationMs = result.DurationMs,
                                Usage = result.Usage
                            };
                            _logger.LogWarning("Workflow node '{StepId}' failed (OnError=Continue, continuing): {Error}", stepId, result.Error);
                            break;

                        default:
                            // Fail（默认）：中止工作流
                            failed = true;
                            _logger.LogWarning("Workflow node '{StepId}' failed: {Error}", stepId, result.Error);
                            break;
                    }
                }

                state.SetOutput(stepId, effectiveResult.Output);

                stepResults.Add(new WorkflowStepResultDto
                {
                    StepId = stepId,
                    Output = effectiveResult.Output.Text,
                    Skipped = skipped
                });

                // 聚合 token 用量
                if (effectiveResult.Usage != null)
                {
                    totalInputTokens += effectiveResult.Usage.InputTokens;
                    totalOutputTokens += effectiveResult.Usage.OutputTokens;
                }

                 if (!skipped)
                 {
                     // 使用原始结果决定 RunNode 状态（Skip/Continue 策略下原始失败也记录为 Completed，让 Error 字段保留）
                     var nodeStatus = result.AwaitingApproval
                         ? AgentRunNodeStatus.AwaitingApproval
                         : result.AwaitingInterrupt != null
                             ? AgentRunNodeStatus.AwaitingApproval // 通用中断也使用 AwaitingApproval 状态
                             : effectiveResult.IsSuccess
                                 ? AgentRunNodeStatus.Completed
                                 : AgentRunNodeStatus.Failed;

                     await UpdateRunNodeAsync(runStore, nodeRecord, nodeStatus, effectiveResult, result.Error, cancellationToken);
                 }

                // 记录节点级审批暂停和通用中断（检查点在整层结果处理完之后统一写一次）
                (awaitingApproval, awaitingApprovalStepId, awaitingInterrupt) =
                    RecordInterrupt(effectiveResult, stepId, failed, awaitingApproval, awaitingApprovalStepId,
                        awaitingApprovalStepIds, awaitingInterrupt, checkpointStore);

                // 处理条件边路由（被中断的节点还没有真实输出，路由与循环等它恢复执行后再算）
                if (!skipped && !failed && !interrupted)
                {
                    await HandleConditionalEdgeAsync(graph, stepId, effectiveResult, state, completed, runStore, run, nodeOrderIndex, cancellationToken);
                }

                // 处理循环
                if (!skipped && !failed && !interrupted)
                {
                    HandleLoop(graph, stepId, state, completed, loopIterations);
                }
            }

            // ★ 暂停检查点在整层结果都折进 completed / state 之后才写，且一次写全：此前它在处理
            // 第一个被中断的结果时就写掉了，同层随后完成的兄弟节点不在里面，恢复时被重跑重计费；
            // 第二个审批节点也因「已经在等审批」被跳过，永远进不了 StepsAwaitingApproval。
            if (awaitingApproval || awaitingInterrupt != null)
            {
                if (checkpointStore != null)
                {
                    checkpointCreatedAt ??= DateTime.UtcNow;
                    if (awaitingApproval)
                    {
                        await SaveCheckpointAsync(checkpointStore, executionId, state, completed,
                            WorkflowExecutionStatus.AwaitingApproval, awaitingApprovalStepIds, checkpointCreatedAt, cancellationToken);
                    }
                    else
                    {
                        await SaveCheckpointWithInterruptAsync(checkpointStore, executionId, state, completed,
                            awaitingInterrupt!, checkpointCreatedAt, cancellationToken);
                    }
                }

                break;
            }

            // 只有 Fail 策略（默认）会把 failed 置 true；Skip/Continue 吸收失败，不置位也不中止。
            if (failed) break;

            // HITL：检查本层是否有步骤需要人工审批。
            // nodeType=approval 的节点自己就是闸门（经 CheckInterruptAsync 暂停、恢复时按 ResumeData 出结果），
            // 可视化编辑器会给它同时打上 RequiresApproval；这里排除它，否则恢复执行后又被 run-then-gate
            // 拦一次，同一个节点要审批两遍。
            if (!failed)
            {
                var approvalNodes = readyNodes
                    .Where(s => s.RequiresApproval && !IsApprovalNodeType(s))
                    .ToList();
                foreach (var approvalNode in approvalNodes)
                {
                    var approvalStepId = approvalNode.StepId!;
                    var stepOutput = state.GetOutput(approvalStepId)?.Text ?? string.Empty;

                    if (interruptHandler != null)
                    {
                        var interruptResult = await interruptHandler.HandleInterruptAsync(new WorkflowInterruptContext
                        {
                            ExecutionId = executionId,
                            StepId = approvalStepId,
                            AgentName = approvalNode.StepId ?? "unknown",
                            StepOutput = stepOutput
                        }, cancellationToken);

                        if (!interruptResult.Approved)
                        {
                            var rejectionOutput = new WorkflowStepOutput
                            {
                                Text = $"[Rejected: {interruptResult.Feedback ?? "No feedback provided"}]"
                            };
                            state.SetOutput(approvalStepId, rejectionOutput);
                            UpdateStepResult(stepResults, approvalStepId, rejectionOutput.Text);
                            await UpdateRunNodeAsync(runStore, run?.Nodes.FirstOrDefault(n =>
                                string.Equals(n.NodeName, approvalStepId, StringComparison.OrdinalIgnoreCase)),
                                AgentRunNodeStatus.Rejected,
                                new WorkflowNodeResult { Output = rejectionOutput, IsSuccess = false, Error = interruptResult.Feedback },
                                interruptResult.Feedback,
                                cancellationToken);
                            failed = true;
                        }
                        else if (interruptResult.ModifiedInput != null)
                        {
                            state.SetOutput(approvalStepId, interruptResult.ModifiedInput);
                            UpdateStepResult(stepResults, approvalStepId, interruptResult.ModifiedInput);
                        }
                    }
                    else if (checkpointStore != null)
                    {
                        awaitingApproval = true;
                        awaitingApprovalStepId ??= approvalStepId;
                        awaitingApprovalStepIds.Add(approvalStepId);
                        await UpdateRunNodeAsync(runStore, run?.Nodes.FirstOrDefault(n =>
                            string.Equals(n.NodeName, approvalStepId, StringComparison.OrdinalIgnoreCase)),
                            AgentRunNodeStatus.AwaitingApproval,
                            new WorkflowNodeResult { Output = state.GetOutput(approvalStepId) ?? string.Empty, AwaitingApproval = true },
                            null,
                            cancellationToken);
                    }
                }

                if (awaitingApproval)
                {
                    checkpointCreatedAt ??= DateTime.UtcNow;
                    await SaveCheckpointAsync(checkpointStore!, executionId, state, completed,
                        WorkflowExecutionStatus.AwaitingApproval, awaitingApprovalStepIds, checkpointCreatedAt, cancellationToken);
                    break;
                }
            }

            // 每层完成后保存检查点
            if (checkpointStore != null && !awaitingApproval && awaitingInterrupt == null)
            {
                var status = failed ? WorkflowExecutionStatus.Failed : (completed.Count >= graph.Nodes.Count ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running);
                checkpointCreatedAt ??= DateTime.UtcNow;
                await SaveCheckpointAsync(checkpointStore, executionId, state, completed, status, null, checkpointCreatedAt, cancellationToken);
            }
        }

        return await BuildFinalResultAsync(
            executionId, initialInput, serviceProvider, state, completed, stepResults,
            totalInputTokens, totalOutputTokens, failed, cancelled, awaitingApproval, awaitingApprovalStepId,
            awaitingInterrupt, checkpointStore, checkpointCreatedAt, run, runStore, cancellationToken);
    }

    /// <summary>
    /// Node preparation result captured in the sequential phase before parallel execution.
    /// Carries either the prepared step (to execute in its own scope) or a pre-computed skipped result.
    /// </summary>
    private sealed record NodePreparation(
        WorkflowStepDto Step,
        string StepId,
        AgentRunNode? NodeRecord,
        Dictionary<string, object>? ResumeData,
        bool Skipped,
        WorkflowNodeResult? SkippedResult);



    // ------------------------------------------------------------------------
    // 条件边路由、循环处理、检查点、Run 节点管理等辅助方法
    // ------------------------------------------------------------------------

    /// <summary>
    /// 处理节点级审批暂停和通用中断
    /// </summary>
    /// <summary>
    /// 恢复时交给节点的 ResumeData：按步骤的映射优先，其次是单步的 ResumeStepId/ResumeData 对。
    /// 同一层里多个审批节点各自被批准后，一次恢复要把每个节点的结论都带上。
    /// </summary>
    private static Dictionary<string, object>? ResolveResumeData(WorkflowExecutionOptions? options, string stepId)
    {
        if (options == null) return null;

        if (options.ResumeDataByStep != null && options.ResumeDataByStep.TryGetValue(stepId, out var perStep))
        {
            return perStep;
        }

        return options.ResumeStepId != null
               && string.Equals(options.ResumeStepId, stepId, StringComparison.OrdinalIgnoreCase)
            ? options.ResumeData
            : null;
    }

    /// <summary>
    /// 记录一个节点结果携带的暂停请求。只记不写：本层所有结果处理完后，调用方按累计的
    /// 审批集合 / 首个通用中断写一次检查点。审批型（含 <see cref="InterruptType.Approval"/>
    /// 的通用中断）全部进 <paramref name="awaitingApprovalStepIds"/>；其它类型只保留第一个，
    /// 后面的节点不在 completed 里，恢复后会再次中断。
    /// </summary>
    private static (bool awaitingApproval, string? awaitingApprovalStepId, WorkflowInterrupt? awaitingInterrupt)
        RecordInterrupt(
            WorkflowNodeResult result,
            string stepId,
            bool failed,
            bool awaitingApproval,
            string? awaitingApprovalStepId,
            HashSet<string> awaitingApprovalStepIds,
            WorkflowInterrupt? awaitingInterrupt,
            IWorkflowCheckpointStore? checkpointStore)
    {
        if (failed)
        {
            return (awaitingApproval, awaitingApprovalStepId, awaitingInterrupt);
        }

        // 节点级审批暂停（ApprovalNode 返回 AwaitingApproval=true）
        if (result.AwaitingApproval)
        {
            awaitingApproval = true;
            awaitingApprovalStepId ??= stepId;
            awaitingApprovalStepIds.Add(stepId);
            return (awaitingApproval, awaitingApprovalStepId, awaitingInterrupt);
        }

        if (result.AwaitingInterrupt == null)
        {
            return (awaitingApproval, awaitingApprovalStepId, awaitingInterrupt);
        }

        // Runtime backstop for interrupt/HITL nodes. The static HasHitlNode entry guard
        // only catches declared approval nodes (RequiresApproval / nodeType=approval);
        // a custom node can raise an interrupt via CheckInterruptAsync without those
        // markers. Any interrupt needs a checkpoint store to persist resumable state, and
        // non-DAG modes have none, so setting an awaiting state here would break with no
        // way to resume (silent hang). Fail fast instead.
        if (checkpointStore == null)
        {
            throw new BusinessException(
                "Interrupt/HITL nodes require DAG execution mode",
                ErrorCodes.WorkflowExecutionInvalidState, 400);
        }

        // 审批型通用中断同时置两个标志（结果对象向后兼容地暴露 AwaitingInterrupt），
        // 检查点与运行状态以 awaitingApproval 为准。
        if (result.AwaitingInterrupt.Type == InterruptType.Approval)
        {
            awaitingApproval = true;
            awaitingApprovalStepId ??= stepId;
            awaitingApprovalStepIds.Add(stepId);
        }

        awaitingInterrupt ??= result.AwaitingInterrupt;

        return (awaitingApproval, awaitingApprovalStepId, awaitingInterrupt);
    }

    /// <summary>
    /// 构建最终执行结果（最终检查点 + Run 状态更新 + 结果对象）
    /// </summary>
    private static async Task<WorkflowEngineResult> BuildFinalResultAsync(
        string executionId,
        string initialInput,
        IServiceProvider serviceProvider,
        WorkflowState state,
        HashSet<string> completed,
        List<WorkflowStepResultDto> stepResults,
        int totalInputTokens,
        int totalOutputTokens,
        bool failed,
        bool cancelled,
        bool awaitingApproval,
        string? awaitingApprovalStepId,
        WorkflowInterrupt? awaitingInterrupt,
        IWorkflowCheckpointStore? checkpointStore,
        DateTime? checkpointCreatedAt,
        AgentRun? run,
        IRunStore? runStore,
        CancellationToken cancellationToken)
    {
        // 最终检查点
        if (checkpointStore != null && !awaitingApproval && awaitingInterrupt == null)
        {
            checkpointCreatedAt ??= DateTime.UtcNow;
            var finalStatus = cancelled
                ? WorkflowExecutionStatus.Cancelled
                : failed
                    ? WorkflowExecutionStatus.Failed
                    : WorkflowExecutionStatus.Completed;
            await SaveCheckpointAsync(checkpointStore, executionId, state, completed, finalStatus, null, checkpointCreatedAt, cancellationToken);
        }

        // 更新 Run 状态
        if (run != null)
        {
            runStore ??= serviceProvider.GetService<IRunStore>();
            if (runStore != null)
            {
                // 中断分两种等待：审批型等 approve/reject，其余（HumanInput / ExternalEvent）等
                // ResumeWithInputAsync。行上的状态必须与执行的 AwaitingApproval / AwaitingInput 同口径
                // （与 WorkflowDelegator.MapStatus 一致）：此前一律写 AwaitingApproval，于是首次
                // HumanInput 中断后 send_agent_input 被按行状态判成"未在等输入"而 409，
                // 而读执行状态的 GetState 同时报 canSendInput=true。
                run.Status = failed
                    ? AgentRunStatus.Failed
                    : cancelled
                        ? AgentRunStatus.Cancelled
                        : awaitingApproval
                            ? AgentRunStatus.AwaitingApproval
                            : awaitingInterrupt != null
                                ? AgentRunStatus.RequiresClarification
                                : AgentRunStatus.Completed;
                run.TotalInputTokens = totalInputTokens;
                run.TotalOutputTokens = totalOutputTokens;
                run.OutputSummary = stepResults.LastOrDefault(r => !r.Skipped)?.Output;
                run.LastHeartbeatAt = DateTime.UtcNow;
                await runStore.UpdateAsync(run, cancellationToken);
            }
        }

        var finalOutput = stepResults
            .Where(r => !r.Skipped)
            .LastOrDefault()?.Output ?? initialInput;

        return new WorkflowEngineResult
        {
            ExecutionId = executionId,
            FinalOutput = finalOutput,
            StepResults = stepResults,
            State = state,
            Usage = totalInputTokens > 0 || totalOutputTokens > 0
                ? new TokenUsageDto
                {
                    InputTokens = totalInputTokens,
                    OutputTokens = totalOutputTokens,
                    TotalTokens = totalInputTokens + totalOutputTokens
                }
                : null,
            HasFailure = failed,
            Cancelled = cancelled,
            AwaitingApproval = awaitingApproval,
            AwaitingApprovalStepId = awaitingApprovalStepId,
            AwaitingInterrupt = awaitingInterrupt,
            RunId = run?.Id
        };
    }

    /// <summary>
    /// 处理条件边路由
    /// </summary>
    private async Task HandleConditionalEdgeAsync(
        WorkflowGraph graph,
        string fromNodeId,
        WorkflowNodeResult result,
        WorkflowState state,
        HashSet<string> completed,
        IRunStore? runStore,
        AgentRun? run,
        IReadOnlyDictionary<string, int> nodeOrderIndex,
        CancellationToken ct)
    {
        var edge = graph.GetConditionalEdge(fromNodeId);
        if (edge == null) return;

        var outputText = result.Output.Text;
        string? targetNodeId = null;

        // 优先使用节点结果中的 RouteTo。Router / Conditional / Review 节点给的是<b>路由键</b>（"accept" / "technical"），
        // 要经边的 Routes 表翻成目标节点；代码优先的节点也可以直接给目标节点 id。两者都不是时落到 DefaultTarget ——
        // 此前把路由键当节点 id 用：选中集合里是一个不存在的节点，随后所有真实分支被一起标成跳过。
        if (result.RouteTo != null)
        {
            targetNodeId = edge.Routes.TryGetValue(result.RouteTo, out var mappedTarget)
                ? mappedTarget
                : graph.GetNode(result.RouteTo) != null ? result.RouteTo : null;
        }
        else
        {
            targetNodeId = edge.ConditionType switch
            {
                EdgeConditionType.OutputContains => EvaluateOutputContains(edge, outputText),
                EdgeConditionType.OutputEquals => EvaluateOutputEquals(edge, outputText),
                EdgeConditionType.JsonPath => EvaluateJsonPath(edge, outputText),
                _ => edge.DefaultTarget
            };
        }

        if (targetNodeId == null)
        {
            targetNodeId = edge.DefaultTarget;
        }

        if (targetNodeId != null)
        {
            _logger.LogDebug("Conditional edge from '{FromNode}' routing to '{TargetNode}'", fromNodeId, targetNodeId);

            var selectedReachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetNodeId };
            foreach (var selectedDownstream in graph.GetTransitiveDownstream(targetNodeId))
            {
                selectedReachable.Add(selectedDownstream);
            }

            // Nodes belonging to a currently-active loop must NOT be force-marked completed by
            // branch pruning: loop iteration (HandleLoop) owns their completion lifecycle by
            // removing them from `completed` to re-run. A loop is "active" when it contains the
            // routing node (fromNodeId) or the selected target. Without this guard, an alternate
            // branch node that happens to live inside the active loop would be wrongly skipped,
            // breaking the loop's next iteration.
            var activeLoopNodes = CollectActiveLoopNodeIds(graph, fromNodeId, targetNodeId, selectedReachable);

            var alternateRoots = edge.Routes.Values
                .Append(edge.DefaultTarget)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(x => !string.Equals(x, targetNodeId, StringComparison.OrdinalIgnoreCase));

            foreach (var alternateRoot in alternateRoots)
            {
                var alternateReachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { alternateRoot! };
                foreach (var downstream in graph.GetTransitiveDownstream(alternateRoot!))
                {
                    alternateReachable.Add(downstream);
                }

                foreach (var downstream in alternateReachable)
                {
                    if (selectedReachable.Contains(downstream) || completed.Contains(downstream))
                    {
                        continue;
                    }

                    // Skip nodes inside an active loop: leave their state to HandleLoop.
                    if (activeLoopNodes.Contains(downstream))
                    {
                        _logger.LogDebug("Conditional edge: not skipping '{Node}' (member of an active loop)", downstream);
                        continue;
                    }

                    completed.Add(downstream);
                    _logger.LogDebug("Conditional edge: skipping non-target node '{SkippedNode}'", downstream);

                    if (runStore != null && run != null && graph.GetNode(downstream) != null)
                    {
                        var skippedStep = graph.GetNode(downstream)!;
                        var skippedNode = await EnsureRunNodeAsync(
                            runStore,
                            run,
                            skippedStep,
                            nodeOrderIndex.GetValueOrDefault(downstream),
                            BuildNodeInputSummary(skippedStep, state),
                            ct);

                        await UpdateRunNodeAsync(
                            runStore,
                            skippedNode,
                            AgentRunNodeStatus.Skipped,
                            new WorkflowNodeResult { Output = string.Empty, IsSuccess = true },
                            null,
                            ct);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 收集「当前激活循环」内的全部节点 ID，用于在条件边分支裁剪时显式排除它们。
    /// 一个循环视为「激活」的判定：该循环包含路由节点（<paramref name="fromNodeId"/>）、
    /// 或包含被选中的目标节点（<paramref name="targetNodeId"/>）、或其任一成员落在被选中路径
    /// （<paramref name="selectedReachable"/>）内。激活循环内的节点完成态由 <see cref="HandleLoop"/>
    /// 通过从 completed 集合移除来管理，故分支裁剪不得将其误标完成。
    /// </summary>
    private static HashSet<string> CollectActiveLoopNodeIds(
        WorkflowGraph graph,
        string fromNodeId,
        string targetNodeId,
        IReadOnlySet<string> selectedReachable)
    {
        var activeLoopNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (graph.Loops.Count == 0) return activeLoopNodes;

        foreach (var (_, loopDef) in graph.Loops)
        {
            var isActive = loopDef.NodeIds.Any(id =>
                string.Equals(id, fromNodeId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, targetNodeId, StringComparison.OrdinalIgnoreCase)
                || selectedReachable.Contains(id));

            if (!isActive) continue;

            foreach (var id in loopDef.NodeIds)
            {
                activeLoopNodes.Add(id);
            }
        }

        return activeLoopNodes;
    }

    /// <summary>
    /// 处理循环
    /// </summary>
    private void HandleLoop(
        WorkflowGraph graph,
        string nodeId,
        WorkflowState state,
        HashSet<string> completed,
        Dictionary<string, int> loopIterations)
    {
        var (inLoop, loopId) = graph.IsInLoop(nodeId);
        if (!inLoop || loopId == null) return;

        var loopDef = graph.Loops[loopId];
        var lastNodeInLoop = loopDef.NodeIds[^1];

        // 只在循环的最后一个节点完成后检查是否需要继续循环
        if (!string.Equals(nodeId, lastNodeInLoop, StringComparison.OrdinalIgnoreCase)) return;

        var currentIteration = loopIterations.GetValueOrDefault(loopId, 0) + 1;
        loopIterations[loopId] = currentIteration;

        if (currentIteration >= loopDef.MaxIterations)
        {
            _logger.LogInformation("Loop '{LoopId}' reached max iterations ({Max})", loopId, loopDef.MaxIterations);
            return;
        }

        // 检查循环终止条件（通过最后一个节点的输出元数据）
        var lastOutput = state.GetOutput(lastNodeInLoop);
        if (lastOutput?.Metadata != null
            && lastOutput.Metadata.TryGetValue("loop_done", out var doneValue)
            && string.Equals(doneValue, "true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Loop '{LoopId}' terminated by node output (iteration {Iteration})", loopId, currentIteration);
            return;
        }

        // 重置循环内节点为未完成，使其可以再次执行
        _logger.LogDebug("Loop '{LoopId}' continuing iteration {Iteration}/{Max}", loopId, currentIteration, loopDef.MaxIterations);
        foreach (var loopNodeId in loopDef.NodeIds)
        {
            completed.Remove(loopNodeId);
        }
    }

    /// <summary>
    /// 简单条件评估（fail-closed 真值门控）：
    /// 空/纯空白 = 无条件 → 执行；非空时仅白名单 "true"/"1"/"yes" 判真；
    /// 含未解析模板占位符（{{...}}）或任意其他文本（如 LLM 自由输出）一律判假并告警。
    /// <para>
    /// 注意：这与 <see cref="Nodes.ConditionalNode"/> 的 <c>ResolveRouteValue</c> 语义不同：
    /// 本方法决定「节点是否执行」（布尔门控，fail-closed）；<c>ResolveRouteValue</c> 决定
    /// 「条件边路由到哪个目标节点」（自由值路由键解析）。两者职责不可互换。
    /// </para>
    /// </summary>
    private bool EvaluateCondition(string condition)
    {
        // 无条件 = 执行（语义不变）
        if (string.IsNullOrWhiteSpace(condition)) return true;

        var trimmed = condition.Trim();

        // 模板未解析（仍含 {{...}}）→ fail-closed 跳过节点
        if (trimmed.Contains("{{", StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Workflow step condition contains unresolved template placeholders; treating as false (fail-closed): {Condition}",
                Truncate(trimmed, 200));
            return false;
        }

        var normalized = trimmed.ToLowerInvariant();
        if (normalized is "true" or "1" or "yes") return true;

        _logger.LogWarning(
            "Workflow step condition did not match the truthy whitelist (true/1/yes); treating as false (fail-closed): {Condition}",
            Truncate(trimmed, 200));
        return false;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static string? EvaluateOutputContains(ConditionalEdge edge, string output)
    {
        foreach (var (key, targetId) in edge.Routes)
        {
            if (output.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return targetId;
            }
        }
        return null;
    }

    private static string? EvaluateOutputEquals(ConditionalEdge edge, string output)
    {
        var trimmed = output.Trim();
        foreach (var (key, targetId) in edge.Routes)
        {
            if (string.Equals(trimmed, key, StringComparison.OrdinalIgnoreCase))
            {
                return targetId;
            }
        }
        return null;
    }

    private static string? EvaluateJsonPath(ConditionalEdge edge, string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);

            // 简单 JsonPath: Routes 的 Key 格式为 "$.property=value"
            foreach (var (key, targetId) in edge.Routes)
            {
                var parts = key.Split('=', 2);
                if (parts.Length != 2) continue;

                var path = parts[0].TrimStart('$', '.');
                var expectedValue = parts[1];

                if (doc.RootElement.TryGetProperty(path, out var element))
                {
                    var actualValue = element.ToString();
                    if (string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        return targetId;
                    }
                }
            }
        }
        catch
        {
            // JSON 解析失败，忽略
        }
        return null;
    }

    private static bool RequiresHumanApproval(WorkflowStepDto step)
    {
        return step.RequiresApproval || IsApprovalNodeType(step);
    }

    private static bool IsApprovalNodeType(WorkflowStepDto step)
        => string.Equals(GetNodeType(step), WorkflowNodeTypes.Approval, StringComparison.OrdinalIgnoreCase);

    private static string GetNodeType(WorkflowStepDto step)
        => WorkflowStepNodeType.Get(step) ?? WorkflowNodeTypes.Agent;

    // Node input summary uses the shared WorkflowNodeHelper.BuildStepInput (single
    // canonical implementation, also consumed by AgentNode) to avoid triplicated logic.
    private static string BuildNodeInputSummary(WorkflowStepDto step, WorkflowState state)
        => WorkflowNodeHelper.BuildStepInput(step, state);

    private static async Task<AgentRun> GetOrCreateRunAsync(
        IRunStore runStore,
        WorkflowExecutionOptions? options,
        string executionId,
        string initialInput,
        CancellationToken ct)
    {
        if (options?.RunId.HasValue == true)
        {
            var existingRun = await runStore.GetWithNodesAsync(options.RunId.Value, ct);
            if (existingRun == null)
            {
                throw new InvalidOperationException($"Workflow run '{options.RunId}' was not found.");
            }

            return existingRun;
        }

        return await runStore.CreateAsync(new AgentRun
        {
            Status = AgentRunStatus.Running,
            ExecutionMode = AgentExecutionMode.Single,
            InputSummary = initialInput.Length > 500 ? initialInput[..500] : initialInput,
            WorkflowExecutionId = executionId,
            WorkflowDefinitionId = options?.WorkflowDefinitionId
        }, ct);
    }

    private static async Task<AgentRunNode?> EnsureRunNodeAsync(
        IRunStore? runStore,
        AgentRun? run,
        WorkflowStepDto step,
        int orderIndex,
        string inputSummary,
        CancellationToken ct)
    {
        if (runStore == null || run == null || string.IsNullOrWhiteSpace(step.StepId))
        {
            return null;
        }

        var existingNode = run.Nodes.FirstOrDefault(n =>
            string.Equals(n.NodeName, step.StepId, StringComparison.OrdinalIgnoreCase));

        if (existingNode != null)
        {
            if (existingNode.InputSummary != inputSummary)
            {
                existingNode.InputSummary = inputSummary;
                await runStore.UpdateNodeAsync(existingNode, ct);
            }

            return existingNode;
        }

        var node = new AgentRunNode
        {
            RunId = run.Id,
            NodeType = GetNodeType(step),
            NodeName = step.StepId,
            NodeKey = step.StepId,
            AgentId = step.AgentId,
            Status = AgentRunNodeStatus.Pending,
            InputSummary = inputSummary,
            OrderIndex = orderIndex
        };

        await runStore.AddNodeAsync(node, ct);
        run.Nodes.Add(node);
        return node;
    }

    private static async Task UpdateRunNodeAsync(
        IRunStore? runStore,
        AgentRunNode? node,
        AgentRunNodeStatus status,
        WorkflowNodeResult result,
        string? error,
        CancellationToken ct)
    {
        if (runStore == null || node == null) return;

        node.Status = status;
        node.Output = result.Output.Text;
        node.DurationMs = result.DurationMs;
        node.Error = error;
        node.AwaitingInputKind = result.AwaitingInterrupt?.Type switch
        {
            InterruptType.Approval => "approval",
            InterruptType.HumanInput => "human_input",
            InterruptType.ExternalEvent => "external_event",
            _ => result.AwaitingApproval ? "approval" : null
        };
        if (result.Usage != null)
        {
            node.InputTokens = result.Usage.InputTokens;
            node.OutputTokens = result.Usage.OutputTokens;
        }

        await runStore.UpdateNodeAsync(node, ct);
    }

    /// <summary>
    /// 从检查点恢复或创建新状态
    /// </summary>
    private static async Task<(WorkflowState state, HashSet<string> completed, List<WorkflowStepResultDto> stepResults)>
        RestoreOrCreateStateAsync(
            string executionId,
            string initialInput,
            WorkflowExecutionOptions? options,
            IWorkflowCheckpointStore? checkpointStore,
            CancellationToken ct)
    {
        if (options?.Resume == true && checkpointStore != null)
        {
            var checkpoint = await checkpointStore.GetCheckpointAsync(executionId, ct);
            if (checkpoint != null)
            {
                var state = WorkflowState.FromCheckpoint(checkpoint);
                var completed = new HashSet<string>(checkpoint.CompletedStepIds, StringComparer.OrdinalIgnoreCase);
                var stepResults = checkpoint.CompletedStepIds
                    .Select(id => new WorkflowStepResultDto
                    {
                        StepId = id,
                        Output = checkpoint.StepOutputs.GetValueOrDefault(id)?.Text ?? string.Empty
                    })
                    .ToList();
                return (state, completed, stepResults);
            }
        }

        return (new WorkflowState(initialInput),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            []);
    }

    /// <summary>
    /// 保存检查点
    /// </summary>
    private static async Task SaveCheckpointAsync(
        IWorkflowCheckpointStore store,
        string executionId,
        WorkflowState state,
        HashSet<string> completed,
        WorkflowExecutionStatus status,
        HashSet<string>? stepsAwaitingApproval,
        DateTime? createdAt,
        CancellationToken ct)
    {
        var checkpoint = new WorkflowCheckpoint
        {
            ExecutionId = executionId,
            CompletedStepIds = new HashSet<string>(completed),
            StepOutputs = state.ToDictionary(),
            InitialInput = state.InitialInput,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Status = status,
            StepsAwaitingApproval = stepsAwaitingApproval ?? []
        };
        await store.SaveCheckpointAsync(checkpoint, ct);
    }

    /// <summary>
    /// 保存包含通用中断信息的检查点
    /// </summary>
    private static async Task SaveCheckpointWithInterruptAsync(
        IWorkflowCheckpointStore store,
        string executionId,
        WorkflowState state,
        HashSet<string> completed,
        WorkflowInterrupt interrupt,
        DateTime? createdAt,
        CancellationToken ct)
    {
        var checkpoint = new WorkflowCheckpoint
        {
            ExecutionId = executionId,
            CompletedStepIds = new HashSet<string>(completed),
            StepOutputs = state.ToDictionary(),
            InitialInput = state.InitialInput,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Status = WorkflowExecutionStatus.AwaitingInput,
            PendingInterruptJson = JsonSerializer.Serialize(interrupt, TnziJsonDefaults.Options)
        };
        await store.SaveCheckpointAsync(checkpoint, ct);
    }

    /// <summary>
    /// 从邮箱拉取待处理信号并应用。★ 引擎只应用 <see cref="WorkflowExecutionSignalTypes.Cancel"/>；
    /// 其它类型（含 <see cref="WorkflowExecutionSignalTypes.ResumeInput"/>）没有应用路径 ——
    /// 人工输入到达节点的唯一通道是服务层的 ResumeWithInput（<c>ResumeStepId</c>/<c>ResumeData</c>）。
    /// 这类信号仍会被确认（留着只会把 PendingSignalCount 钉死，永远没人来应用它），但**记 Warning 指名**，
    /// 此前是无声确认并丢弃。
    /// </summary>
    [ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
    private async Task<WorkflowSignalProcessingResult> ApplyPendingSignalsAsync(
        string executionId,
        IServiceProvider serviceProvider,
        IWorkflowCheckpointStore? checkpointStore,
        WorkflowState state,
        HashSet<string> completed,
        DateTime? checkpointCreatedAt,
        AgentRun? run,
        IRunStore? runStore,
        CancellationToken ct)
    {
        var mailbox = serviceProvider.GetService<IWorkflowExecutionMailbox>();
        if (mailbox == null)
        {
            return WorkflowSignalProcessingResult.None;
        }

        var signals = await mailbox.GetPendingSignalsAsync(executionId, ct);
        if (signals.Count == 0)
        {
            return WorkflowSignalProcessingResult.None;
        }

        var consumedIds = new List<string>();
        var cancelled = false;
        foreach (var signal in signals)
        {
            switch (signal.Type)
            {
                case WorkflowExecutionSignalTypes.Cancel:
                    cancelled = true;
                    consumedIds.Add(signal.SignalId);
                    break;
                default:
                    _logger.LogWarning(
                        "Workflow execution '{ExecutionId}' discarded signal '{SignalId}' of type '{SignalType}' (step: {StepId}): the engine applies only '{CancelType}' signals; deliver input through ResumeWithInput instead.",
                        executionId,
                        signal.SignalId,
                        signal.Type,
                        signal.StepId,
                        WorkflowExecutionSignalTypes.Cancel);
                    consumedIds.Add(signal.SignalId);
                    break;
            }
        }

        if (consumedIds.Count > 0)
        {
            await mailbox.AcknowledgeSignalsAsync(executionId, consumedIds, ct);
        }

        if (!cancelled)
        {
            return WorkflowSignalProcessingResult.None;
        }

        if (checkpointStore != null)
        {
            checkpointCreatedAt ??= DateTime.UtcNow;
            await SaveCheckpointAsync(
                checkpointStore,
                executionId,
                state,
                completed,
                WorkflowExecutionStatus.Cancelled,
                null,
                checkpointCreatedAt,
                ct);
        }

        if (run != null && runStore != null)
        {
            run.Status = AgentRunStatus.Cancelled;
            run.LastHeartbeatAt = DateTime.UtcNow;
            await runStore.UpdateAsync(run, ct);
        }

        return new WorkflowSignalProcessingResult
        {
            Cancelled = true,
            CheckpointCreatedAt = checkpointCreatedAt
        };
    }

    /// <summary>
    /// 更新 stepResults 中指定步骤的输出
    /// </summary>
    private static void UpdateStepResult(List<WorkflowStepResultDto> stepResults, string stepId, string output)
    {
        var existing = stepResults.FirstOrDefault(r => string.Equals(r.StepId, stepId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Output = output;
        }
    }

    [ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
    private sealed class WorkflowSignalProcessingResult
    {
        public static WorkflowSignalProcessingResult None { get; } = new();

        public bool Cancelled { get; init; }

        public DateTime? CheckpointCreatedAt { get; init; }
    }
}

/// <summary>
/// 工作流引擎执行结果
/// </summary>
public class WorkflowEngineResult
{
    /// <summary>执行实例 ID</summary>
    public string ExecutionId { get; init; } = string.Empty;

    /// <summary>最终输出文本</summary>
    public string FinalOutput { get; init; } = string.Empty;

    /// <summary>各步骤执行结果</summary>
    public List<WorkflowStepResultDto> StepResults { get; init; } = [];

    /// <summary>工作流状态</summary>
    public WorkflowState State { get; init; } = null!;

    /// <summary>聚合 Token 用量</summary>
    public TokenUsageDto? Usage { get; init; }

    /// <summary>是否存在失败节点</summary>
    public bool HasFailure { get; init; }

    /// <summary>是否已取消</summary>
    public bool Cancelled { get; init; }

    /// <summary>是否因等待审批而暂停</summary>
    public bool AwaitingApproval { get; init; }

    /// <summary>等待审批的步骤 ID</summary>
    public string? AwaitingApprovalStepId { get; init; }

    /// <summary>关联的 Run ID（条件边/循环时自动创建）</summary>
    public Guid? RunId { get; init; }

    /// <summary>
    /// 当前等待中的通用中断（如人工输入、外部事件等）
    /// </summary>
    [ExperimentalApi(Reason = "Generic workflow interrupt is in preview")]
    public WorkflowInterrupt? AwaitingInterrupt { get; init; }

    /// <summary>
    /// 根据执行结果推导状态文本（用于 DTO 层）
    /// </summary>
    /// <remarks>
    /// ★ 审批优先于通用中断：<c>InterruptType.Approval</c> 的中断会同时置位 <see cref="AwaitingApproval"/>
    /// 与 <see cref="AwaitingInterrupt"/>，而检查点存的是 <c>AwaitingApproval</c>。此前这里先看中断，
    /// 于是 nodeType=approval 的执行经服务层落库成 <c>AwaitingInput</c>，随后 <c>ApproveStepAsync</c>
    /// 以"不在审批中"拒绝（400）—— 文档里的 approve → resume 流程对审批节点从来走不通。
    /// </remarks>
    public string StatusText => AwaitingApproval
        ? nameof(WorkflowExecutionStatus.AwaitingApproval)
        : AwaitingInterrupt != null
            ? nameof(WorkflowExecutionStatus.AwaitingInput)
            : Cancelled
                ? nameof(WorkflowExecutionStatus.Cancelled)
            : HasFailure
                ? nameof(WorkflowExecutionStatus.Failed)
                : nameof(WorkflowExecutionStatus.Completed);
}
