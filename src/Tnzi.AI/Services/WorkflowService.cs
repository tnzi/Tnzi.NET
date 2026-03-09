
namespace Tnzi.AI.Services;

/// <summary>
/// 工作流服务实现
/// </summary>
public class WorkflowService : ApplicationService, IWorkflowService
{
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IRepository<WorkflowExecution, Guid> _executionRepository;
    private readonly IRepository<AgentRun, Guid> _runRepository;
    private readonly IUsageLogService _usageLogService;
    private readonly IQuotaService _quotaService;
    private readonly IWorkflowCheckpointStore _checkpointStore;
    private readonly WorkflowEngine _workflowEngine;

    public WorkflowService(
        IRepository<WorkflowDefinition, Guid> repository,
        IRepository<WorkflowExecution, Guid> executionRepository,
        IRepository<AgentRun, Guid> runRepository,
        IUsageLogService usageLogService,
        IQuotaService quotaService,
        IWorkflowCheckpointStore checkpointStore,
        WorkflowEngine workflowEngine,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _executionRepository = Check.NotNull(executionRepository);
        _runRepository = Check.NotNull(runRepository);
        _usageLogService = Check.NotNull(usageLogService);
        _quotaService = Check.NotNull(quotaService);
        _checkpointStore = Check.NotNull(checkpointStore);
        _workflowEngine = Check.NotNull(workflowEngine);
    }

    public async Task<Result<WorkflowDefinitionDto>> CreateAsync(CreateWorkflowDefinitionDto input)
    {
        Check.NotNull(input);
        var entity = input.MapTo<WorkflowDefinition>();
        entity.Steps = JsonSerializer.Serialize(input.Steps, TnziJsonDefaults.Options);
        await _repository.InsertAsync(entity);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<WorkflowDefinitionDto>> UpdateAsync(Guid id, UpdateWorkflowDefinitionDto input)
    {
        Check.NotNull(input);
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail<WorkflowDefinitionDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);

        if (input.Name != null) entity.Name = input.Name;
        if (input.Description != null) entity.Description = input.Description;
        if (input.Steps != null) entity.Steps = JsonSerializer.Serialize(input.Steps, TnziJsonDefaults.Options);
        if (input.ExecutionMode.HasValue) entity.ExecutionMode = input.ExecutionMode.Value;
        if (input.IsEnabled.HasValue) entity.IsEnabled = input.IsEnabled.Value;

        await _repository.UpdateAsync(entity);
        return Ok(MapToDto(entity));
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        await _repository.DeleteAsync(entity);
        return Ok();
    }

    public async Task<Result<WorkflowDefinitionDto>> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail<WorkflowDefinitionDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<IPagedList<WorkflowDefinitionDto>>> GetListAsync(PagedQueryDto query)
    {
        var queryable = _repository.OrderByDescending(w => w.CreationTime);
        var pagedList = await queryable.ProjectTo<WorkflowDefinition, WorkflowDefinitionDto>().CreateAsync(query);
        return Ok(pagedList);
    }

    public async Task<Result<WorkflowExecutionResultDto>> RunAsync(Guid workflowId, string input, Guid? userId = null, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var workflowDef = await _repository.GetAsync(workflowId, ct);
        if (workflowDef == null) return Fail<WorkflowExecutionResultDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        if (!workflowDef.IsEnabled) return Fail<WorkflowExecutionResultDto>("Workflow is disabled", 400, ErrorCodes.WorkflowDisabled);
        string? executionId = null;

        QuotaReservation? reservation = null;
        if (userId.HasValue)
        {
            // 步骤数影响 Token 用量：每步输出作为下一步输入，总量约为单步的 N 倍
            var stepCount = 1;
            if (!string.IsNullOrWhiteSpace(workflowDef.Steps))
            {
                try
                {
                    using var doc = JsonDocument.Parse(workflowDef.Steps);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        stepCount = Math.Max(1, doc.RootElement.GetArrayLength());
                }
                catch { /* 解析失败时退化为单步估算 */ }
            }
            var estimatedTokens = (input.Length / 4 + 2000) * stepCount;
            var reserveResult = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, ct);
            if (!reserveResult.Succeeded)
            {
                return Fail<WorkflowExecutionResultDto>(
                    reserveResult.Message ?? "Quota reservation failed",
                    reserveResult.Code ?? 500,
                    reserveResult.Code == 429 ? ErrorCodes.QuotaExceeded : ErrorCodes.QuotaCheckFailed);
            }
            reservation = reserveResult.Data;
        }

        try
        {
            WorkflowExecutionResultDto resultDto;
            int actualInputTokens = 0, actualOutputTokens = 0;
            WorkflowEngineResult engineResult;
            WorkflowExecutionOptions? options = null;

            if (workflowDef.ExecutionMode == WorkflowExecutionMode.Dag)
            {
                executionId = await EnsureWorkflowExecutionAsync(workflowDef.Id, input, ct);
                options = new WorkflowExecutionOptions
                {
                    ExecutionId = executionId,
                    WorkflowDefinitionId = workflowDef.Id,
                    CheckpointStore = _checkpointStore
                };
            }
            else
            {
                options = new WorkflowExecutionOptions
                {
                    WorkflowDefinitionId = workflowDef.Id
                };
            }

            var graph = BuildWorkflowGraph(workflowDef);
            engineResult = await _workflowEngine.ExecuteAsync(graph, input, ServiceProvider!, options, ct);

            if (engineResult.Usage != null)
            {
                actualInputTokens = engineResult.Usage.PromptTokens;
                actualOutputTokens = engineResult.Usage.CompletionTokens;
            }

            resultDto = new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                RunId = engineResult.RunId,
                Output = engineResult.FinalOutput,
                Status = engineResult.AwaitingApproval
                    ? "AwaitingApproval"
                    : engineResult.HasFailure
                        ? "Failed"
                        : "Completed",
                StepResults = engineResult.StepResults.Count > 0 ? engineResult.StepResults : null
            };

            var actualTotalTokens = actualInputTokens + actualOutputTokens;
            await _usageLogService.LogUsageAsync(AIOperationType.WorkflowRun, "Workflow", workflowDef.Name, actualInputTokens, actualOutputTokens, stopwatch.ElapsedMilliseconds, true, ct: ct);

            // 结算配额（使用实际 token 用量）
            if (userId.HasValue && reservation != null)
            {
                try { await _quotaService.SettleQuotaAsync(userId.Value, reservation, actualTotalTokens, ct); }
                catch (Exception settleEx) { Logger.LogError(settleEx, "Failed to settle workflow quota: WorkflowId={WorkflowId}", workflowId); }
            }

            return Ok(resultDto);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(executionId))
            {
                await TryUpdateWorkflowExecutionStatusAsync(executionId, "failed", CancellationToken.None);
            }

            Logger.LogError(ex, "Workflow execution failed: {WorkflowId}", workflowId);
            await _usageLogService.LogUsageAsync(AIOperationType.WorkflowRun, "Workflow", workflowDef.Name, 0, 0, stopwatch.ElapsedMilliseconds, false, ex.Message, ct: ct);

            // 释放预留配额（错误时按 0 结算，退回预留）
            if (userId.HasValue && reservation != null)
            {
                try { await _quotaService.SettleQuotaAsync(userId.Value, reservation, 0, CancellationToken.None); }
                catch (Exception settleEx) { Logger.LogError(settleEx, "Failed to release workflow quota reservation"); }
            }

            return Fail<WorkflowExecutionResultDto>("Workflow execution failed.", 500, ErrorCodes.WorkflowFailed);
        }
    }

    /// <summary>
    /// 流式运行工作流。
    /// 当前统一由 WorkflowEngine 驱动，输出节点完成事件和最终汇总事件。
    /// token 级流式尚未接入 WorkflowEngine，因此 Sequential/Parallel/DAG 目前都是步骤级流式。
    /// </summary>
    public async IAsyncEnumerable<WorkflowExecutionResultDto> RunStreamingAsync(Guid workflowId, string input, Guid? userId = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var workflowDef = await _repository.GetAsync(workflowId, ct);
        if (workflowDef == null)
            throw new BusinessException("Workflow not found", ErrorCodes.WorkflowNotFound, 404);
        if (!workflowDef.IsEnabled)
            throw new BusinessException("Workflow is disabled", ErrorCodes.WorkflowDisabled, 400);

        // 配额预留
        QuotaReservation? reservation = null;
        if (userId.HasValue)
        {
            var stepCount = 1;
            if (!string.IsNullOrWhiteSpace(workflowDef.Steps))
            {
                try
                {
                    using var doc = JsonDocument.Parse(workflowDef.Steps);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        stepCount = Math.Max(1, doc.RootElement.GetArrayLength());
                }
                catch { /* 解析失败时退化为单步估算 */ }
            }
            var estimatedTokens = (input.Length / 4 + 2000) * stepCount;
            var reserveResult = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, ct);
            if (!reserveResult.Succeeded)
            {
                throw new BusinessException(
                    reserveResult.Message ?? "Quota reservation failed",
                    reserveResult.Code == 429 ? ErrorCodes.QuotaExceeded : ErrorCodes.QuotaCheckFailed,
                    reserveResult.Code ?? 500);
            }
            reservation = reserveResult.Data;
        }

        int actualInputTokens = 0, actualOutputTokens = 0;
        bool streamCompleted = false;
        var finalOutput = input;
        string? finalStatusOverride = null;
        string? executionId = null;
        var aggregatedStepResults = new List<WorkflowStepResultDto>();

        try
        {
            WorkflowExecutionOptions? options = null;

            if (workflowDef.ExecutionMode == WorkflowExecutionMode.Dag)
            {
                executionId = await EnsureWorkflowExecutionAsync(workflowDef.Id, input, ct);
                options = new WorkflowExecutionOptions
                {
                    ExecutionId = executionId,
                    WorkflowDefinitionId = workflowDef.Id,
                    CheckpointStore = _checkpointStore
                };
            }
            else
            {
                options = new WorkflowExecutionOptions
                {
                    WorkflowDefinitionId = workflowDef.Id
                };
            }

            var graph = BuildWorkflowGraph(workflowDef);
            var engineResult = await _workflowEngine.ExecuteAsync(graph, input, ServiceProvider!, options, ct);
            AggregateUsage(engineResult.Usage, ref actualInputTokens, ref actualOutputTokens);
            aggregatedStepResults.AddRange(engineResult.StepResults);
            finalOutput = engineResult.FinalOutput;
            finalStatusOverride = engineResult.AwaitingApproval
                ? "AwaitingApproval"
                : engineResult.HasFailure
                    ? "Failed"
                    : "Completed";

            foreach (var stepResult in engineResult.StepResults)
            {
                yield return new WorkflowExecutionResultDto
                {
                    ExecutionId = executionId,
                    RunId = engineResult.RunId,
                    Output = stepResult.Output,
                    Status = $"Step '{stepResult.StepId}'" + (stepResult.Skipped ? " (skipped)" : string.Empty),
                    StepResults = [stepResult]
                };
            }

            yield return new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                RunId = engineResult.RunId,
                Output = finalOutput,
                Status = finalStatusOverride ?? "Completed",
                StepResults = aggregatedStepResults.Count > 0 ? aggregatedStepResults : null
            };

            streamCompleted = true;
        }
        finally
        {
            // 使用 CancellationToken.None 防止客户端断连导致配额泄漏和数据丢失
            var actualTotalTokens = actualInputTokens + actualOutputTokens;

            try
            {
                await _usageLogService.LogUsageAsync(
                    AIOperationType.WorkflowRun, "Workflow", workflowDef.Name,
                    actualInputTokens, actualOutputTokens, stopwatch.ElapsedMilliseconds,
                    streamCompleted,
                    errorMessage: streamCompleted ? null : "Workflow stream interrupted by exception",
                    ct: CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to log workflow streaming usage: WorkflowId={WorkflowId}", workflowId);
            }

            if (userId.HasValue && reservation != null)
            {
                try
                {
                    // 失败时按 0 结算（退回预留），成功时按实际用量结算
                    var settleTokens = streamCompleted ? actualTotalTokens : 0;
                    await _quotaService.SettleQuotaAsync(userId.Value, reservation, settleTokens, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to settle workflow streaming quota: WorkflowId={WorkflowId}", workflowId);
                }
            }

            if (!streamCompleted && !string.IsNullOrWhiteSpace(executionId))
            {
                await TryUpdateWorkflowExecutionStatusAsync(executionId, "failed", CancellationToken.None);
            }
        }
    }

    public async Task<Result<WorkflowExecutionResultDto>> ResumeAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        // 从检查点存储加载检查点
        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail<WorkflowExecutionResultDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        // 允许从 paused、awaiting_approval 或 failed 状态恢复。
        // failed 场景用于节点重试：检查点里仍保留已完成步骤和失败前的状态，恢复后会从未完成节点继续。
        if (checkpoint.Status is not ("paused" or "awaiting_approval" or "failed"))
            return Fail<WorkflowExecutionResultDto>($"Cannot resume execution in '{checkpoint.Status}' state, expected 'paused', 'awaiting_approval' or 'failed'", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // 检查是否还有步骤等待审批
        if (checkpoint.StepsAwaitingApproval.Count > 0)
            return Fail<WorkflowExecutionResultDto>("Execution has steps awaiting approval, approve or reject them first", 400, ErrorCodes.WorkflowExecutionInvalidState);

        // 查找关联的工作流定义（通过 WorkflowExecution 的 WorkflowDefinitionId）
        var executionEntity = await _executionRepository
            .FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);

        if (executionEntity?.WorkflowDefinitionId == null)
            return Fail<WorkflowExecutionResultDto>("Cannot determine workflow definition for this execution", 400, ErrorCodes.WorkflowExecutionInvalidState);

        var workflowDef = await _repository.GetAsync(executionEntity.WorkflowDefinitionId.Value, ct);
        if (workflowDef == null)
            return Fail<WorkflowExecutionResultDto>("Associated workflow definition not found", 404, ErrorCodes.WorkflowNotFound);

        try
        {
            // 仅 DAG 模式支持恢复
            if (workflowDef.ExecutionMode != WorkflowExecutionMode.Dag)
                return Fail<WorkflowExecutionResultDto>("Resume is only supported for DAG execution mode", 400, ErrorCodes.WorkflowExecutionInvalidState);

            var graph = BuildWorkflowGraph(workflowDef);
            var existingRun = await _runRepository.FirstOrDefaultAsync(r => r.WorkflowExecutionId == executionId, ct);

            var options = new WorkflowExecutionOptions
            {
                ExecutionId = executionId,
                WorkflowDefinitionId = workflowDef.Id,
                RunId = existingRun?.Id,
                Resume = true,
                CheckpointStore = _checkpointStore
            };

            var dagResult = await _workflowEngine.ExecuteAsync(graph, checkpoint.InitialInput, ServiceProvider!, options, ct);

            return Ok(new WorkflowExecutionResultDto
            {
                ExecutionId = executionId,
                RunId = dagResult.RunId,
                Output = dagResult.FinalOutput,
                Status = dagResult.AwaitingApproval
                    ? "AwaitingApproval"
                    : dagResult.HasFailure
                        ? "Failed"
                        : "Completed",
                StepResults = dagResult.StepResults
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to resume workflow execution: {ExecutionId}", executionId);
            return Fail<WorkflowExecutionResultDto>("Failed to resume workflow execution", 500, ErrorCodes.WorkflowFailed);
        }
    }

    public async Task<Result> ApproveStepAsync(string executionId, string stepId, string? feedback = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);
        Check.NotNullOrWhiteSpace(stepId);

        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        if (checkpoint.Status != "awaiting_approval")
            return Fail($"Execution is not awaiting approval, current status: '{checkpoint.Status}'", 400, ErrorCodes.WorkflowExecutionInvalidState);

        if (!checkpoint.StepsAwaitingApproval.Contains(stepId))
            return Fail($"Step '{stepId}' is not awaiting approval", 400, ErrorCodes.WorkflowStepNotAwaitingApproval);

        // 移除该步骤的审批等待
        checkpoint.StepsAwaitingApproval.Remove(stepId);

        // 如果有 feedback，替换步骤输出
        if (!string.IsNullOrWhiteSpace(feedback))
        {
            checkpoint.StepOutputs[stepId] = new WorkflowStepOutput
            {
                Text = feedback,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["approval_feedback"] = feedback
                }
            };
        }

        // 如果没有更多步骤等待审批，将状态改为 paused（等待 resume）
        checkpoint.Status = checkpoint.StepsAwaitingApproval.Count > 0 ? "awaiting_approval" : "paused";
        checkpoint.UpdatedAt = DateTime.UtcNow;

        await _checkpointStore.SaveCheckpointAsync(checkpoint, ct);

        return Ok();
    }

    public async Task<Result> RejectStepAsync(string executionId, string stepId, string reason, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);
        Check.NotNullOrWhiteSpace(stepId);
        Check.NotNullOrWhiteSpace(reason);

        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        if (checkpoint.Status != "awaiting_approval")
            return Fail($"Execution is not awaiting approval, current status: '{checkpoint.Status}'", 400, ErrorCodes.WorkflowExecutionInvalidState);

        if (!checkpoint.StepsAwaitingApproval.Contains(stepId))
            return Fail($"Step '{stepId}' is not awaiting approval", 400, ErrorCodes.WorkflowStepNotAwaitingApproval);

        // 标记步骤输出为拒绝信息
        checkpoint.StepOutputs[stepId] = new WorkflowStepOutput
        {
            Text = $"[Rejected: {reason}]",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["approval_status"] = "rejected",
                ["approval_reason"] = reason
            }
        };
        checkpoint.StepsAwaitingApproval.Clear();
        checkpoint.Status = "failed";
        checkpoint.UpdatedAt = DateTime.UtcNow;

        await _checkpointStore.SaveCheckpointAsync(checkpoint, ct);

        return Ok();
    }

    public async Task<Result<WorkflowExecutionStatusDto>> GetExecutionStatusAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail<WorkflowExecutionStatusDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        return Ok(new WorkflowExecutionStatusDto
        {
            ExecutionId = checkpoint.ExecutionId,
            Status = checkpoint.Status,
            CompletedStepIds = checkpoint.CompletedStepIds.ToList(),
            StepsAwaitingApproval = checkpoint.StepsAwaitingApproval.ToList(),
            CreatedAt = checkpoint.CreatedAt,
            UpdatedAt = checkpoint.UpdatedAt
        });
    }

    private static void AggregateUsage(TokenUsageDto? usage, ref int actualInputTokens, ref int actualOutputTokens)
    {
        if (usage == null) return;
        actualInputTokens += usage.PromptTokens;
        actualOutputTokens += usage.CompletionTokens;
    }

    private async Task<string> EnsureWorkflowExecutionAsync(Guid workflowDefinitionId, string input, CancellationToken ct)
    {
        var executionId = Guid.NewGuid().ToString("N");
        var executionRepository = _executionRepository;

        await executionRepository.InsertAsync(new WorkflowExecution
        {
            ExecutionId = executionId,
            WorkflowDefinitionId = workflowDefinitionId,
            InitialInput = input,
            Status = "running",
            UpdatedTime = DateTime.UtcNow
        }, ct);

        return executionId;
    }

    private async Task TryUpdateWorkflowExecutionStatusAsync(string executionId, string status, CancellationToken ct)
    {
        try
        {
            var executionRepository = _executionRepository;
            var entity = await executionRepository.FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);
            if (entity == null)
            {
                return;
            }

            entity.Status = status;
            entity.UpdatedTime = DateTime.UtcNow;
            if (status is "completed" or "failed")
            {
                entity.CompletedTime ??= entity.UpdatedTime;
            }

            await executionRepository.UpdateAsync(entity, ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to update workflow execution status: {ExecutionId} -> {Status}", executionId, status);
        }
    }

    private static WorkflowDefinitionDto MapToDto(WorkflowDefinition entity)
    {
        var dto = entity.MapTo<WorkflowDefinitionDto>();
        dto.Steps = string.IsNullOrWhiteSpace(entity.Steps)
            ? new List<WorkflowStepDto>()
            : JsonSerializer.Deserialize<List<WorkflowStepDto>>(entity.Steps, TnziJsonDefaults.Options) ?? new();
        return dto;
    }

    private static WorkflowGraph BuildWorkflowGraph(WorkflowDefinition workflowDef)
    {
        var rawSteps = string.IsNullOrWhiteSpace(workflowDef.Steps)
            ? new List<WorkflowStepDto>()
            : JsonSerializer.Deserialize<List<WorkflowStepDto>>(workflowDef.Steps, TnziJsonDefaults.Options) ?? [];

        var steps = rawSteps
            .Select((step, index) => CloneStep(step, index))
            .OrderBy(step => step.Order)
            .ThenBy(step => step.Configuration?.TryGetValue("__originalIndex", out var indexValue) == true && int.TryParse(indexValue, out var parsedIndex)
                ? parsedIndex
                : int.MaxValue)
            .ToList();

        for (var i = 0; i < steps.Count; i++)
        {
            steps[i].StepId ??= $"step-{i + 1}";
            steps[i].Configuration?.Remove("__originalIndex");
        }

        if (workflowDef.ExecutionMode == WorkflowExecutionMode.Sequential)
        {
            for (var i = 1; i < steps.Count; i++)
            {
                var dependsOn = steps[i].DependsOn;
                if (dependsOn == null || dependsOn.Count == 0)
                {
                    var previousStepId = steps[i - 1].StepId;
                    if (!string.IsNullOrWhiteSpace(previousStepId))
                    {
                        steps[i].DependsOn = [previousStepId];
                    }
                }
            }
        }

        WorkflowGraphConfiguration? graphConfig = null;
        if (!string.IsNullOrWhiteSpace(workflowDef.Configuration))
        {
            graphConfig = JsonSerializer.Deserialize<WorkflowGraphConfiguration>(workflowDef.Configuration, TnziJsonDefaults.Options);
        }

        return new WorkflowGraph(
            steps,
            graphConfig?.ConditionalEdges,
            graphConfig?.Loops);
    }

    private static WorkflowStepDto CloneStep(WorkflowStepDto step, int originalIndex)
    {
        var configuration = step.Configuration != null
            ? new Dictionary<string, string>(step.Configuration, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        configuration["__originalIndex"] = originalIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new WorkflowStepDto
        {
            StepId = step.StepId,
            AgentId = step.AgentId,
            Order = step.Order,
            DependsOn = step.DependsOn?.ToList(),
            Condition = step.Condition,
            Provider = step.Provider,
            Model = step.Model,
            Instructions = step.Instructions,
            MaxRetries = step.MaxRetries,
            RetryDelaySeconds = step.RetryDelaySeconds,
            TimeoutSeconds = step.TimeoutSeconds,
            RequiresApproval = step.RequiresApproval,
            Configuration = configuration
        };
    }

    private sealed class WorkflowGraphConfiguration
    {
        public List<ConditionalEdge>? ConditionalEdges { get; init; }

        public Dictionary<string, LoopDefinition>? Loops { get; init; }
    }
}
