namespace Tnzi.AI.Streaming;

/// <summary>
/// 步骤级流式事件类型常量（StreamMode.Steps）
/// </summary>
public static class StepStreamEventTypes
{
    /// <summary>Agent 开始执行</summary>
    public const string AgentStarted = "agent_started";

    /// <summary>Agent 执行完成</summary>
    public const string AgentCompleted = "agent_completed";

    /// <summary>Handoff 发生（Agent 间转接）</summary>
    public const string HandoffOccurred = "handoff_occurred";

    /// <summary>Workflow 步骤开始</summary>
    public const string WorkflowStepStarted = "workflow_step_started";

    /// <summary>Workflow 步骤完成</summary>
    public const string WorkflowStepCompleted = "workflow_step_completed";

    /// <summary>工具调用开始</summary>
    public const string ToolCallStarted = "tool_call_started";

    /// <summary>工具调用完成</summary>
    public const string ToolCallCompleted = "tool_call_completed";
}

/// <summary>
/// 调试级流式事件类型常量（StreamMode.Debug）
/// </summary>
public static class DebugStreamEventTypes
{
    /// <summary>中间件进入</summary>
    public const string MiddlewareEnter = "middleware_enter";

    /// <summary>中间件退出</summary>
    public const string MiddlewareExit = "middleware_exit";

    /// <summary>上下文注入完成</summary>
    public const string ContextInjected = "context_injected";

    /// <summary>历史消息加载完成</summary>
    public const string HistoryLoaded = "history_loaded";

    /// <summary>Token 预算分配</summary>
    public const string TokenBudget = "token_budget";
}
