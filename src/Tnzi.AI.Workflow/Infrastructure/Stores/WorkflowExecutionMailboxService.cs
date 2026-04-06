namespace Tnzi.AI.Infrastructure.Stores;

/// <summary>
/// 基于 WorkflowExecution 的执行邮箱
/// </summary>
[ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
public class WorkflowExecutionMailboxService : IWorkflowExecutionMailbox
{
    private readonly IRepository<WorkflowExecution, Guid> _repository;

    public WorkflowExecutionMailboxService(IRepository<WorkflowExecution, Guid> repository)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<List<WorkflowExecutionSignal>> GetPendingSignalsAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _repository.FirstOrDefaultAsync(x => x.ExecutionId == executionId, ct);
        if (entity == null)
        {
            return [];
        }

        return DeserializeSignals(entity.PendingSignalsJson);
    }

    public async Task EnqueueSignalAsync(string executionId, WorkflowExecutionSignal signal, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);
        Check.NotNull(signal);

        var entity = await _repository.FirstOrDefaultAsync(x => x.ExecutionId == executionId, ct)
            ?? throw new BusinessException("Workflow execution not found", ErrorCodes.WorkflowExecutionNotFound, 404);

        var signals = DeserializeSignals(entity.PendingSignalsJson);
        signals.Add(signal);

        entity.PendingSignalsJson = JsonSerializer.Serialize(signals, TnziJsonDefaults.Options);
        entity.PendingSignalCount = signals.Count;
        entity.UpdatedTime = DateTime.UtcNow;
        entity.CurrentWaitReason ??= signal.Type == WorkflowExecutionSignalTypes.Cancel ? "cancel_requested" : "external_signal";
        await _repository.UpdateAsync(entity, ct);
    }

    public async Task AcknowledgeSignalsAsync(string executionId, IEnumerable<string> signalIds, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);
        Check.NotNull(signalIds);

        var signalIdSet = signalIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (signalIdSet.Count == 0)
        {
            return;
        }

        var entity = await _repository.FirstOrDefaultAsync(x => x.ExecutionId == executionId, ct);
        if (entity == null)
        {
            return;
        }

        var signals = DeserializeSignals(entity.PendingSignalsJson);
        signals.RemoveAll(x => signalIdSet.Contains(x.SignalId));
        entity.PendingSignalsJson = JsonSerializer.Serialize(signals, TnziJsonDefaults.Options);
        entity.PendingSignalCount = signals.Count;
        entity.UpdatedTime = DateTime.UtcNow;
        if (entity.PendingSignalCount == 0 && string.Equals(entity.CurrentWaitReason, "cancel_requested", StringComparison.OrdinalIgnoreCase))
        {
            entity.CurrentWaitReason = null;
        }

        await _repository.UpdateAsync(entity, ct);
    }

    public async Task ClearSignalsAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _repository.FirstOrDefaultAsync(x => x.ExecutionId == executionId, ct);
        if (entity == null)
        {
            return;
        }

        entity.PendingSignalsJson = "[]";
        entity.PendingSignalCount = 0;
        entity.UpdatedTime = DateTime.UtcNow;
        if (string.Equals(entity.CurrentWaitReason, "cancel_requested", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entity.CurrentWaitReason, "external_signal", StringComparison.OrdinalIgnoreCase))
        {
            entity.CurrentWaitReason = null;
        }

        await _repository.UpdateAsync(entity, ct);
    }

    private static List<WorkflowExecutionSignal> DeserializeSignals(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<WorkflowExecutionSignal>>(json, TnziJsonDefaults.Options) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
