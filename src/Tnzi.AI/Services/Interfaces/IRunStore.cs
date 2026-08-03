namespace Tnzi.AI.Services;

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

    /// <summary>按条件分页查询 Run 列表</summary>
    Task<List<AgentRun>> ListAsync(AgentRunStatus? status, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>统计指定根 Run 下的后代数量（不含根自身）</summary>
    Task<int> CountDescendantsAsync(Guid rootRunId, CancellationToken cancellationToken = default);

    /// <summary>获取指定 Run 的父 Run ID（仅 Id + ParentRunId 字段）</summary>
    Task<Guid?> GetParentRunIdAsync(Guid runId, CancellationToken cancellationToken = default);
}
