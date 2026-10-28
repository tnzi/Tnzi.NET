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

    /// <summary>
    /// 这一轮是否以一个答复收场（会写进会话历史的那种结局）。被拒、配额不足、失败、转接超限都不算；
    /// 没有结束原因（部分提供方不回报）按正常收场处理。
    /// </summary>
    /// <remarks>
    /// 写会话历史（<c>HistoryMiddleware</c>）与轮后提炼（上下文提供器的 <c>OnCompletedAsync</c>）用同一判据：
    /// 下一轮读回来的历史里有这一轮，这一轮的记忆就该被提炼；反之亦然。
    /// </remarks>
    internal static bool EndsWithAnswer(string? finishReason) => finishReason switch
    {
        GuardrailRejected or QuotaExceeded or Error or Failed or Rejected or MaxHandoffs => false,
        _ => true
    };
}
