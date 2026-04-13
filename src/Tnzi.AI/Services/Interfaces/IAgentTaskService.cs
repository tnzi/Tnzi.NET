namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent 任务持久化服务接口 — 将 TodoTools 的瞬态任务同步到数据库
/// </summary>
public interface IAgentTaskService
{
    /// <summary>
    /// 从 TodoItemDto 列表同步到持久化 AgentTask（按 OrderIndex 匹配：新增或更新）
    /// </summary>
    Task SyncFromTodosAsync(Guid runId, List<TodoItemDto> todos, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 RunId 获取任务列表（按 OrderIndex 排序）
    /// </summary>
    Task<Result<List<AgentTaskDto>>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按状态获取任务列表（按 CreationTime 排序）
    /// </summary>
    Task<Result<List<AgentTaskDto>>> GetByStatusAsync(AgentTaskStatus status, CancellationToken cancellationToken = default);
}
