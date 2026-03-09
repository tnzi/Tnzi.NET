namespace Tnzi.AI.Domain;

/// <summary>
/// 执行轨迹条目 — 细粒度日志。
/// 记录 LLM 调用、工具调用、路由决策等低级别事件。
/// </summary>
public class AgentRunTrace : CreationAuditedEntity<Guid>
{
    /// <summary>关联的 Run ID</summary>
    public Guid RunId { get; set; }

    /// <summary>关联的节点 ID</summary>
    public Guid? NodeId { get; set; }

    /// <summary>事件类型（llm_call, tool_call, handoff, route, review, approval, error）</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>事件数据（JSON）</summary>
    public string? EventData { get; set; }

    /// <summary>耗时（毫秒）</summary>
    public long DurationMs { get; set; }

    /// <summary>关联的 Run</summary>
    public AgentRun Run { get; set; } = null!;
}
