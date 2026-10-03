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
        Check.NotNull(todos);

        // 获取该 RunId 的已有任务。同一序号出现多行（并发写入留下的）只认第一行，其余随下面的删除一起清掉。
        var existing = await _repository.ToListAsync(e => e.RunId == runId, cancellationToken);
        var existingByOrder = existing
            .GroupBy(e => e.OrderIndex)
            .ToDictionary(g => g.Key, g => g.First());

        // write_todos 给的是完整列表：同一序号给了多次以最后一次为准。
        var incoming = todos
            .GroupBy(t => t.Order)
            .Select(g => g.Last())
            .ToList();
        var incomingOrders = incoming.Select(t => t.Order).ToHashSet();

        var toInsert = new List<AgentTask>();
        var modifiedTasks = new List<AgentTask>();
        var toDelete = existing
            .Where(e => !incomingOrders.Contains(e.OrderIndex) || !ReferenceEquals(existingByOrder[e.OrderIndex], e))
            .ToList();

        foreach (var todo in incoming)
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

        if (toDelete.Count > 0)
        {
            await _repository.DeleteManyAsync(toDelete, cancellationToken);
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
