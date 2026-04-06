namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent runtime 控制服务
/// </summary>
public interface IAgentRuntimeControlService
{
    /// <summary>启动后台 AgentRun</summary>
    Task<Result<AgentRunControlStateDto>> SpawnAsync(SpawnAgentRunInput input, CancellationToken cancellationToken = default);

    /// <summary>获取运行时状态</summary>
    Task<Result<AgentRunControlStateDto>> GetStateAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>等待运行进入可观察终态</summary>
    Task<Result<AgentRunWaitResultDto>> WaitAsync(Guid runId, WaitAgentRunInput? input = null, CancellationToken cancellationToken = default);

    /// <summary>向运行发送额外输入并触发恢复</summary>
    Task<Result<AgentRunControlStateDto>> SendInputAsync(Guid runId, SendAgentRunInput input, CancellationToken cancellationToken = default);

    /// <summary>终止运行</summary>
    Task<Result> KillAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>列出已注册的子 Agent 类型</summary>
    Task<Result<List<SubAgentTypeDto>>> ListSubAgentTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>列出 AgentRun 记录（按创建时间倒序）</summary>
    Task<Result<List<AgentRunListItemDto>>> ListRunsAsync(int maxResults = 20, AgentRunStatus? status = null, CancellationToken cancellationToken = default);
}
