namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent 记忆管理服务 - 管理端为某个 Agent 预置/管理其长期记忆（agent-bound scope）。
/// </summary>
/// <remarks>
/// 记忆以结构化 <c>AgentId</c> 列 + agent-bound scope（<c>agent-bound:{agentId}:default</c>）写入，
/// 与运行时 <c>MemoryContributor</c> 的 <c>MemoryScope.ForAgent(agentId)</c> 加载路径对齐 ——
/// 因此管理端预置的记忆在该 Agent 任意一次运行时都会被注入（headless-safe）。
/// </remarks>
public interface IAgentMemoryService
{
    /// <summary>分页查询某 Agent 的记忆条目</summary>
    Task<Result<IPagedList<AgentMemoryDto>>> GetListAsync(Guid agentId, AgentMemoryListQueryDto query, CancellationToken ct = default);

    /// <summary>为某 Agent 创建一条记忆</summary>
    Task<Result<AgentMemoryDto>> CreateAsync(Guid agentId, CreateAgentMemoryDto dto, CancellationToken ct = default);

    /// <summary>更新某 Agent 的一条记忆</summary>
    Task<Result<AgentMemoryDto>> UpdateAsync(Guid agentId, Guid memoryId, UpdateAgentMemoryDto dto, CancellationToken ct = default);

    /// <summary>删除某 Agent 的一条记忆</summary>
    Task<Result> DeleteAsync(Guid agentId, Guid memoryId, CancellationToken ct = default);
}
