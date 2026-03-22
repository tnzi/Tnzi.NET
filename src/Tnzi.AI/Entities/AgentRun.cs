namespace Tnzi.AI.Entities;

/// <summary>
/// AI 运行实例 — 复杂运行的一等对象。
/// 不再只是一次 RunAsync 调用，而是可追踪、可恢复、可审计的执行实例。
/// </summary>
public class AgentRun : MultiTenantAuditedEntity<Guid>
{
    /// <summary>触发运行的 Agent</summary>
    public Guid? AgentId { get; set; }

    /// <summary>关联的对话线程</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>关联的工作流定义（工作流运行时）</summary>
    public Guid? WorkflowDefinitionId { get; set; }

    /// <summary>关联的工作流执行实例 ID（工作流运行时）</summary>
    public string? WorkflowExecutionId { get; set; }

    /// <summary>运行状态</summary>
    public AgentRunStatus Status { get; set; }

    /// <summary>执行模式</summary>
    public AgentExecutionMode ExecutionMode { get; set; }

    /// <summary>原始用户输入（摘要）</summary>
    public string InputSummary { get; set; } = string.Empty;

    /// <summary>最终输出（摘要）</summary>
    public string? OutputSummary { get; set; }

    /// <summary>总输入 Token 用量</summary>
    public int TotalInputTokens { get; set; }

    /// <summary>总输出 Token 用量</summary>
    public int TotalOutputTokens { get; set; }

    /// <summary>总耗时（毫秒）</summary>
    public long DurationMs { get; set; }

    /// <summary>错误信息（失败时）</summary>
    public string? Error { get; set; }

    /// <summary>运行节点集合</summary>
    public ICollection<AgentRunNode> Nodes { get; set; } = [];
}
