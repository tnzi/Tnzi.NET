namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// Tokens the nodes of one engine call have consumed so far.
/// </summary>
/// <remarks>
/// The engine's own totals leave it only through the result of a call that returns. A call that
/// throws (the client disconnected, a gateway timed the request out, a checkpoint write failed)
/// returns nothing, yet every node that finished before it was billed by the provider. The meter
/// is filled as each node finishes, including nodes that finish in the same layer as the one that
/// throws, so the caller can log and settle what was really consumed on every exit path.
/// </remarks>
public sealed class WorkflowUsageMeter
{
    private int _inputTokens;
    private int _outputTokens;

    public int InputTokens => Volatile.Read(ref _inputTokens);

    public int OutputTokens => Volatile.Read(ref _outputTokens);

    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>Add one node's usage. Safe to call from the parallel node tasks of a layer.</summary>
    public void Add(TokenUsageDto? usage)
    {
        if (usage == null) return;

        Interlocked.Add(ref _inputTokens, usage.InputTokens);
        Interlocked.Add(ref _outputTokens, usage.OutputTokens);
    }
}
