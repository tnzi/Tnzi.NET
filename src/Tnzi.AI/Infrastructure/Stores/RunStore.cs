namespace Tnzi.AI.Infrastructure.Stores;

/// <summary>
/// Run 持久化存储 — 基于 EF Core Repository 实现
/// </summary>
public class RunStore : IRunStore
{
    private readonly IRepository<AgentRun, Guid> _runRepository;
    private readonly IRepository<AgentRunNode, Guid> _nodeRepository;

    public RunStore(
        IRepository<AgentRun, Guid> runRepository,
        IRepository<AgentRunNode, Guid> nodeRepository)
    {
        _runRepository = Check.NotNull(runRepository);
        _nodeRepository = Check.NotNull(nodeRepository);
    }

    /// <summary>创建 Run 记录</summary>
    public async Task<AgentRun> CreateAsync(AgentRun run, CancellationToken cancellationToken = default)
    {
        Check.NotNull(run);
        await _runRepository.InsertAsync(run, cancellationToken);
        return run;
    }

    /// <summary>更新 Run 状态</summary>
    public async Task UpdateAsync(AgentRun run, CancellationToken cancellationToken = default)
    {
        Check.NotNull(run);
        await _runRepository.UpdateAsync(run, cancellationToken);
    }

    /// <summary>获取 Run</summary>
    public async Task<AgentRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await _runRepository.GetAsync(runId, cancellationToken);
    }

    /// <summary>获取 Run（含节点）</summary>
    public async Task<AgentRun?> GetWithNodesAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await _runRepository.AsQueryable()
            .Include(r => r.Nodes.OrderBy(n => n.OrderIndex))
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
    }

    /// <summary>添加节点记录</summary>
    public async Task<AgentRunNode> AddNodeAsync(AgentRunNode node, CancellationToken cancellationToken = default)
    {
        Check.NotNull(node);
        await _nodeRepository.InsertAsync(node, cancellationToken);
        return node;
    }

    /// <summary>更新节点状态</summary>
    public async Task UpdateNodeAsync(AgentRunNode node, CancellationToken cancellationToken = default)
    {
        Check.NotNull(node);
        await _nodeRepository.UpdateAsync(node, cancellationToken);
    }
}
