namespace Tnzi.AI.Services;

/// <summary>
/// 子 Agent / 后台 AgentRun 启动服务
/// </summary>
public interface ISubAgentExecutionService
{
    Task<Result<AgentRunControlStateDto>> SpawnAsync(SpawnAgentRunInput input, CancellationToken cancellationToken = default);
}
