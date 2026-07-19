namespace Tnzi.AI.Metadata;

/// <summary>
/// AI 执行完成原因常量
/// </summary>
public static class FinishReasons
{
    public const string Stop = "stop";
    public const string Error = "error";
    public const string Completed = "completed";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Failed = "failed";
    public const string GuardrailRejected = "guardrail_rejected";
    public const string QuotaExceeded = "quota_exceeded";
    public const string MaxToolIterations = "max_tool_iterations";
    public const string MaxHandoffs = "max_handoffs";
    public const string AgentAsToolsComplete = "agent_as_tools_complete";
    public const string RequiresClarification = "requires_clarification";
}
