namespace Tnzi.AI.Entities;

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
    Cancelled
}
