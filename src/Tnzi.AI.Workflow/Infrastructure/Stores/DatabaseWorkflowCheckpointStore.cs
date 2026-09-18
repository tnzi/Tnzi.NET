namespace Tnzi.AI.Workflow.Infrastructure.Stores;

/// <summary>
/// 数据库工作流检查点存储 - 基于 IRepository 持久化工作流执行状态
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
    // violation) or an Update hits an optimistic-concurrency conflict (stale token).
    // After this many attempts we rethrow so the caller sees the failure rather than
    // silently dropping state.
    private const int SaveCheckpointMaxRetries = 3;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Two concurrent writers with the same ExecutionId can both observe no existing
    /// row and both attempt Insert. The unique index on ExecutionId (see
    /// WorkflowExecutionConfiguration) causes one of them to fail with a unique
    /// violation. We catch that, re-read, and retry as Update.
    /// </para>
    /// <para>
    /// Once the row exists, two concurrent writers can both read the same
    /// ConcurrencyStamp and both attempt Update. The optimistic-concurrency token
    /// (WorkflowExecution.ConcurrencyStamp, auto-bumped by AuditPropertyHelper) makes
    /// the second SaveChanges throw <see cref="DbUpdateConcurrencyException"/>. We
    /// reload the conflicting entry's fresh database values, UNION-MERGE the incoming
    /// checkpoint's CompletedSteps/StepOutputs onto that fresh state (so the concurrent
    /// writer's steps are preserved rather than overwritten), and retry. Bounded retries
    /// (SaveCheckpointMaxRetries) prevent runaway loops under pathological contention.
    /// </para>
    /// <para>
    /// ★ The union-merge lives ONLY on that conflict path. A writer that read the current
    /// row and saved without conflict is authoritative: its CompletedSteps / StepOutputs
    /// replace the persisted ones. Merging on the normal path too (as this store did from
    /// 2026-05-29) silently resurrected every step a caller deliberately removed - the
    /// service's "re-enter the interrupted node" removal in ResumeWithInputAsync and the
    /// engine's loop-body reset both write a smaller set on purpose - so human input was
    /// never delivered while every call reported success.
    /// </para>
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
                    await _repository.InsertAsync(entity, ct);
                    _logger.LogDebug("Created workflow checkpoint for execution {ExecutionId}", checkpoint.ExecutionId);
                }
                else
                {
                    ApplyCheckpoint(entity, checkpoint, mergeWithPersisted: false);
                    await _repository.UpdateAsync(entity, ct);
                    _logger.LogDebug("Updated workflow checkpoint for execution {ExecutionId}, status: {Status}",
                        checkpoint.ExecutionId, checkpoint.Status);
                }

                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Lost the optimistic-concurrency race: a concurrent writer bumped the
                // ConcurrencyStamp between our read and our SaveChanges. Reload the fresh
                // DB state, UNION-MERGE the incoming checkpoint, and re-save the same
                // tracked instance until it sticks (so neither writer's completed steps
                // are dropped). Re-reading via FirstOrDefaultAsync here would collide
                // with the still-tracked conflicting instance, so we operate on the
                // tracked entry directly.
                if (attempt >= SaveCheckpointMaxRetries)
                {
                    throw;
                }

                _logger.LogDebug(ex,
                    "Checkpoint concurrency conflict for {ExecutionId}, reloading + merging (attempt {Attempt}/{Max})",
                    checkpoint.ExecutionId, attempt, SaveCheckpointMaxRetries);

                await ResolveConcurrencyConflictAndSaveAsync(ex, checkpoint, attempt, ct);
                return;
            }
            catch (DbUpdateException ex) when (attempt < SaveCheckpointMaxRetries)
            {
                // Lost the Insert race (unique violation). The next iteration re-reads
                // and proceeds as an Update. Small backoff scaled by attempt reduces
                // hot-loop contention without significantly slowing normal operation.
                _logger.LogDebug(ex,
                    "Checkpoint insert conflict for {ExecutionId}, retrying (attempt {Attempt}/{Max})",
                    checkpoint.ExecutionId, attempt, SaveCheckpointMaxRetries);
                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), ct);
            }
        }
    }

    /// <summary>
    /// Resolve an optimistic-concurrency conflict by reloading the conflicting entry's
    /// fresh database values (the winning writer's committed state), re-applying the
    /// incoming checkpoint as a UNION-MERGE so both writers' completed steps/outputs are
    /// preserved, and re-saving the already-tracked instance. Loops until the save sticks
    /// or the retry budget is exhausted (then rethrows).
    /// </summary>
    private async Task ResolveConcurrencyConflictAndSaveAsync(
        DbUpdateConcurrencyException conflict,
        WorkflowCheckpoint checkpoint,
        int attempt,
        CancellationToken ct)
    {
        var current = conflict;
        while (true)
        {
            foreach (var entry in current.Entries)
            {
                // Refresh the tracked entity's current + original values from the database
                // (the concurrent writer's committed state). This clears the stale
                // ConcurrencyStamp original value and surfaces the latest persisted steps.
                await entry.ReloadAsync(ct);

                if (entry.Entity is WorkflowExecution reloaded)
                {
                    // Re-merge the incoming checkpoint onto the just-reloaded fresh state.
                    ApplyCheckpoint(reloaded, checkpoint, mergeWithPersisted: true);
                    entry.State = EntityState.Modified;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), ct);

            try
            {
                await _repository.SaveChangesAsync(ct);
                _logger.LogDebug(
                    "Resolved checkpoint concurrency conflict for {ExecutionId} after reload + union-merge",
                    checkpoint.ExecutionId);
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                attempt++;
                if (attempt >= SaveCheckpointMaxRetries)
                {
                    throw;
                }

                _logger.LogDebug(ex,
                    "Checkpoint concurrency conflict persisted for {ExecutionId}, reloading + merging again (attempt {Attempt}/{Max})",
                    checkpoint.ExecutionId, attempt, SaveCheckpointMaxRetries);
                current = ex;
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

    /// <summary>
    /// Apply an incoming checkpoint onto a (possibly already-persisted) entity.
    /// </summary>
    /// <param name="entity">The tracked row.</param>
    /// <param name="checkpoint">The incoming checkpoint.</param>
    /// <param name="mergeWithPersisted">
    /// <c>false</c> (normal path): the incoming CompletedSteps / StepOutputs are
    /// authoritative and replace the persisted ones - the writer read the current row and
    /// the concurrency stamp proves nobody wrote in between, so a smaller set is a
    /// deliberate removal, not a lost update.
    /// <c>true</c> (conflict path only): UNION-MERGE with the values just reloaded from the
    /// database so the concurrent writer's completed steps and step outputs survive
    /// instead of being clobbered by a last-write-wins update. On key collisions the
    /// incoming checkpoint wins (it carries the freshest output for a step this writer
    /// just produced).
    /// Status / wait-reason / interrupt are always last-writer (the incoming checkpoint
    /// reflects the latest authored intent).
    /// </param>
    private void ApplyCheckpoint(WorkflowExecution entity, WorkflowCheckpoint checkpoint, bool mergeWithPersisted)
    {
        var steps = mergeWithPersisted
            ? DeserializeHashSet(entity.CompletedSteps)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        steps.UnionWith(checkpoint.CompletedStepIds);
        entity.CompletedSteps = JsonSerializer.Serialize(steps);

        var outputs = mergeWithPersisted
            ? DeserializeStepOutputs(entity.StepOutputs)
            : new Dictionary<string, WorkflowStepOutput>(StringComparer.OrdinalIgnoreCase);
        foreach (var (stepId, output) in checkpoint.StepOutputs)
        {
            outputs[stepId] = output;
        }
        entity.StepOutputs = JsonSerializer.Serialize(outputs);

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
            await _repository.DeleteAsync(entity, ct);
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
