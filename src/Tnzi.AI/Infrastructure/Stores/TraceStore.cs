namespace Tnzi.AI.Infrastructure.Stores;

/// <summary>
/// Trace 持久化存储 — 基于 EF Core Repository 实现
/// </summary>
public class TraceStore : ITraceStore
{
    private readonly IRepository<AgentRunTrace, Guid> _traceRepository;

    public TraceStore(IRepository<AgentRunTrace, Guid> traceRepository)
    {
        _traceRepository = Check.NotNull(traceRepository);
    }

    /// <summary>记录 Trace 条目</summary>
    public async Task<AgentRunTrace> AddAsync(AgentRunTrace trace, CancellationToken cancellationToken = default)
    {
        Check.NotNull(trace);
        await _traceRepository.InsertAsync(trace, cancellationToken);
        return trace;
    }

    /// <summary>按 Run 查询 Trace 列表</summary>
    public async Task<List<AgentRunTrace>> GetByRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await _traceRepository.AsQueryable()
            .Where(t => t.RunId == runId)
            .OrderBy(t => t.CreationTime)
            .ToListAsync(cancellationToken);
    }

    /// <summary>按节点查询 Trace 列表</summary>
    public async Task<List<AgentRunTrace>> GetByNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        return await _traceRepository.AsQueryable()
            .Where(t => t.NodeId == nodeId)
            .OrderBy(t => t.CreationTime)
            .ToListAsync(cancellationToken);
    }
}
