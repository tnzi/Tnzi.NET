namespace Tnzi.AI.Events.Handlers;

/// <summary>
/// Thread 删除级联清理处理器：删除相关的消息、运行记录、产物等关联数据。
/// </summary>
/// <remarks>
/// 清理顺序：
/// 1. AgentThreadMessage（消息记录）
/// 2. AgentRunTrace + AgentRunNode + AgentTask + AgentRun（运行记录、追踪与 write_todos 留下的任务）。
///    AgentTask 只有裸 RunId 列、没有外键，数据库不会级联；不在这里删，任务标题（取自对话内容）会在
///    线程删除后留在管理端任务列表里，RunId 指向已不存在的运行
/// 3. AgentArtifact（产物记录）与 AgentAttachment（用户随消息发的图片 / PDF、工具返回的图片，连同字节）
/// 4. UsageLog（使用日志）：只解除 ThreadId 引用，账目行保留（预算聚合与用量分析的事实来源）
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
    private readonly IRepository<AgentTask, Guid>? _taskRepository;
    private readonly IRepository<AgentArtifact, Guid>? _artifactRepository;
    private readonly IRepository<UsageLog, Guid>? _usageLogRepository;
    private readonly IRepository<AgentAttachment, Guid>? _attachmentRepository;
    private readonly ILogger<ThreadCleanupHandler> _logger;

    public ThreadCleanupHandler(
        ILogger<ThreadCleanupHandler> logger,
        IRepository<AgentThreadMessage, Guid>? messageRepository = null,
        IRepository<AgentRun, Guid>? runRepository = null,
        IRepository<AgentRunTrace, Guid>? traceRepository = null,
        IRepository<AgentRunNode, Guid>? nodeRepository = null,
        IRepository<AgentArtifact, Guid>? artifactRepository = null,
        IRepository<UsageLog, Guid>? usageLogRepository = null,
        IRepository<AgentTask, Guid>? taskRepository = null,
        IRepository<AgentAttachment, Guid>? attachmentRepository = null)
    {
        _logger = Check.NotNull(logger);
        _messageRepository = messageRepository;
        _runRepository = runRepository;
        _traceRepository = traceRepository;
        _nodeRepository = nodeRepository;
        _taskRepository = taskRepository;
        _artifactRepository = artifactRepository;
        _usageLogRepository = usageLogRepository;
        _attachmentRepository = attachmentRepository;
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

                if (_taskRepository != null)
                {
                    await _taskRepository.AsQueryable()
                        .Where(t => runIds.Contains(t.RunId))
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

        // 附件带着字节：用户删掉对话，他发过的图片与文档、工具从他的机器上取回的截图不能留在库里
        if (_attachmentRepository != null)
        {
            await _attachmentRepository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(a => a.ThreadId == threadId)
                .ExecuteDeleteAsync(ct);
        }

        // 4. 解除使用日志对线程的引用，账目保留。
        //    UsageLog 是租户 / Agent 预算聚合与用量分析的事实来源（按 CreationTime 聚合 EstimatedCostUsd），
        //    线程本身只是软删；随线程硬删账目 = 用户删掉自己的线程就让本期花费缩水、预算门重新放行、
        //    成本报表出现「负增长」且无法追溯。这里只把 ThreadId 置空，token / cost / provider /
        //    model / agent / tenant / 时间一律不动。
        if (_usageLogRepository != null)
        {
            await _usageLogRepository.AsQueryable()
                .Where(log => log.ThreadId == threadId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(log => log.ThreadId, (Guid?)null), ct);
        }

        _logger.LogInformation("Cascade cleanup completed for thread {ThreadId}", evt.ThreadId);
    }
}
