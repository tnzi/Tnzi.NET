namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Trace 持久化存储接口
/// </summary>
public interface ITraceStore
{
    /// <summary>记录 Trace 条目</summary>
    Task<AgentRunTrace> AddAsync(AgentRunTrace trace, CancellationToken cancellationToken = default);

    /// <summary>按 Run 查询 Trace 列表</summary>
    Task<List<AgentRunTrace>> GetByRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>按节点查询 Trace 列表</summary>
    Task<List<AgentRunTrace>> GetByNodeAsync(Guid nodeId, CancellationToken cancellationToken = default);
}
