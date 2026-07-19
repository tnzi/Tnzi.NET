namespace Tnzi.AI.Metadata;

/// <summary>
/// Agent 运行追踪事件类型常量
/// </summary>
public static class AgentTraceEventTypes
{
    public const string Error = "error";
    public const string RunCompleted = "run_completed";
    public const string RunRejected = "run_rejected";
    public const string RunResumed = "run_resumed";
    public const string StreamCompleted = "stream_completed";
    public const string StreamCancelled = "stream_cancelled";
    public const string NodeExecute = "node_execute";
    public const string NodeError = "node_error";
    public const string NodeRetryRequested = "node_retry_requested";
}
