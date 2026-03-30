namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent 人格服务接口
/// </summary>
public interface IAgentPersonaService
{
    Task<Result<AgentPersonaDto>> CreateAsync(CreateAgentPersonaDto input, CancellationToken ct = default);
    Task<Result<AgentPersonaDto>> UpdateAsync(Guid id, UpdateAgentPersonaDto input, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result<AgentPersonaDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<AgentPersonaDto>> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<Result<IPagedList<AgentPersonaDto>>> GetListAsync(AgentPersonaQueryDto query, CancellationToken ct = default);
}
