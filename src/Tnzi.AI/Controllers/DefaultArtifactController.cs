namespace Tnzi.AI.Controllers;

/// <summary>
/// Agent 产出物控制器 — 列表与查询（仅限当前用户自有产出物）
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("artifacts")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultArtifactController : ApiControllerBase
{
    protected readonly IAgentArtifactService ArtifactService;
    protected readonly IAgentThreadService ThreadService;

    public DefaultArtifactController(
        IAgentArtifactService artifactService,
        IAgentThreadService threadService)
    {
        ArtifactService = Check.NotNull(artifactService);
        ThreadService = Check.NotNull(threadService);
    }

    /// <summary>
    /// 获取线程的所有产出物（限当前用户所有的线程）
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<AgentArtifactDto>>> GetByThread([FromQuery] Guid threadId, CancellationToken ct = default)
    {
        await EnsureThreadOwnershipAsync(threadId);
        var result = await ArtifactService.GetByThreadAsync(threadId, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取运行的所有产出物（限当前用户所有线程中的产出物）
    /// </summary>
    [HttpGet("by-run")]
    public virtual async Task<ApiResult<List<AgentArtifactDto>>> GetByRun([FromQuery] Guid runId, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var result = await ArtifactService.GetByRunAsync(runId, userId, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取单个产出物详情（限当前用户所有线程中的产出物）
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<AgentArtifactDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var artifactResult = await ArtifactService.GetByIdAsync(id, ct);
        if (!artifactResult.Succeeded || artifactResult.Data == null)
            return artifactResult.ToApiResult();

        await EnsureThreadOwnershipAsync(artifactResult.Data.ThreadId);
        return artifactResult.ToApiResult();
    }

    /// <summary>
    /// 获取当前用户 ID，未认证时抛出异常
    /// </summary>
    private Guid GetCurrentUserId() => AiControllerHelpers.RequireUserId(GetRequiredCurrentUser());

    /// <summary>
    /// 验证当前用户是否为线程所有者，不是则抛出 404（不暴露存在性）
    /// </summary>
    private async Task EnsureThreadOwnershipAsync(Guid threadId)
    {
        var userId = GetCurrentUserId();
        if (!await ThreadService.IsOwnerAsync(threadId, userId))
            throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
    }
}
