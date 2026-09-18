namespace Tnzi.AI.Services;

/// <summary>
/// Agent runtime 控制服务
/// </summary>
/// <remarks>
/// 读 / 等 / 送输入 / 终止 / 列表都带 <see cref="AgentRunAccessScope"/>，默认
/// <see cref="AgentRunAccessScope.Caller"/>（只看得见自己起的运行，别人的一律 404）；
/// 管理端显式传 <see cref="AgentRunAccessScope.Tenant"/>。<c>task</c> 工具组用默认档。
/// </remarks>
public interface IAgentRuntimeControlService
{
    /// <summary>启动后台 AgentRun（归属人 = 输入的 UserId，无则当前运行请求的用户，再无则环境用户）</summary>
    Task<Result<AgentRunControlStateDto>> SpawnAsync(SpawnAgentRunInput input, CancellationToken cancellationToken = default);

    /// <summary>获取运行时状态</summary>
    Task<Result<AgentRunControlStateDto>> GetStateAsync(Guid runId, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default);

    /// <summary>等待运行进入可观察终态</summary>
    Task<Result<AgentRunWaitResultDto>> WaitAsync(Guid runId, WaitAgentRunInput? input = null, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default);

    /// <summary>向运行发送额外输入并触发恢复</summary>
    Task<Result<AgentRunControlStateDto>> SendInputAsync(Guid runId, SendAgentRunInput input, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default);

    /// <summary>终止运行</summary>
    Task<Result> KillAsync(Guid runId, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default);

    /// <summary>列出已注册的子 Agent 类型</summary>
    Task<Result<List<SubAgentTypeDto>>> ListSubAgentTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>列出 AgentRun 记录（按创建时间倒序）</summary>
    Task<Result<List<AgentRunListItemDto>>> ListRunsAsync(int maxResults = 20, AgentRunStatus? status = null, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default);
}
