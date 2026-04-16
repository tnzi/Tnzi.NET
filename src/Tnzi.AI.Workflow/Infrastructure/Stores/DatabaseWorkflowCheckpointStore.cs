namespace Tnzi.AI.Infrastructure.Stores;

/// <summary>
/// 数据库工作流检查点存储 — 基于 IRepository 持久化工作流执行状态
/// </summary>
public class DatabaseWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    private readonly IRepository<WorkflowExecution, Guid> _repository;
    private readonly ILogger<DatabaseWorkflowCheckpointStore> _logger;

    public DatabaseWorkflowCheckpointStore(
        IRepository<WorkflowExecution, Guid> repository,
        ILogger<DatabaseWorkflowCheckpointStore> logger)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
    }

    // Max retry attempts when a concurrent writer wins the Insert race (unique index
    // violation) or an Update hits a concurrency conflict. After this many attempts
    // we rethrow so the caller sees the failure rather than silently dropping state.
    private const int SaveCheckpointMaxRetries = 3;

    /// <inheritdoc />
    /// <remarks>
    /// Two concurrent writers with the same ExecutionId can both observe no existing
    /// row and both attempt Insert. The unique index on ExecutionId (see
    /// WorkflowExecutionConfiguration) causes one of them to fail with a unique
    /// violation. We catch that, re-read, and retry as Update. Bounded retries
    /// (SaveCheckpointMaxRetries) prevent runaway loops under pathological contention.
    /// </remarks>
    public async Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
    {
        Check.NotNull(checkpoint);
        Check.NotNullOrWhiteSpace(checkpoint.ExecutionId);

        for (var attempt = 1; attempt <= SaveCheckpointMaxRetries; attempt++)
        {
            try
            {
                var entity = await _repository.FirstOrDefaultAsync(e => e.ExecutionId == checkpoint.ExecutionId, ct);

                if (entity == null)
                {
                    entity = BuildNewEntity(checkpoint);
                    await _repository.InsertAsync(entity);
                    _logger.LogDebug("Created workflow checkpoint for execution {ExecutionId}", checkpoint.ExecutionId);
                }
                else
                {
                    ApplyCheckpoint(entity, checkpoint);
                    await _repository.UpdateAsync(entity);
                    _logger.LogDebug("Updated workflow checkpoint for execution {ExecutionId}, status: {Status}",
                        checkpoint.ExecutionId, checkpoint.Status);
                }

                return;
            }
            catch (DbUpdateException ex) when (attempt < SaveCheckpointMaxRetries)
            {
                // Lost the Insert race (unique violation) OR concurrent Update conflict.
                // Small backoff scaled by attempt reduces hot-loop contention without
                // significantly slowing down normal operation.
                _logger.LogDebug(ex,
                    "Checkpoint write conflict for {ExecutionId}, retrying (attempt {Attempt}/{Max})",
                    checkpoint.ExecutionId, attempt, SaveCheckpointMaxRetries);
                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), ct);
            }
        }
    }

    private WorkflowExecution BuildNewEntity(WorkflowCheckpoint checkpoint)
    {
        var entity = new WorkflowExecution
        {
            ExecutionId = checkpoint.ExecutionId,
            InitialInput = checkpoint.InitialInput,
            CompletedSteps = JsonSerializer.Serialize(checkpoint.CompletedStepIds),
            StepOutputs = JsonSerializer.Serialize(checkpoint.StepOutputs),
            Status = checkpoint.Status,
            UpdatedTime = checkpoint.UpdatedAt == default ? DateTime.UtcNow : checkpoint.UpdatedAt,
            StepsAwaitingApproval = JsonSerializer.Serialize(checkpoint.StepsAwaitingApproval),
            PendingInterruptJson = checkpoint.PendingInterruptJson,
            CurrentWaitReason = ResolveWaitReason(checkpoint)
        };

        if (checkpoint.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.Cancelled)
        {
            entity.CompletedTime = DateTime.UtcNow;
        }

        return entity;
    }

    private void ApplyCheckpoint(WorkflowExecution entity, WorkflowCheckpoint checkpoint)
    {
        entity.CompletedSteps = JsonSerializer.Serialize(checkpoint.CompletedStepIds);
        entity.StepOutputs = JsonSerializer.Serialize(checkpoint.StepOutputs);
        entity.Status = checkpoint.Status;
        entity.UpdatedTime = checkpoint.UpdatedAt == default ? DateTime.UtcNow : checkpoint.UpdatedAt;
        entity.StepsAwaitingApproval = JsonSerializer.Serialize(checkpoint.StepsAwaitingApproval);
        entity.PendingInterruptJson = checkpoint.PendingInterruptJson;
        entity.CurrentWaitReason = ResolveWaitReason(checkpoint);

        if (checkpoint.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.Cancelled)
        {
            entity.CompletedTime = DateTime.UtcNow;
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowCheckpoint?> GetCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _repository.FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);

        if (entity == null)
        {
            return null;
        }

        return new WorkflowCheckpoint
        {
            ExecutionId = entity.ExecutionId,
            CompletedStepIds = DeserializeHashSet(entity.CompletedSteps),
            StepOutputs = DeserializeStepOutputs(entity.StepOutputs),
            InitialInput = entity.InitialInput,
            CreatedAt = entity.CreationTime,
            UpdatedAt = entity.UpdatedTime,
            Status = entity.Status,
            StepsAwaitingApproval = DeserializeHashSet(entity.StepsAwaitingApproval),
            PendingInterruptJson = entity.PendingInterruptJson
        };
    }

    /// <inheritdoc />
    public async Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _repository.FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);

        if (entity != null)
        {
            await _repository.DeleteAsync(entity);
            _logger.LogDebug("Deleted workflow checkpoint for execution {ExecutionId}", executionId);
        }
    }

    /// <summary>
    /// Deserialize a JSON array into a case-insensitive HashSet. On malformed
    /// input we return an empty set so the workflow can resume, but we log a
    /// Warning so operators can detect checkpoint corruption before it leads
    /// to silent state loss.
    /// </summary>
    private HashSet<string> DeserializeHashSet(string json)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list != null ? new HashSet<string>(list, StringComparer.OrdinalIgnoreCase) : [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to deserialize workflow checkpoint HashSet; returning empty. Raw (truncated): {Raw}",
                Truncate(json));
            return [];
        }
    }

    /// <summary>
    /// Deserialize a JSON object into a case-insensitive step-output dictionary.
    /// Mirrors <see cref="DeserializeHashSet"/>: returns empty + Warning on
    /// corruption rather than throwing, so workflow resume is best-effort.
    /// </summary>
    private Dictionary<string, WorkflowStepOutput> DeserializeStepOutputs(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning(
                    "Workflow checkpoint step outputs expected JSON object, got {Kind}. Raw (truncated): {Raw}",
                    document.RootElement.ValueKind, Truncate(json));
                return new(StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, WorkflowStepOutput>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    result[property.Name] = property.Value.GetString() ?? string.Empty;
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    var output = property.Value.Deserialize<WorkflowStepOutput>(TnziJsonDefaults.Options);
                    if (output != null)
                    {
                        result[property.Name] = output;
                    }
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to deserialize workflow checkpoint step outputs; returning empty. Raw (truncated): {Raw}",
                Truncate(json));
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 200 ? value : value[..200] + "...";

    private static string? ResolveWaitReason(WorkflowCheckpoint checkpoint)
    {
        if (checkpoint.Status == WorkflowExecutionStatus.AwaitingApproval)
        {
            return "approval";
        }

        if (checkpoint.Status == WorkflowExecutionStatus.AwaitingInput)
        {
            return "input";
        }

        if (checkpoint.Status == WorkflowExecutionStatus.Cancelled)
        {
            return "cancelled";
        }

        return null;
    }
}
