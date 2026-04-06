namespace Tnzi.AI.Events.Handlers;

/// <summary>
/// Thread 删除级联清理处理器 — 删除相关的消息、运行记录、产物等关联数据。
/// </summary>
/// <remarks>
/// 清理顺序：
/// 1. AgentThreadMessage（消息记录）
/// 2. AgentRunTrace + AgentRunNode + AgentRun（运行记录及追踪）
/// 3. AgentArtifact（产物记录）
/// 4. UsageLog（使用日志）
/// 所有清理操作均静默失败，不影响主流程。
/// </remarks>
public class ThreadCleanupHandler : IEventHandler<ThreadDeletedEvent>
{
    private readonly IRepository<AgentThreadMessage, Guid>? _messageRepository;
    private readonly IRepository<AgentRun, Guid>? _runRepository;
    private readonly IRepository<AgentRunTrace, Guid>? _traceRepository;
    private readonly IRepository<AgentRunNode, Guid>? _nodeRepository;
    private readonly IRepository<AgentArtifact, Guid>? _artifactRepository;
    private readonly IRepository<UsageLog, Guid>? _usageLogRepository;
    private readonly ILogger<ThreadCleanupHandler> _logger;

    public ThreadCleanupHandler(
        ILogger<ThreadCleanupHandler> logger,
        IRepository<AgentThreadMessage, Guid>? messageRepository = null,
        IRepository<AgentRun, Guid>? runRepository = null,
        IRepository<AgentRunTrace, Guid>? traceRepository = null,
        IRepository<AgentRunNode, Guid>? nodeRepository = null,
        IRepository<AgentArtifact, Guid>? artifactRepository = null,
        IRepository<UsageLog, Guid>? usageLogRepository = null)
    {
        _logger = Check.NotNull(logger);
        _messageRepository = messageRepository;
        _runRepository = runRepository;
        _traceRepository = traceRepository;
        _nodeRepository = nodeRepository;
        _artifactRepository = artifactRepository;
        _usageLogRepository = usageLogRepository;
    }

    public async Task HandleAsync(ThreadDeletedEvent evt, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting cascade cleanup for thread {ThreadId}", evt.ThreadId);

        var threadId = evt.ThreadId;

        // 1. 删除消息
        await SafeDeleteAsync("messages", async () =>
        {
            if (_messageRepository == null) return;
            await _messageRepository.AsQueryable()
                .Where(m => m.ThreadId == threadId)
                .ExecuteDeleteAsync(ct);
        });

        // 2. 删除运行追踪、节点和运行记录（先删子表再删主表）
        await SafeDeleteAsync("runs and related", async () =>
        {
            if (_runRepository == null) return;

            // 查询一次 runIds，共享给 traces/nodes 清理
            var runIds = await _runRepository.AsQueryable()
                .Where(r => r.ThreadId == threadId)
                .Select(r => r.Id)
                .ToListAsync(ct);

            if (runIds.Count > 0)
            {
                // 先删子表
                if (_traceRepository != null)
                {
                    await _traceRepository.AsQueryable()
                        .Where(t => runIds.Contains(t.RunId))
                        .ExecuteDeleteAsync(ct);
                }

                if (_nodeRepository != null)
                {
                    await _nodeRepository.AsQueryable()
                        .Where(n => runIds.Contains(n.RunId))
                        .ExecuteDeleteAsync(ct);
                }
            }

            // 再删主表
            await _runRepository.AsQueryable()
                .Where(r => r.ThreadId == threadId)
                .ExecuteDeleteAsync(ct);
        });

        // 3. 删除产物
        await SafeDeleteAsync("artifacts", async () =>
        {
            if (_artifactRepository == null) return;
            await _artifactRepository.AsQueryable()
                .Where(a => a.ThreadId == threadId)
                .ExecuteDeleteAsync(ct);
        });

        // 4. 删除使用日志
        await SafeDeleteAsync("usage logs", async () =>
        {
            if (_usageLogRepository == null) return;
            await _usageLogRepository.AsQueryable()
                .Where(log => log.ThreadId == threadId)
                .ExecuteDeleteAsync(ct);
        });

        _logger.LogInformation("Cascade cleanup completed for thread {ThreadId}", evt.ThreadId);
    }

    /// <summary>
    /// 静默执行清理操作，异常仅记录不传播
    /// </summary>
    private async Task SafeDeleteAsync(string resource, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup {Resource} during thread cascade delete", resource);
        }
    }
}
