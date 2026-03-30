namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent 产出物服务接口
/// </summary>
public interface IAgentArtifactService
{
    Task<Result<AgentArtifactDto>> CreateAsync(Guid runId, Guid threadId, string virtualPath, string fileName, string? contentType = null, long? size = null, CancellationToken ct = default);
    Task<Result<List<AgentArtifactDto>>> GetByThreadAsync(Guid threadId, CancellationToken ct = default);
    Task<Result<List<AgentArtifactDto>>> GetByRunAsync(Guid runId, CancellationToken ct = default);
    Task<Result<AgentArtifactDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
}
