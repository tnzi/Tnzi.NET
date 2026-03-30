namespace Tnzi.AI.Services;

/// <summary>
/// 工作流服务实现
/// </summary>
public partial class WorkflowService : ApplicationService, IWorkflowService
{
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IRepository<WorkflowExecution, Guid> _executionRepository;
    private readonly IRepository<WorkflowDefinitionVersion, Guid> _versionRepository;
    private readonly IRepository<AgentRun, Guid> _runRepository;
    private readonly IUsageLogService _usageLogService;
    private readonly IQuotaService _quotaService;
    private readonly IWorkflowCheckpointStore _checkpointStore;
    private readonly WorkflowEngine _workflowEngine;

    public WorkflowService(
        IRepository<WorkflowDefinition, Guid> repository,
        IRepository<WorkflowExecution, Guid> executionRepository,
        IRepository<WorkflowDefinitionVersion, Guid> versionRepository,
        IRepository<AgentRun, Guid> runRepository,
        IUsageLogService usageLogService,
        IQuotaService quotaService,
        IWorkflowCheckpointStore checkpointStore,
        WorkflowEngine workflowEngine,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _executionRepository = Check.NotNull(executionRepository);
        _versionRepository = Check.NotNull(versionRepository);
        _runRepository = Check.NotNull(runRepository);
        _usageLogService = Check.NotNull(usageLogService);
        _quotaService = Check.NotNull(quotaService);
        _checkpointStore = Check.NotNull(checkpointStore);
        _workflowEngine = Check.NotNull(workflowEngine);
    }

    public async Task<Result<WorkflowDefinitionDto>> CreateAsync(CreateWorkflowDefinitionDto input)
    {
        Check.NotNull(input);
        var entity = input.MapTo<WorkflowDefinition>();
        entity.Steps = JsonSerializer.Serialize(input.Steps, TnziJsonDefaults.Options);
        await _repository.InsertAsync(entity);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<WorkflowDefinitionDto>> UpdateAsync(Guid id, UpdateWorkflowDefinitionDto input)
    {
        Check.NotNull(input);
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail<WorkflowDefinitionDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);

        // 自动创建版本快照（在修改之前保存当前状态）
        var changeDescription = (input as UpdateWorkflowDefinitionWithVersionDto)?.ChangeDescription;
        await CreateVersionSnapshotAsync(entity, changeDescription);

        if (input.Name != null) entity.Name = input.Name;
        if (input.Description != null) entity.Description = input.Description;
        if (input.Steps != null) entity.Steps = JsonSerializer.Serialize(input.Steps, TnziJsonDefaults.Options);
        if (input.ExecutionMode.HasValue) entity.ExecutionMode = input.ExecutionMode.Value;
        if (input.IsEnabled.HasValue) entity.IsEnabled = input.IsEnabled.Value;

        await _repository.UpdateAsync(entity);
        return Ok(MapToDto(entity));
    }

    private async Task CreateVersionSnapshotAsync(WorkflowDefinition entity, string? changeDescription = null)
    {
        // 获取当前最大版本号
        var maxVersion = await _versionRepository.AsQueryable()
            .Where(v => v.WorkflowDefinitionId == entity.Id)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

        // 序列化当前定义的完整快照
        var snapshot = JsonSerializer.Serialize(new
        {
            entity.Name,
            entity.Description,
            entity.Steps,
            ExecutionMode = entity.ExecutionMode.ToString(),
            entity.IsEnabled,
            entity.Configuration
        }, TnziJsonDefaults.Options);

        var version = new WorkflowDefinitionVersion
        {
            WorkflowDefinitionId = entity.Id,
            VersionNumber = maxVersion + 1,
            Definition = snapshot,
            ChangeDescription = changeDescription
        };

        await _versionRepository.InsertAsync(version);
        Logger.LogInformation("Workflow version created: WorkflowId={WorkflowId}, Version={Version}", entity.Id, version.VersionNumber);
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        await _repository.DeleteAsync(entity);
        return Ok();
    }

    public async Task<Result<WorkflowDefinitionDto>> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail<WorkflowDefinitionDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<IPagedList<WorkflowDefinitionDto>>> GetListAsync(WorkflowDefinitionQueryDto query)
    {
        Check.NotNull(query);

        var queryable = _repository
            .WhereIf(w => w.Name.ToLower().Contains(query.Keyword!.ToLower()) ||
                         (w.Description != null && w.Description.ToLower().Contains(query.Keyword!.ToLower())),
                !string.IsNullOrWhiteSpace(query.Keyword))
            .WhereIf(w => w.IsEnabled == query.IsEnabled!.Value, query.IsEnabled.HasValue)
            .WhereIf(w => w.ExecutionMode == query.ExecutionMode!.Value, query.ExecutionMode.HasValue)
            .OrderByDescending(w => w.CreationTime);

        var pagedList = await queryable.ProjectTo<WorkflowDefinition, WorkflowDefinitionDto>().CreateAsync(query);
        return Ok(pagedList);
    }

    private static WorkflowDefinitionDto MapToDto(WorkflowDefinition entity)
    {
        var dto = entity.MapTo<WorkflowDefinitionDto>();
        dto.Steps = string.IsNullOrWhiteSpace(entity.Steps)
            ? new List<WorkflowStepDto>()
            : JsonSerializer.Deserialize<List<WorkflowStepDto>>(entity.Steps, TnziJsonDefaults.Options) ?? new();
        return dto;
    }

    private static WorkflowGraph BuildWorkflowGraph(WorkflowDefinition workflowDef)
    {
        var rawSteps = string.IsNullOrWhiteSpace(workflowDef.Steps)
            ? new List<WorkflowStepDto>()
            : JsonSerializer.Deserialize<List<WorkflowStepDto>>(workflowDef.Steps, TnziJsonDefaults.Options) ?? [];

        var steps = rawSteps
            .Select((step, index) => CloneStep(step, index))
            .OrderBy(step => step.Order)
            .ThenBy(step => step.Configuration?.TryGetValue("__originalIndex", out var indexValue) == true && int.TryParse(indexValue, out var parsedIndex)
                ? parsedIndex
                : int.MaxValue)
            .ToList();

        for (var i = 0; i < steps.Count; i++)
        {
            steps[i].StepId ??= $"step-{i + 1}";
            steps[i].Configuration?.Remove("__originalIndex");
        }

        if (workflowDef.ExecutionMode == WorkflowExecutionMode.Sequential)
        {
            for (var i = 1; i < steps.Count; i++)
            {
                var dependsOn = steps[i].DependsOn;
                if (dependsOn == null || dependsOn.Count == 0)
                {
                    var previousStepId = steps[i - 1].StepId;
                    if (!string.IsNullOrWhiteSpace(previousStepId))
                    {
                        steps[i].DependsOn = [previousStepId];
                    }
                }
            }
        }

        WorkflowGraphConfiguration? graphConfig = null;
        if (!string.IsNullOrWhiteSpace(workflowDef.Configuration))
        {
            graphConfig = JsonSerializer.Deserialize<WorkflowGraphConfiguration>(workflowDef.Configuration, TnziJsonDefaults.Options);
        }

        return new WorkflowGraph(
            steps,
            graphConfig?.ConditionalEdges,
            graphConfig?.Loops);
    }

    private static WorkflowStepDto CloneStep(WorkflowStepDto step, int originalIndex)
    {
        var configuration = step.Configuration != null
            ? new Dictionary<string, string>(step.Configuration, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        configuration["__originalIndex"] = originalIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new WorkflowStepDto
        {
            StepId = step.StepId,
            AgentId = step.AgentId,
            Order = step.Order,
            DependsOn = step.DependsOn?.ToList(),
            Condition = step.Condition,
            Provider = step.Provider,
            Model = step.Model,
            Instructions = step.Instructions,
            MaxRetries = step.MaxRetries,
            RetryDelaySeconds = step.RetryDelaySeconds,
            TimeoutSeconds = step.TimeoutSeconds,
            RequiresApproval = step.RequiresApproval,
            Configuration = configuration
        };
    }

    private sealed class WorkflowGraphConfiguration
    {
        public List<ConditionalEdge>? ConditionalEdges { get; init; }

        public Dictionary<string, LoopDefinition>? Loops { get; init; }
    }
}
