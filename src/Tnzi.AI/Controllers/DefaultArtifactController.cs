namespace Tnzi.AI.Controllers;

/// <summary>
/// Agent 产出物控制器 — 列表与查询
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("artifacts")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultArtifactController : ApiControllerBase
{
    protected readonly IAgentArtifactService ArtifactService;

    public DefaultArtifactController(IAgentArtifactService artifactService)
    {
        ArtifactService = Check.NotNull(artifactService);
    }

    /// <summary>
    /// 获取线程的所有产出物
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<AgentArtifactDto>>> GetByThread([FromQuery] Guid threadId, CancellationToken ct = default)
    {
        var result = await ArtifactService.GetByThreadAsync(threadId, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取运行的所有产出物
    /// </summary>
    [HttpGet("by-run")]
    public virtual async Task<ApiResult<List<AgentArtifactDto>>> GetByRun([FromQuery] Guid runId, CancellationToken ct = default)
    {
        var result = await ArtifactService.GetByRunAsync(runId, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取单个产出物详情
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<AgentArtifactDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await ArtifactService.GetByIdAsync(id, ct);
        return result.ToApiResult();
    }
}
