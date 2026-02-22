namespace Tnzi.AI;

/// <summary>
/// 工作流执行模式
/// </summary>
public enum WorkflowExecutionMode
{
    /// <summary>
    /// 顺序执行 - Agent 按顺序依次执行
    /// </summary>
    Sequential,

    /// <summary>
    /// 并行执行 - Agent 同时执行
    /// </summary>
    Parallel
}

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
}

/// <summary>
/// 消息角色常量
/// </summary>
public static class MessageRole
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}
