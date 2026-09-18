namespace Tnzi.AI.Workflow.Services;

/// <summary>
/// 工作流服务 - 版本历史和执行统计
/// </summary>
public partial class WorkflowService
{
    public async Task<Result<List<WorkflowDefinitionVersionDto>>> GetVersionHistoryAsync(Guid workflowId)
    {
        var exists = await _repository.AnyAsync(e => e.Id == workflowId);
        if (!exists)
            return Fail<List<WorkflowDefinitionVersionDto>>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);

        var versions = await _versionRepository.AsQueryable()
            .Where(v => v.WorkflowDefinitionId == workflowId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new WorkflowDefinitionVersionDto
            {
                Id = v.Id,
                WorkflowDefinitionId = v.WorkflowDefinitionId,
                VersionNumber = v.VersionNumber,
                ChangeDescription = v.ChangeDescription,
                CreationTime = v.CreationTime
            })
            .ToListAsync();

        return Ok(versions);
    }

    public async Task<Result<WorkflowDefinitionVersionDto>> GetVersionAsync(Guid workflowId, int versionNumber)
    {
        var version = await _versionRepository.AsQueryable()
            .Where(v => v.WorkflowDefinitionId == workflowId && v.VersionNumber == versionNumber)
            .Select(v => new WorkflowDefinitionVersionDto
            {
                Id = v.Id,
                WorkflowDefinitionId = v.WorkflowDefinitionId,
                VersionNumber = v.VersionNumber,
                ChangeDescription = v.ChangeDescription,
                Definition = v.Definition,
                CreationTime = v.CreationTime
            })
            .FirstOrDefaultAsync();

        if (version == null)
            return Fail<WorkflowDefinitionVersionDto>("Workflow version not found", 404, ErrorCodes.WorkflowVersionNotFound);

        return Ok(version);
    }

    public async Task<Result> RestoreVersionAsync(Guid workflowId, int versionNumber, string? changeDescription = null)
    {
        var entity = await _repository.GetAsync(workflowId);
        if (entity == null)
            return Fail("Workflow not found", 404, ErrorCodes.WorkflowNotFound);

        var version = await _versionRepository
            .FirstOrDefaultAsync(v => v.WorkflowDefinitionId == workflowId && v.VersionNumber == versionNumber);
        if (version == null)
            return Fail("Workflow version not found", 404, ErrorCodes.WorkflowVersionNotFound);

        // ★ 先把快照解析并校验完，再落任何写入：一份解析不了的快照既不该建"恢复前"版本，
        // 更不该把定义行写坏后回 200。此前 Steps 是被双重编码写回去的（快照把 Steps 字符串
        // 序列化成 JSON 字符串 token，恢复用 GetRawText() 取回带引号带转义的字面量），
        // 恢复本身从不读回 Steps，于是每个后续读取路径都抛 JsonException 而这里毫无症状。
        RestoredDefinition restored;
        try
        {
            restored = ParseSnapshot(version.Definition, entity);
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Workflow version snapshot is not restorable: WorkflowId={WorkflowId}, Version={Version}", workflowId, versionNumber);
            return Fail("Workflow version snapshot is not restorable", 500, ErrorCodes.WorkflowFailed);
        }

        // 先保存当前状态为新版本
        await CreateVersionSnapshotAsync(entity, changeDescription ?? $"Before restore to version {versionNumber}");

        entity.Name = restored.Name;
        entity.Description = restored.Description;
        entity.Steps = restored.Steps;
        entity.ExecutionMode = restored.ExecutionMode;
        entity.IsEnabled = restored.IsEnabled;
        entity.Configuration = restored.Configuration;

        await _repository.UpdateAsync(entity);
        Logger.LogInformation("Workflow restored to version {Version}: WorkflowId={WorkflowId}", versionNumber, workflowId);

        return Ok();
    }

    private sealed record RestoredDefinition(
        string Name,
        string? Description,
        string Steps,
        WorkflowExecutionMode ExecutionMode,
        bool IsEnabled,
        string? Configuration);

    /// <summary>
    /// 把版本快照解析成可写回实体的字段集。快照是完整快照（六个成员全在，只有 null 成员会被
    /// 序列化选项省略），所以"键不存在"就是"当时为 null"，而不是"保留现值"。
    /// </summary>
    /// <exception cref="JsonException">快照本身或其中的 Steps 解析不出步骤列表。</exception>
    private static RestoredDefinition ParseSnapshot(string definition, WorkflowDefinition current)
    {
        using var doc = JsonDocument.Parse(definition);
        var root = doc.RootElement;

        var name = TryGetSnapshotProperty(root, "Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
            ? nameProp.GetString() ?? current.Name
            : current.Name;

        var description = TryGetSnapshotProperty(root, "Description", out var descProp) && descProp.ValueKind == JsonValueKind.String
            ? descProp.GetString()
            : null;

        var steps = TryGetSnapshotProperty(root, "Steps", out var stepsProp)
            ? ReadEmbeddedJson(stepsProp) ?? "[]"
            : "[]";
        // 写回之前先按读取方的方式解析一遍：解析不过的 Steps 一旦落库，行就再也读不出来了。
        _ = JsonSerializer.Deserialize<List<WorkflowStepDto>>(steps, TnziJsonDefaults.Options)
            ?? throw new JsonException("Snapshot steps did not deserialize to a step list.");

        var executionMode = current.ExecutionMode;
        if (TryGetSnapshotProperty(root, "ExecutionMode", out var modeProp)
            && modeProp.ValueKind == JsonValueKind.String
            && Enum.TryParse<WorkflowExecutionMode>(modeProp.GetString(), true, out var parsedMode))
        {
            executionMode = parsedMode;
        }

        var isEnabled = TryGetSnapshotProperty(root, "IsEnabled", out var enabledProp)
            && enabledProp.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? enabledProp.GetBoolean()
            : current.IsEnabled;

        var configuration = TryGetSnapshotProperty(root, "Configuration", out var configProp)
            ? ReadEmbeddedJson(configProp)
            : null;
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            using var configurationDoc = JsonDocument.Parse(configuration);
        }

        return new RestoredDefinition(name, description, steps, executionMode, isEnabled, configuration);
    }

    private static bool TryGetSnapshotProperty(JsonElement root, string pascalName, out JsonElement value)
    {
        if (root.TryGetProperty(pascalName, out value))
        {
            return true;
        }

        var camelName = char.ToLowerInvariant(pascalName[0]) + pascalName[1..];
        return root.TryGetProperty(camelName, out value);
    }

    /// <summary>
    /// 读出快照里嵌入的 JSON 列：新快照存的是 JSON 值本身（数组/对象），直接取原文；
    /// 旧快照存的是装着 JSON 的字符串 token，取字符串内容。Null 视为无值。
    /// </summary>
    private static string? ReadEmbeddedJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText()
    };

    public async Task<Result<WorkflowExecutionStatsDto>> GetExecutionStatsAsync(Guid workflowId)
    {
        var exists = await _repository.AnyAsync(e => e.Id == workflowId);
        if (!exists)
            return Fail<WorkflowExecutionStatsDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);

        var executions = await _executionRepository.AsQueryable()
            .Where(e => e.WorkflowDefinitionId == workflowId)
            .Select(e => new { e.Status, e.DurationMs })
            .ToListAsync();

        if (executions.Count == 0)
        {
            return Ok(new WorkflowExecutionStatsDto
            {
                WorkflowId = workflowId,
                TotalExecutions = 0,
                SuccessRate = 0
            });
        }

        var completedCount = executions.Count(e => e.Status == WorkflowExecutionStatus.Completed);
        var durationsWithValue = executions
            .Where(e => e.DurationMs.HasValue)
            .Select(e => e.DurationMs!.Value)
            .OrderBy(d => d)
            .ToList();

        double? avgDuration = null;
        long? minDuration = null;
        long? maxDuration = null;
        long? p95Duration = null;

        if (durationsWithValue.Count > 0)
        {
            avgDuration = durationsWithValue.Average();
            minDuration = durationsWithValue[0];
            maxDuration = durationsWithValue[^1];

            // P95: 取第 95 百分位索引
            var p95Index = (int)Math.Ceiling(durationsWithValue.Count * 0.95) - 1;
            p95Duration = durationsWithValue[Math.Clamp(p95Index, 0, durationsWithValue.Count - 1)];
        }

        return Ok(new WorkflowExecutionStatsDto
        {
            WorkflowId = workflowId,
            TotalExecutions = executions.Count,
            AvgDurationMs = avgDuration,
            MinDurationMs = minDuration,
            MaxDurationMs = maxDuration,
            P95DurationMs = p95Duration,
            SuccessRate = executions.Count > 0 ? (double)completedCount / executions.Count : 0
        });
    }
}
