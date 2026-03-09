namespace Tnzi.AI.Infrastructure.Stores;

/// <summary>
/// 数据库工作流检查点存储 — 基于 IRepository 持久化工作流执行状态
/// </summary>
[ExperimentalApi(Reason = "Workflow checkpointing is in preview")]
public class DatabaseWorkflowCheckpointStore : IWorkflowCheckpointStore
{
    private readonly IRepository<WorkflowExecution, Guid> _repository;
    private readonly ILogger<DatabaseWorkflowCheckpointStore> _logger;

    public DatabaseWorkflowCheckpointStore(
        IRepository<WorkflowExecution, Guid> repository,
        ILogger<DatabaseWorkflowCheckpointStore> logger)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken ct = default)
    {
        Check.NotNull(checkpoint);
        Check.NotNullOrWhiteSpace(checkpoint.ExecutionId);

        var entity = await _repository.FirstOrDefaultAsync(e => e.ExecutionId == checkpoint.ExecutionId, ct);

        if (entity == null)
        {
            // 新建
            entity = new WorkflowExecution
            {
                ExecutionId = checkpoint.ExecutionId,
                InitialInput = checkpoint.InitialInput,
                CompletedSteps = JsonSerializer.Serialize(checkpoint.CompletedStepIds),
                StepOutputs = JsonSerializer.Serialize(checkpoint.StepOutputs),
                Status = checkpoint.Status,
                UpdatedTime = checkpoint.UpdatedAt == default ? DateTime.UtcNow : checkpoint.UpdatedAt,
                StepsAwaitingApproval = JsonSerializer.Serialize(checkpoint.StepsAwaitingApproval)
            };

            if (checkpoint.Status is "completed" or "failed")
            {
                entity.CompletedTime = DateTime.UtcNow;
            }

            await _repository.InsertAsync(entity);
            _logger.LogDebug("Created workflow checkpoint for execution {ExecutionId}", checkpoint.ExecutionId);
        }
        else
        {
            // 更新
            entity.CompletedSteps = JsonSerializer.Serialize(checkpoint.CompletedStepIds);
            entity.StepOutputs = JsonSerializer.Serialize(checkpoint.StepOutputs);
            entity.Status = checkpoint.Status;
            entity.UpdatedTime = checkpoint.UpdatedAt == default ? DateTime.UtcNow : checkpoint.UpdatedAt;
            entity.StepsAwaitingApproval = JsonSerializer.Serialize(checkpoint.StepsAwaitingApproval);

            if (checkpoint.Status is "completed" or "failed")
            {
                entity.CompletedTime = DateTime.UtcNow;
            }

            await _repository.UpdateAsync(entity);
            _logger.LogDebug("Updated workflow checkpoint for execution {ExecutionId}, status: {Status}",
                checkpoint.ExecutionId, checkpoint.Status);
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowCheckpoint?> GetCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _repository.FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);

        if (entity == null)
        {
            return null;
        }

        return new WorkflowCheckpoint
        {
            ExecutionId = entity.ExecutionId,
            CompletedStepIds = DeserializeHashSet(entity.CompletedSteps),
            StepOutputs = DeserializeStepOutputs(entity.StepOutputs),
            InitialInput = entity.InitialInput,
            CreatedAt = entity.CreationTime,
            UpdatedAt = entity.UpdatedTime,
            Status = entity.Status,
            StepsAwaitingApproval = DeserializeHashSet(entity.StepsAwaitingApproval)
        };
    }

    /// <inheritdoc />
    public async Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(executionId);

        var entity = await _repository.FirstOrDefaultAsync(e => e.ExecutionId == executionId, ct);

        if (entity != null)
        {
            await _repository.DeleteAsync(entity);
            _logger.LogDebug("Deleted workflow checkpoint for execution {ExecutionId}", executionId);
        }
    }

    /// <summary>
    /// 反序列化 JSON 数组为 HashSet
    /// </summary>
    private static HashSet<string> DeserializeHashSet(string json)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list != null ? new HashSet<string>(list, StringComparer.OrdinalIgnoreCase) : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 反序列化 JSON 对象为 Dictionary
    /// </summary>
    private static Dictionary<string, WorkflowStepOutput> DeserializeStepOutputs(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new(StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, WorkflowStepOutput>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    result[property.Name] = property.Value.GetString() ?? string.Empty;
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    var output = property.Value.Deserialize<WorkflowStepOutput>(TnziJsonDefaults.Options);
                    if (output != null)
                    {
                        result[property.Name] = output;
                    }
                }
            }

            return result;
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
