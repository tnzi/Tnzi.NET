namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Run 持久化存储接口
/// </summary>
public interface IRunStore
{
    /// <summary>创建 Run 记录</summary>
    Task<AgentRun> CreateAsync(AgentRun run, CancellationToken cancellationToken = default);

    /// <summary>更新 Run 状态</summary>
    Task UpdateAsync(AgentRun run, CancellationToken cancellationToken = default);

    /// <summary>获取 Run</summary>
    Task<AgentRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>获取 Run（含节点）</summary>
    Task<AgentRun?> GetWithNodesAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>添加节点记录</summary>
    Task<AgentRunNode> AddNodeAsync(AgentRunNode node, CancellationToken cancellationToken = default);

    /// <summary>更新节点状态</summary>
    Task UpdateNodeAsync(AgentRunNode node, CancellationToken cancellationToken = default);
}
