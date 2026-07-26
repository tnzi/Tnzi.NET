namespace Tnzi.AI.Infrastructure.Stores;

/// <summary>
/// Run 持久化存储 - 基于 EF Core Repository 实现
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

    /// <summary>按条件分页查询 Run 列表</summary>
    public async Task<List<AgentRun>> ListAsync(AgentRunStatus? status, int maxResults, CancellationToken cancellationToken = default)
    {
        var query = _runRepository.AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        return await query
            .OrderByDescending(r => r.CreationTime)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }

    /// <summary>统计指定根 Run 下的后代数量（不含根自身）</summary>
    public async Task<int> CountDescendantsAsync(Guid rootRunId, CancellationToken cancellationToken = default)
    {
        return await _runRepository.AsQueryable()
            .CountAsync(r => r.RootRunId == rootRunId && r.Id != rootRunId, cancellationToken);
    }

    /// <summary>获取指定 Run 的父 Run ID（仅 Id + ParentRunId 字段）</summary>
    public async Task<Guid?> GetParentRunIdAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await _runRepository.AsQueryable()
            .Where(r => r.Id == runId)
            .Select(r => r.ParentRunId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
