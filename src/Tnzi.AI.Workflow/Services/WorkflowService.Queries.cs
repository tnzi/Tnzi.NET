namespace Tnzi.AI.Workflow.Services;

/// <summary>
/// 工作流服务 - 执行查询投影：执行状态 / 详情 / 历史分页 + 状态映射、终态更新、
/// pending signals 加载、JSON 列反序列化等共享私有助手。
/// </summary>
public partial class WorkflowService
{
    public async Task<Result<WorkflowExecutionStatusDto>> GetExecutionStatusAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _executionRepository
            .FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);
        if (entity == null)
            return Fail<WorkflowExecutionStatusDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        var checkpoint = await _checkpointStore.GetCheckpointAsync(executionId, ct);
        if (checkpoint == null)
            return Fail<WorkflowExecutionStatusDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        return Ok(new WorkflowExecutionStatusDto
        {
            ExecutionId = checkpoint.ExecutionId,
            Status = checkpoint.Status.ToString(),
            CompletedStepIds = checkpoint.CompletedStepIds.ToList(),
            StepsAwaitingApproval = checkpoint.StepsAwaitingApproval.ToList(),
            CreatedAt = checkpoint.CreatedAt,
            UpdatedAt = checkpoint.UpdatedAt,
            PendingSignalCount = entity.PendingSignalCount,
            CurrentWaitReason = entity.CurrentWaitReason
        });
    }

    public async Task<Result<WorkflowExecutionDetailDto>> GetExecutionDetailAsync(string executionId)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _executionRepository
            .FirstOrDefaultAsync(e => e.ExecutionId == executionId);

        if (entity == null)
            return Fail<WorkflowExecutionDetailDto>("Workflow execution not found", 404, ErrorCodes.WorkflowExecutionNotFound);

        var completedStepIds = DeserializeJsonList(entity.CompletedSteps);
        var stepsAwaiting = DeserializeJsonList(entity.StepsAwaitingApproval);
        var stepOutputs = DeserializeJsonDict(entity.StepOutputs);

        // 终态（Completed/Failed/Cancelled）时不加载 pending signals，必然为空
        var isTerminal = entity.Status is WorkflowExecutionStatus.Completed
            or WorkflowExecutionStatus.Failed
            or WorkflowExecutionStatus.Cancelled;

        return Ok(new WorkflowExecutionDetailDto
        {
            Id = entity.Id,
            ExecutionId = entity.ExecutionId,
            WorkflowDefinitionId = entity.WorkflowDefinitionId,
            Status = entity.Status,
            InitialInput = entity.InitialInput,
            CompletedStepIds = completedStepIds,
            CompletedStepCount = completedStepIds.Count,
            StepsAwaitingApproval = stepsAwaiting,
            AwaitingApprovalCount = stepsAwaiting.Count,
            StepOutputs = stepOutputs,
            StartedAt = entity.StartedAt,
            DurationMs = entity.DurationMs,
            CreationTime = entity.CreationTime,
            CompletedTime = entity.CompletedTime,
            UpdatedTime = entity.UpdatedTime,
            PendingSignalCount = entity.PendingSignalCount,
            CurrentWaitReason = entity.CurrentWaitReason,
            PendingSignals = isTerminal ? [] : await LoadPendingSignalsAsync(entity.ExecutionId)
        });
    }

    public async Task<Result<IPagedList<WorkflowExecutionSummaryDto>>> GetExecutionsAsync(WorkflowExecutionQueryDto query)
    {
        Check.NotNull(query);

        var pagedEntities = await _executionRepository.AsQueryable()
            .WhereIf(e => e.WorkflowDefinitionId == query.WorkflowDefinitionId!.Value, query.WorkflowDefinitionId.HasValue)
            .WhereIf(e => e.Status == query.Status!.Value, query.Status.HasValue)
            .OrderByDescending(e => e.CreationTime)
            .CreateAsync(query);

        // 分页后在内存中反序列化 JSON 列，得出真实的步骤计数（每页条目少，成本可接受）
        var items = pagedEntities.Items
            .Select(e => new WorkflowExecutionSummaryDto
            {
                Id = e.Id,
                ExecutionId = e.ExecutionId,
                WorkflowDefinitionId = e.WorkflowDefinitionId,
                Status = e.Status,
                CompletedStepCount = DeserializeJsonList(e.CompletedSteps).Count,
                AwaitingApprovalCount = DeserializeJsonList(e.StepsAwaitingApproval).Count,
                StartedAt = e.StartedAt,
                DurationMs = e.DurationMs,
                CreationTime = e.CreationTime,
                CompletedTime = e.CompletedTime,
                UpdatedTime = e.UpdatedTime,
                PendingSignalCount = e.PendingSignalCount,
                CurrentWaitReason = e.CurrentWaitReason
            })
            .ToList();

        IPagedList<WorkflowExecutionSummaryDto> pagedList = new PagedList<WorkflowExecutionSummaryDto>(
            items, pagedEntities.PageIndex, pagedEntities.PageSize, pagedEntities.TotalCount);

        return Ok(pagedList);
    }

    private static WorkflowExecutionStatus MapExecutionStatus(string? statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText))
        {
            return WorkflowExecutionStatus.Running;
        }

        if (statusText.StartsWith("PartialFailure", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowExecutionStatus.Failed;
        }

        return statusText switch
        {
            nameof(WorkflowExecutionStatus.Completed) => WorkflowExecutionStatus.Completed,
            nameof(WorkflowExecutionStatus.Failed) => WorkflowExecutionStatus.Failed,
            nameof(WorkflowExecutionStatus.Cancelled) => WorkflowExecutionStatus.Cancelled,
            nameof(WorkflowExecutionStatus.Paused) => WorkflowExecutionStatus.Paused,
            nameof(WorkflowExecutionStatus.AwaitingApproval) => WorkflowExecutionStatus.AwaitingApproval,
            nameof(WorkflowExecutionStatus.AwaitingInput) => WorkflowExecutionStatus.AwaitingInput,
            _ => WorkflowExecutionStatus.Running
        };
    }

    /// <summary>
    /// 把引擎给出的终态写回 <c>WorkflowExecution</c> 行。★ 引擎自己的结局是权威的：看门狗若在执行期间
    /// 误判回收过这一行（Failed / timed_out / 假的 CompletedTime），这里**覆盖**而不是 <c>??=</c> 保留 ——
    /// 否则 DurationMs 会按那个假的结束时刻算；且看门狗那次写会推进并发标记，本作用域从插入起就跟踪着
    /// 这个实体、原始标记已过期，直接保存会撞 <see cref="DbUpdateConcurrencyException"/> 然后被吞成一条
    /// Warning，行就永远停在 timed_out。撞上时重读一次再按本方法的意图重写。
    /// </summary>
    private async Task TryUpdateWorkflowExecutionStatusAsync(string executionId, WorkflowExecutionStatus status, CancellationToken ct, string? currentWaitReason = null)
    {
        try
        {
            var entity = await _executionRepository.FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);
            if (entity == null)
            {
                return;
            }

            ApplyTerminalStatus(entity, status, currentWaitReason);

            try
            {
                await _executionRepository.UpdateAsync(entity, ct);
            }
            catch (DbUpdateConcurrencyException conflict)
            {
                foreach (var entry in conflict.Entries)
                {
                    await entry.ReloadAsync(ct);
                    if (entry.Entity is WorkflowExecution reloaded)
                    {
                        ApplyTerminalStatus(reloaded, status, currentWaitReason);
                        entry.State = EntityState.Modified;
                    }
                }

                await _executionRepository.SaveChangesAsync(ct);
                Logger.LogInformation("Workflow execution {ExecutionId} was modified concurrently (watchdog?) before its terminal update; re-applied {Status} on the reloaded row.", executionId, status);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to update workflow execution status: {ExecutionId} -> {Status}", executionId, status);
        }
    }

    private static void ApplyTerminalStatus(WorkflowExecution entity, WorkflowExecutionStatus status, string? currentWaitReason)
    {
        var now = DateTime.UtcNow;
        entity.Status = status;
        entity.CurrentWaitReason = currentWaitReason;
        entity.UpdatedTime = now;
        if (status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.Cancelled)
        {
            entity.CompletedTime = now;
            if (entity.StartedAt.HasValue)
            {
                entity.DurationMs = (long)(now - entity.StartedAt.Value).TotalMilliseconds;
            }
        }
    }

    private async Task<List<WorkflowExecutionSignal>> LoadPendingSignalsAsync(string executionId)
    {
        var mailbox = ServiceProvider?.GetService<IWorkflowExecutionMailbox>();
        if (mailbox == null)
        {
            return [];
        }

        return await mailbox.GetPendingSignalsAsync(executionId, CancellationToken.None);
    }

    private static List<string> DeserializeJsonList(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, TnziJsonDefaults.Options) ?? []; }
        catch { return []; }
    }

    private static Dictionary<string, string> DeserializeJsonDict(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new();
        try
        {
            // StepOutputs is stored as Dictionary<string, WorkflowStepOutput> in checkpoint,
            // but in execution entity it's a flat JSON object. Parse flexibly.
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.GetRawText();
            }
            return dict;
        }
        catch { return new(); }
    }
}
