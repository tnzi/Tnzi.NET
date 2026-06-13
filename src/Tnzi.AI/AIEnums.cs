namespace Tnzi.AI;

/// <summary>
/// 工作流执行模式
/// </summary>
public enum WorkflowExecutionMode
{
    /// <summary>
    /// 顺序执行 — Agent 按顺序依次执行
    /// </summary>
    Sequential,

    /// <summary>
    /// 并行执行 — Agent 同时执行
    /// </summary>
    Parallel,

    /// <summary>
    /// DAG 执行 — 按依赖关系拓扑排序，同层无依赖步骤自动并行
    /// </summary>
    Dag
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

/// <summary>
/// Agent 执行模式
/// </summary>
public enum AgentExecutionMode
{
    /// <summary>单 Agent 执行（默认）</summary>
    Single = 0,

    /// <summary>Agent 间转接（Handoff）</summary>
    Handoff = 1,

    /// <summary>Agent 作为工具（父 Agent 调用子 Agent 作为工具函数）</summary>
    AgentAsTools = 2,

    /// <summary>路由 Agent 选择最合适的目标 Agent</summary>
    Router = 3
}

/// <summary>
/// 运行状态枚举
/// </summary>
public enum AgentRunStatus
{
    /// <summary>等待执行</summary>
    Pending,

    /// <summary>执行中</summary>
    Running,

    /// <summary>等待人工审批</summary>
    AwaitingApproval,

    /// <summary>执行完成</summary>
    Completed,

    /// <summary>执行失败</summary>
    Failed,

    /// <summary>已取消</summary>
    Cancelled,

    /// <summary>等待用户澄清</summary>
    RequiresClarification
}

/// <summary>
/// 澄清请求类型
/// </summary>
public enum ClarificationType
{
    /// <summary>缺少必要信息</summary>
    MissingInfo,

    /// <summary>需求描述模糊</summary>
    AmbiguousRequirement,

    /// <summary>方案选择</summary>
    ApproachChoice,

    /// <summary>风险确认</summary>
    RiskConfirmation,

    /// <summary>建议确认</summary>
    Suggestion
}

/// <summary>
/// Todo 项状态
/// </summary>
public enum TodoStatus
{
    /// <summary>待处理</summary>
    Pending,

    /// <summary>进行中</summary>
    InProgress,

    /// <summary>已完成</summary>
    Completed,

    /// <summary>已跳过</summary>
    Skipped
}

/// <summary>
/// 使用量趋势统计时间粒度
/// </summary>
public enum UsageGranularity
{
    /// <summary>按天统计</summary>
    Daily = 0,

    /// <summary>按周统计</summary>
    Weekly = 1,

    /// <summary>按月统计</summary>
    Monthly = 2
}

/// <summary>
/// 运行节点状态枚举
/// </summary>
public enum AgentRunNodeStatus
{
    /// <summary>等待执行</summary>
    Pending,

    /// <summary>执行中</summary>
    Running,

    /// <summary>执行完成</summary>
    Completed,

    /// <summary>执行失败</summary>
    Failed,

    /// <summary>已跳过</summary>
    Skipped,

    /// <summary>等待人工审批</summary>
    AwaitingApproval,

    /// <summary>已批准</summary>
    Approved,

    /// <summary>已拒绝</summary>
    Rejected
}

/// <summary>
/// 评估运行状态枚举
/// </summary>
public enum EvaluationRunStatus
{
    /// <summary>执行中</summary>
    Running,

    /// <summary>执行完成</summary>
    Completed,

    /// <summary>执行失败</summary>
    Failed
}

/// <summary>
/// 工作流节点失败时的错误处理策略
/// </summary>
public enum NodeErrorPolicy
{
    /// <summary>
    /// 节点失败时立即中止整个工作流（默认行为）
    /// </summary>
    Fail,

    /// <summary>
    /// 节点失败时跳过该节点，继续执行后续就绪节点（节点输出为空字符串）
    /// </summary>
    Skip,

    /// <summary>
    /// 节点失败时以错误文本作为输出继续执行，不中止工作流
    /// </summary>
    Continue
}

/// <summary>
/// 工作流节点类型常量
/// </summary>
public static class WorkflowNodeTypes
{
    public const string Agent = "agent";
    public const string Review = "review";
    public const string Approval = "approval";
    public const string Router = "router";
    public const string Synthesize = "synthesize";
    public const string Debate = "debate";
    public const string Conditional = "conditional";
    public const string Parallel = "parallel";
    public const string Transform = "transform";
}

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

/// <summary>
/// 子 Agent 流式事件类型常量 (AgentAsTools 模式)
/// </summary>
public static class SubAgentEventTypes
{
    public const string Started = "sub_agent_started";
    public const string Completed = "sub_agent_completed";
    public const string Failed = "sub_agent_failed";
    public const string TimedOut = "sub_agent_timed_out";
}

/// <summary>
/// 工作流流式事件类型常量
/// </summary>
public static class WorkflowStreamEventTypes
{
    public const string Step = "step";
    public const string Completed = "completed";
    public const string Error = "error";
}
