namespace Tnzi.AI.Services;

/// <summary>
/// Agent 运行轨迹查询服务实现
/// </summary>
public class AgentTraceService : ApplicationService, IAgentTraceService
{
    private readonly IRepository<AgentRun, Guid> _runRepository;
    private readonly IRepository<AgentRunTrace, Guid> _traceRepository;
    private readonly IRepository<AgentRunNode, Guid> _nodeRepository;

    public AgentTraceService(
        IRepository<AgentRun, Guid> runRepository,
        IRepository<AgentRunTrace, Guid> traceRepository,
        IRepository<AgentRunNode, Guid> nodeRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _runRepository = Check.NotNull(runRepository);
        _traceRepository = Check.NotNull(traceRepository);
        _nodeRepository = Check.NotNull(nodeRepository);
    }

    public async Task<Result<List<AgentRunTraceDto>>> GetByRunAsync(Guid runId)
    {
        var runExists = await _runRepository.AnyAsync(r => r.Id == runId);
        if (!runExists)
            return Fail<List<AgentRunTraceDto>>("Run not found", 404, ErrorCodes.RunNotFound);

        var traces = await _traceRepository
            .Where(t => t.RunId == runId)
            .OrderBy(t => t.CreationTime)
            .ProjectTo<AgentRunTrace, AgentRunTraceDto>()
            .ToListAsync();

        return Ok(traces);
    }

    public async Task<Result<List<AgentRunTraceDto>>> GetByNodeAsync(Guid runId, Guid nodeId)
    {
        var nodeExists = await _nodeRepository.AnyAsync(n => n.RunId == runId && n.Id == nodeId);
        if (!nodeExists)
            return Fail<List<AgentRunTraceDto>>("Node not found", 404, ErrorCodes.NodeNotFound);

        var traces = await _traceRepository
            .Where(t => t.RunId == runId && t.NodeId == nodeId)
            .OrderBy(t => t.CreationTime)
            .ProjectTo<AgentRunTrace, AgentRunTraceDto>()
            .ToListAsync();

        return Ok(traces);
    }
}
