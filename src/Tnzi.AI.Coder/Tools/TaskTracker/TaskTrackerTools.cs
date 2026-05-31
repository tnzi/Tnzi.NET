namespace Tnzi.AI.Coder.TaskTracker;

/// <summary>
/// 任务追踪工具组 — 会话内多步骤任务管理（内存级）
/// </summary>
[AIToolGroup("task_tracker", "Task Tracker", "Manage multi-step tasks during a coding session")]
public class TaskTrackerTools : IAIToolProvider
{
    private readonly ConcurrentDictionary<string, TaskItem> _tasks = new();
    private int _nextId;
    private readonly ILogger<TaskTrackerTools> _logger;

    public TaskTrackerTools(ILogger<TaskTrackerTools> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 创建新任务
    /// </summary>
    [AIFunction("create_task", "Create a new task with title, description, and priority", IsConcurrencySafe = true)]
    public Task<object> CreateTaskAsync(
        [AIParameter("title", "Task title")] string title,
        [AIParameter("description", "Task description", false)] string? description = null,
        [AIParameter("priority", "Priority: high, medium, low", false)] string priority = "medium")
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Task.FromResult<object>(new { error = "Title cannot be empty" });
        }

        var normalizedPriority = NormalizePriority(priority);
        if (normalizedPriority == null)
        {
            return Task.FromResult<object>(new { error = $"Invalid priority '{priority}'. Must be: high, medium, low" });
        }

        var id = $"T{Interlocked.Increment(ref _nextId)}";
        var now = DateTime.UtcNow;

        var task = new TaskItem
        {
            Id = id,
            Title = title,
            Description = description,
            Priority = normalizedPriority,
            Status = TaskItemStatus.Pending,
            Notes = [],
            CreatedAt = now,
            UpdatedAt = now
        };

        _tasks[id] = task;

        _logger.LogDebug("Created task {TaskId}: {Title} (priority={Priority})", id, title, normalizedPriority);

        return Task.FromResult<object>(new
        {
            success = true,
            task = FormatTask(task),
            message = $"Task {id} created successfully"
        });
    }

    /// <summary>
    /// 更新任务状态或追加备注
    /// </summary>
    [AIFunction("update_task", "Update task status or add notes", IsConcurrencySafe = true)]
    public Task<object> UpdateTaskAsync(
        [AIParameter("id", "Task ID (e.g. T1)")] string id,
        [AIParameter("status", "New status: pending, in_progress, completed, blocked", false)] string? status = null,
        [AIParameter("note", "Note to append to the task", false)] string? note = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<object>(new { error = "Task ID cannot be empty" });
        }

        if (!_tasks.TryGetValue(id, out var task))
        {
            return Task.FromResult<object>(new { error = $"Task '{id}' not found" });
        }

        if (status == null && note == null)
        {
            return Task.FromResult<object>(new { error = "Must provide at least 'status' or 'note' to update" });
        }

        // 不可变更新：创建新实例
        var updatedNotes = new List<string>(task.Notes);
        var updatedStatus = task.Status;

        if (status != null)
        {
            var normalizedStatus = NormalizeStatus(status);
            if (normalizedStatus == null)
            {
                return Task.FromResult<object>(new { error = $"Invalid status '{status}'. Must be: pending, in_progress, completed, blocked" });
            }
            updatedStatus = normalizedStatus;
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            updatedNotes.Add(note);
        }

        var updated = task with
        {
            Status = updatedStatus,
            Notes = updatedNotes,
            UpdatedAt = DateTime.UtcNow
        };

        _tasks[id] = updated;

        _logger.LogDebug("Updated task {TaskId}: status={Status}", id, updated.Status);

        return Task.FromResult<object>(new
        {
            success = true,
            task = FormatTask(updated),
            message = $"Task {id} updated successfully"
        });
    }

    /// <summary>
    /// 列出所有任务，可按状态筛选
    /// </summary>
    [AIFunction("list_tasks", "List all tasks, optionally filtered by status", IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<object> ListTasksAsync(
        [AIParameter("status", "Filter by status: pending, in_progress, completed, blocked", false)] string? status = null)
    {
        IEnumerable<TaskItem> tasks = _tasks.Values;

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = NormalizeStatus(status);
            if (normalizedStatus == null)
            {
                return Task.FromResult<object>(new { error = $"Invalid status '{status}'. Must be: pending, in_progress, completed, blocked" });
            }
            tasks = tasks.Where(t => t.Status == normalizedStatus);
        }

        var taskList = tasks
            .OrderBy(t => t.CreatedAt)
            .Select(FormatTask)
            .ToList();

        return Task.FromResult<object>(new
        {
            tasks = taskList,
            total = _tasks.Count,
            filtered = taskList.Count
        });
    }

    /// <summary>
    /// 获取单个任务详情
    /// </summary>
    [AIFunction("get_task", "Get task details by ID", IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<object> GetTaskAsync(
        [AIParameter("id", "Task ID (e.g. T1)")] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<object>(new { error = "Task ID cannot be empty" });
        }

        if (!_tasks.TryGetValue(id, out var task))
        {
            return Task.FromResult<object>(new { error = $"Task '{id}' not found" });
        }

        return Task.FromResult<object>(new
        {
            task = FormatTask(task)
        });
    }

    /// <summary>
    /// 删除任务
    /// </summary>
    [AIFunction("delete_task", "Remove a task by ID", IsConcurrencySafe = true)]
    public Task<object> DeleteTaskAsync(
        [AIParameter("id", "Task ID (e.g. T1)")] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<object>(new { error = "Task ID cannot be empty" });
        }

        if (!_tasks.TryRemove(id, out var removed))
        {
            return Task.FromResult<object>(new { error = $"Task '{id}' not found" });
        }

        _logger.LogDebug("Deleted task {TaskId}: {Title}", id, removed.Title);

        return Task.FromResult<object>(new
        {
            success = true,
            deleted = FormatTask(removed),
            message = $"Task {id} deleted successfully"
        });
    }

    /// <summary>
    /// 格式化任务为 JSON 友好的匿名对象
    /// </summary>
    private static object FormatTask(TaskItem task) => new
    {
        id = task.Id,
        title = task.Title,
        description = task.Description,
        priority = task.Priority,
        status = task.Status,
        notes = task.Notes,
        created_at = task.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
        updated_at = task.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
    };

    /// <summary>
    /// 规范化优先级字符串
    /// </summary>
    private static string? NormalizePriority(string priority)
    {
        return priority.Trim().ToLower() switch
        {
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => null
        };
    }

    /// <summary>
    /// 规范化状态字符串
    /// </summary>
    private static string? NormalizeStatus(string status)
    {
        return status.Trim().ToLower() switch
        {
            "pending" => TaskItemStatus.Pending,
            "in_progress" or "in-progress" or "inprogress" => TaskItemStatus.InProgress,
            "completed" or "done" => TaskItemStatus.Completed,
            "blocked" => TaskItemStatus.Blocked,
            _ => null
        };
    }
}

/// <summary>
/// 任务项数据结构（会话级内存存储）
/// </summary>
internal sealed record TaskItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Priority { get; init; }
    public required string Status { get; init; }
    public required List<string> Notes { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>
/// 任务状态常量
/// </summary>
internal static class TaskItemStatus
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
}
