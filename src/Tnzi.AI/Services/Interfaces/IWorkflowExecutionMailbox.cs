namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 工作流执行邮箱
/// </summary>
[ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
public interface IWorkflowExecutionMailbox
{
    /// <summary>获取待处理信号</summary>
    Task<List<WorkflowExecutionSignal>> GetPendingSignalsAsync(string executionId, CancellationToken ct = default);

    /// <summary>追加信号</summary>
    Task EnqueueSignalAsync(string executionId, WorkflowExecutionSignal signal, CancellationToken ct = default);

    /// <summary>确认并移除指定信号</summary>
    Task AcknowledgeSignalsAsync(string executionId, IEnumerable<string> signalIds, CancellationToken ct = default);

    /// <summary>清空信号</summary>
    Task ClearSignalsAsync(string executionId, CancellationToken ct = default);
}
