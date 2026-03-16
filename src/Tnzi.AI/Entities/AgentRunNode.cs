namespace Tnzi.AI.Entities;

/// <summary>
/// 运行图中的一个节点实例。
/// 记录每个执行步骤的输入/输出/状态。
/// </summary>
public class AgentRunNode : CreationAuditedEntity<Guid>
{
    /// <summary>关联的 Run ID</summary>
    public Guid RunId { get; set; }

    /// <summary>节点类型（agent/review/approval/router/synthesize/...）</summary>
    public string NodeType { get; set; } = string.Empty;

    /// <summary>节点名称（Workflow 步骤名）</summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>执行的 Agent ID（如果是 agent 节点）</summary>
    public Guid? AgentId { get; set; }

    /// <summary>节点状态</summary>
    public AgentRunNodeStatus Status { get; set; }

    /// <summary>输入摘要</summary>
    public string? InputSummary { get; set; }

    /// <summary>输出内容</summary>
    public string? Output { get; set; }

    /// <summary>输入 Token 用量</summary>
    public int InputTokens { get; set; }

    /// <summary>输出 Token 用量</summary>
    public int OutputTokens { get; set; }

    /// <summary>耗时（毫秒）</summary>
    public long DurationMs { get; set; }

    /// <summary>错误信息</summary>
    public string? Error { get; set; }

    /// <summary>重试次数</summary>
    public int RetryCount { get; set; }

    /// <summary>执行顺序</summary>
    public int OrderIndex { get; set; }

    /// <summary>关联的 Run</summary>
    public AgentRun Run { get; set; } = null!;
}
