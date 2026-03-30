namespace Tnzi.AI.Services;

/// <summary>
/// Agent 产出物服务实现
/// </summary>
public class AgentArtifactService : ApplicationService, IAgentArtifactService
{
    private readonly IRepository<AgentArtifact, Guid> _repository;

    public AgentArtifactService(IServiceProvider serviceProvider, IRepository<AgentArtifact, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<Result<AgentArtifactDto>> CreateAsync(Guid runId, Guid threadId, string virtualPath, string fileName, string? contentType = null, long? size = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(virtualPath);
        Check.NotNullOrWhiteSpace(fileName);

        // 去重：同 Thread 同 VirtualPath 则更新
        var existing = await _repository.FirstOrDefaultAsync(
            e => e.ThreadId == threadId && e.VirtualPath == virtualPath, ct);

        if (existing != null)
        {
            existing.RunId = runId;
            existing.FileName = fileName;
            existing.ContentType = contentType;
            existing.Size = size;
            await _repository.UpdateAsync(existing, ct);
            return Ok(existing.MapTo<AgentArtifactDto>());
        }

        var entity = new AgentArtifact
        {
            RunId = runId,
            ThreadId = threadId,
            VirtualPath = virtualPath,
            FileName = fileName,
            ContentType = contentType,
            Size = size
        };

        await _repository.InsertAsync(entity, ct);
        return Ok(entity.MapTo<AgentArtifactDto>());
    }

    public async Task<Result<List<AgentArtifactDto>>> GetByThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        var list = await _repository.ToListAsync(e => e.ThreadId == threadId, ct);
        return Ok(list.MapToList<AgentArtifactDto>());
    }

    public async Task<Result<List<AgentArtifactDto>>> GetByRunAsync(Guid runId, CancellationToken ct = default)
    {
        var list = await _repository.ToListAsync(e => e.RunId == runId, ct);
        return Ok(list.MapToList<AgentArtifactDto>());
    }

    public async Task<Result<AgentArtifactDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.GetAsync(id, ct);
        if (entity == null)
            return Fail<AgentArtifactDto>("Artifact not found.", 404);

        return Ok(entity.MapTo<AgentArtifactDto>());
    }
}
