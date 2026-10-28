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

    /// <inheritdoc />
    /// <remarks>标量投影不经过变更跟踪器的身份解析，读到的是库里的值，而不是本作用域里那份已跟踪实体的内存值。</remarks>
    public async Task<AgentRunStatus?> GetStatusAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return await _runRepository.AsQueryable()
            .Where(r => r.Id == runId)
            .Select(r => (AgentRunStatus?)r.Status)
            .FirstOrDefaultAsync(cancellationToken);
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

    /// <inheritdoc />
    public async Task<List<AgentRun>> ListByOwnerAsync(Guid? ownerUserId, AgentRunStatus? status, int maxResults, CancellationToken cancellationToken = default)
    {
        var query = _runRepository.AsQueryable().Where(r => r.CreatorId == ownerUserId);

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

    /// <inheritdoc />
    public async Task<int> CountActiveDescendantsAsync(Guid rootRunId, CancellationToken cancellationToken = default)
    {
        return await _runRepository.AsQueryable()
            .CountAsync(r => r.RootRunId == rootRunId && r.Id != rootRunId
                             && (r.Status == AgentRunStatus.Pending || r.Status == AgentRunStatus.Running),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountActiveRootRunsByOwnerAsync(Guid? ownerUserId, CancellationToken cancellationToken = default)
    {
        return await _runRepository.AsQueryable()
            .CountAsync(r => r.CreatorId == ownerUserId && r.ParentRunId == null
                             && (r.Status == AgentRunStatus.Pending || r.Status == AgentRunStatus.Running),
                cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>一条条件更新：与运行自己的收尾、孤儿回收赛跑时，谁先写下终态以谁为准。</remarks>
    public async Task<bool> CloseIfStillActiveAsync(Guid runId, AgentRunStatus status, string error, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var truncated = StringTruncator.Truncate(error, 1000);
        var updated = await _runRepository.AsQueryable()
            .Where(r => r.Id == runId && (r.Status == AgentRunStatus.Pending || r.Status == AgentRunStatus.Running))
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.Error, truncated)
                .SetProperty(r => r.LastHeartbeatAt, now)
                .SetProperty(r => r.LastModificationTime, now), cancellationToken);
        return updated > 0;
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
