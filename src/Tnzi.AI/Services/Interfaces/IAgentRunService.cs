namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent 运行管理服务
/// </summary>
public interface IAgentRunService
{
    /// <summary>获取运行统计</summary>
    Task<Result<AgentRunStatsDto>> GetStatsAsync();

    /// <summary>获取运行详情</summary>
    Task<Result<AgentRunDto>> GetByIdAsync(Guid id);

    /// <summary>分页查询运行列表</summary>
    Task<Result<IPagedList<AgentRunDto>>> GetListAsync(AgentRunQueryDto input);

    /// <summary>获取运行的所有节点</summary>
    Task<Result<List<AgentRunNodeDto>>> GetNodesAsync(Guid runId);

    /// <summary>获取指定节点详情</summary>
    Task<Result<AgentRunNodeDto>> GetNodeAsync(Guid runId, Guid nodeId);

    /// <summary>取消运行</summary>
    Task<Result> CancelAsync(Guid runId);

    /// <summary>审批通过（HITL）</summary>
    Task<Result> ApproveAsync(Guid runId, string? comment);

    /// <summary>审批拒绝（HITL）</summary>
    Task<Result> RejectAsync(Guid runId, string? comment);

    /// <summary>重试失败节点</summary>
    Task<Result> RetryNodeAsync(Guid runId, Guid nodeId);
}
