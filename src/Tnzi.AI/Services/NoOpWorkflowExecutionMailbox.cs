namespace Tnzi.AI.Services;

public class NoOpWorkflowExecutionMailbox : IWorkflowExecutionMailbox, INoOpService
{
    public Task<List<WorkflowExecutionSignal>> GetPendingSignalsAsync(string executionId, CancellationToken ct = default)
        => Task.FromResult(new List<WorkflowExecutionSignal>());

    public Task EnqueueSignalAsync(string executionId, WorkflowExecutionSignal signal, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AcknowledgeSignalsAsync(string executionId, IEnumerable<string> signalIds, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ClearSignalsAsync(string executionId, CancellationToken ct = default)
        => Task.CompletedTask;
}
