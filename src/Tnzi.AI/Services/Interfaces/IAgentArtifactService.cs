namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent 产出物服务接口
/// </summary>
public interface IAgentArtifactService
{
    Task<Result<AgentArtifactDto>> CreateAsync(Guid runId, Guid threadId, string virtualPath, string fileName, string? contentType = null, long? size = null, CancellationToken ct = default);
    Task<Result<List<AgentArtifactDto>>> GetByThreadAsync(Guid threadId, CancellationToken ct = default);
    /// <summary>
    /// 按运行 ID 查询产出物，同时校验线程所有者身份（ownerUserId = 线程创建者）。
    /// RunId 为 Guid.Empty 时跳过 RunId 过滤，仅按所有者过滤。
    /// </summary>
    Task<Result<List<AgentArtifactDto>>> GetByRunAsync(Guid runId, Guid ownerUserId, CancellationToken ct = default);

    Task<Result<AgentArtifactDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
}
