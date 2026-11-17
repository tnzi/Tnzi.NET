namespace Tnzi.AI.Metadata;

/// <summary>
/// AI 操作类型
/// </summary>
public static class AIOperationType
{
    public const string Chat = "Chat";
    public const string ChatStreaming = "ChatStreaming";
    public const string AgentRun = "AgentRun";
    public const string AgentRunStreaming = "AgentRunStreaming";
    public const string WorkflowRun = "WorkflowRun";
    public const string WorkflowRunStreaming = "WorkflowRunStreaming";
    public const string Evaluation = "Evaluation";

    /// <summary>给新线程起标题的那一次模型调用（不经中间件管线，由 <c>ThreadTitleService</c> 记账）。</summary>
    public const string ThreadTitle = "ThreadTitle";
}
