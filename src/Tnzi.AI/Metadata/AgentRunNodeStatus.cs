namespace Tnzi.AI.Metadata;

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
