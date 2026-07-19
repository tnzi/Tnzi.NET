namespace Tnzi.AI.Events.Handlers;

/// <summary>
/// Thread 删除级联清理处理器：删除相关的消息、运行记录、产物等关联数据。
/// </summary>
/// <remarks>
/// 清理顺序：
/// 1. AgentThreadMessage（消息记录）
/// 2. AgentRunTrace + AgentRunNode + AgentRun（运行记录及追踪）
/// 3. AgentArtifact（产物记录）
/// 4. UsageLog（使用日志）
/// 各仓储均为可选注入，缺失时对应步骤跳过（软依赖）。
/// 级联删除是持久化副作用，本处理器不吞异常：任何一步失败均让异常冒泡给事件总线
/// （LocalEventBus 已统一做错误隔离、LogError、重试与死信队列）。各删除按 ThreadId
/// 谓词幂等（重删已删行=0 行受影响），总线重试整个处理器安全。此前每步包 log-only
/// try/catch，会架空总线的重试与 DLQ。
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
        if (_messageRepository != null)
        {
            await _messageRepository.AsQueryable()
                .Where(m => m.ThreadId == threadId)
                .ExecuteDeleteAsync(ct);
        }

        // 2. 删除运行追踪、节点和运行记录（先删子表再删主表）
        if (_runRepository != null)
        {
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
        }

        // 3. 删除产物
        if (_artifactRepository != null)
        {
            await _artifactRepository.AsQueryable()
                .Where(a => a.ThreadId == threadId)
                .ExecuteDeleteAsync(ct);
        }

        // 4. 删除使用日志
        if (_usageLogRepository != null)
        {
            await _usageLogRepository.AsQueryable()
                .Where(log => log.ThreadId == threadId)
                .ExecuteDeleteAsync(ct);
        }

        _logger.LogInformation("Cascade cleanup completed for thread {ThreadId}", evt.ThreadId);
    }
}
