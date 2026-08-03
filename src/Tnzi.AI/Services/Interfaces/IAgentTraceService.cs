namespace Tnzi.AI.Services;

/// <summary>
/// Agent 运行轨迹查询服务
/// </summary>
public interface IAgentTraceService
{
    /// <summary>按 Run 查询轨迹列表</summary>
    Task<Result<List<AgentRunTraceDto>>> GetByRunAsync(Guid runId);

    /// <summary>按 Node 查询轨迹列表</summary>
    Task<Result<List<AgentRunTraceDto>>> GetByNodeAsync(Guid runId, Guid nodeId);
}
