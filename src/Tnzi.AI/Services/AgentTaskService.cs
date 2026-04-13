namespace Tnzi.AI.Services;

/// <summary>
/// Agent 任务持久化服务实现
/// </summary>
public class AgentTaskService : ApplicationService, IAgentTaskService
{
    private readonly IRepository<AgentTask, Guid> _repository;

    public AgentTaskService(IServiceProvider serviceProvider, IRepository<AgentTask, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task SyncFromTodosAsync(Guid runId, List<TodoItemDto> todos, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(todos);

        // 获取该 RunId 的已有任务
        var existing = await _repository.ToListAsync(e => e.RunId == runId, cancellationToken);
        var existingByOrder = existing.ToDictionary(e => e.OrderIndex);

        var toInsert = new List<AgentTask>();
        var modifiedTasks = new List<AgentTask>();

        foreach (var todo in todos)
        {
            var status = MapStatus(todo.Status);

            if (existingByOrder.TryGetValue(todo.Order, out var task))
            {
                // 更新已有任务
                task.Title = todo.Content;
                task.Status = status;

                // 状态变为 Completed 且之前未完成时，记录完成时间
                if (status == AgentTaskStatus.Completed && task.CompletedAt == null)
                {
                    task.CompletedAt = DateTime.UtcNow;
                }

                modifiedTasks.Add(task);
            }
            else
            {
                // 新增任务
                var newTask = new AgentTask
                {
                    RunId = runId,
                    Title = todo.Content,
                    Status = status,
                    OrderIndex = todo.Order,
                    CompletedAt = status == AgentTaskStatus.Completed ? DateTime.UtcNow : null
                };
                toInsert.Add(newTask);
            }
        }

        if (modifiedTasks.Count > 0)
        {
            await _repository.UpdateManyAsync(modifiedTasks, cancellationToken);
        }

        if (toInsert.Count > 0)
        {
            await _repository.InsertManyAsync(toInsert, cancellationToken);
        }
    }

    public async Task<Result<List<AgentTaskDto>>> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var tasks = await _repository.ToListAsync(e => e.RunId == runId, cancellationToken);
        return Ok(tasks.OrderBy(t => t.OrderIndex).MapToList<AgentTaskDto>());
    }

    public async Task<Result<List<AgentTaskDto>>> GetByStatusAsync(AgentTaskStatus status, CancellationToken cancellationToken = default)
    {
        var tasks = await _repository.ToListAsync(e => e.Status == status, cancellationToken);
        return Ok(tasks.OrderBy(t => t.CreationTime).MapToList<AgentTaskDto>());
    }

    /// <summary>
    /// 映射 TodoStatus → AgentTaskStatus（值相同，枚举不同）
    /// </summary>
    private static AgentTaskStatus MapStatus(TodoStatus status) => status switch
    {
        TodoStatus.Pending => AgentTaskStatus.Pending,
        TodoStatus.InProgress => AgentTaskStatus.InProgress,
        TodoStatus.Completed => AgentTaskStatus.Completed,
        TodoStatus.Skipped => AgentTaskStatus.Skipped,
        _ => AgentTaskStatus.Pending
    };
}
