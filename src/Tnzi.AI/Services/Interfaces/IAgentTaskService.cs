namespace Tnzi.AI.Services;

/// <summary>
/// Agent 任务持久化服务接口 - 将 TodoTools 的瞬态任务同步到数据库
/// </summary>
public interface IAgentTaskService
{
    /// <summary>
    /// 用<b>完整的</b> TodoItemDto 列表覆盖这次运行的持久化 AgentTask：按 OrderIndex 匹配新增或更新，
    /// 列表里已经没有的删除。空列表 = 这次运行的任务全部删除。
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
