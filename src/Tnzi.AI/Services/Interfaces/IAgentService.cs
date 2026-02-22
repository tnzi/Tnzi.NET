namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent 管理服务接口
/// </summary>
public interface IAgentService
{
    /// <summary>
    /// 创建 Agent
    /// </summary>
    Task<Result<AgentDto>> CreateAsync(CreateAgentDto input);

    /// <summary>
    /// 更新 Agent
    /// </summary>
    Task<Result<AgentDto>> UpdateAsync(Guid id, UpdateAgentDto input);

    /// <summary>
    /// 删除 Agent
    /// </summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// 根据 ID 获取 Agent
    /// </summary>
    Task<Result<AgentDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// 获取 Agent 列表
    /// </summary>
    Task<Result<IPagedList<AgentDto>>> GetListAsync(AgentListQueryDto query);

    /// <summary>
    /// 运行 Agent
    /// </summary>
    Task<Result<AgentResponseDto>> RunAsync(Guid agentId, string? message, List<ContentPartDto>? content = null, Guid? threadId = null, Guid? userId = null, CancellationToken ct = default);

    /// <summary>
    /// 流式运行 Agent（delta 模型 — 每个事件只包含增量内容）
    /// </summary>
    IAsyncEnumerable<StreamEvent> RunStreamingAsync(Guid agentId, string? message, List<ContentPartDto>? content = null, Guid? threadId = null, Guid? userId = null, CancellationToken ct = default);
}
