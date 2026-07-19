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

        // 先保存当前状态为新版本
        await CreateVersionSnapshotAsync(entity, changeDescription ?? $"Before restore to version {versionNumber}");

        // 从快照中恢复定义
        using var doc = JsonDocument.Parse(version.Definition);
        var root = doc.RootElement;

        if (root.TryGetProperty("Name", out var nameProp) || root.TryGetProperty("name", out nameProp))
            entity.Name = nameProp.GetString() ?? entity.Name;

        if (root.TryGetProperty("Description", out var descProp) || root.TryGetProperty("description", out descProp))
            entity.Description = descProp.ValueKind == JsonValueKind.Null ? null : descProp.GetString();

        if (root.TryGetProperty("Steps", out var stepsProp) || root.TryGetProperty("steps", out stepsProp))
            entity.Steps = stepsProp.GetRawText();

        if (root.TryGetProperty("ExecutionMode", out var modeProp) || root.TryGetProperty("executionMode", out modeProp))
        {
            if (Enum.TryParse<WorkflowExecutionMode>(modeProp.GetString(), true, out var mode))
                entity.ExecutionMode = mode;
        }

        if (root.TryGetProperty("IsEnabled", out var enabledProp) || root.TryGetProperty("isEnabled", out enabledProp))
            entity.IsEnabled = enabledProp.GetBoolean();

        if (root.TryGetProperty("Configuration", out var configProp) || root.TryGetProperty("configuration", out configProp))
            entity.Configuration = configProp.ValueKind == JsonValueKind.Null ? null : configProp.GetRawText();

        await _repository.UpdateAsync(entity);
        Logger.LogInformation("Workflow restored to version {Version}: WorkflowId={WorkflowId}", versionNumber, workflowId);

        return Ok();
    }

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
