using WorkflowStepDtoType = Tnzi.AI.Dtos.WorkflowStepDto;

namespace Tnzi.AI.Services;

/// <summary>
/// 工作流服务实现
/// </summary>
public class WorkflowService : ApplicationService, IWorkflowService
{
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly WorkflowBuilderFactory _workflowBuilderFactory;
    private readonly IUsageLogService _usageLogService;
    private readonly IQuotaService _quotaService;

    public WorkflowService(
        IRepository<WorkflowDefinition, Guid> repository,
        WorkflowBuilderFactory workflowBuilderFactory,
        IUsageLogService usageLogService,
        IQuotaService quotaService,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _workflowBuilderFactory = Check.NotNull(workflowBuilderFactory);
        _usageLogService = Check.NotNull(usageLogService);
        _quotaService = Check.NotNull(quotaService);
    }

    public async Task<Result<WorkflowDefinitionDto>> CreateAsync(CreateWorkflowDefinitionDto input)
    {
        var entity = input.MapTo<WorkflowDefinition>();
        entity.Steps = JsonSerializer.Serialize(input.Steps);
        await _repository.InsertAsync(entity);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<WorkflowDefinitionDto>> UpdateAsync(Guid id, UpdateWorkflowDefinitionDto input)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null || entity.IsDeleted) return Fail<WorkflowDefinitionDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);

        if (input.Name != null) entity.Name = input.Name;
        if (input.Description != null) entity.Description = input.Description;
        if (input.Steps != null) entity.Steps = JsonSerializer.Serialize(input.Steps);
        if (input.ExecutionMode.HasValue) entity.ExecutionMode = input.ExecutionMode.Value;
        if (input.IsEnabled.HasValue) entity.IsEnabled = input.IsEnabled.Value;

        await _repository.UpdateAsync(entity);
        return Ok(MapToDto(entity));
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null || entity.IsDeleted) return Fail("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        await _repository.DeleteAsync(entity);
        return Ok();
    }

    public async Task<Result<WorkflowDefinitionDto>> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null || entity.IsDeleted) return Fail<WorkflowDefinitionDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<IPagedList<WorkflowDefinitionDto>>> GetListAsync(PagedQueryDto query)
    {
        var queryable = _repository.OrderByDescending(w => w.CreationTime);
        var pagedList = await queryable.ProjectTo<WorkflowDefinition, WorkflowDefinitionDto>().CreateAsync(query);
        return Ok(pagedList);
    }

    public async Task<Result<WorkflowExecutionResultDto>> RunAsync(Guid workflowId, string input, Guid? userId = null, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var workflowDef = await _repository.GetAsync(workflowId, ct);
        if (workflowDef == null || workflowDef.IsDeleted) return Fail<WorkflowExecutionResultDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        if (!workflowDef.IsEnabled) return Fail<WorkflowExecutionResultDto>("Workflow is disabled", 400, ErrorCodes.WorkflowDisabled);

        if (userId.HasValue)
        {
            var estimatedTokens = input.Length / 4 + 2000;
            var quotaCheckResult = await _quotaService.CheckQuotaAsync(userId.Value, estimatedTokens, ct);
            if (!quotaCheckResult.Succeeded)
            {
                return Fail<WorkflowExecutionResultDto>(quotaCheckResult.Message ?? "Quota check failed", quotaCheckResult.Code ?? 500, ErrorCodes.QuotaCheckFailed);
            }

            var quotaCheck = quotaCheckResult.Data;
            if (quotaCheck != null && !quotaCheck.IsAllowed)
            {
                return Fail<WorkflowExecutionResultDto>(quotaCheck.Reason ?? "Quota exceeded", 429, ErrorCodes.QuotaExceeded);
            }
        }

        try
        {
            var (agents, executionMode) = await _workflowBuilderFactory.BuildWorkflowAsync(workflowDef, ct);

            // 根据执行模式运行工作流
            var response = executionMode is WorkflowExecutionMode.Parallel
                ? await WorkflowRunner.RunParallelAsync(agents, input, ct)
                : await WorkflowRunner.RunSequentialAsync(agents, input, ct);

            await _usageLogService.LogUsageAsync(AIOperationType.WorkflowRun, "Workflow", workflowDef.Name, 0, 0, stopwatch.ElapsedMilliseconds, true, ct: ct);

            return Ok(new WorkflowExecutionResultDto
            {
                Output = response.Text ?? string.Empty,
                Status = "Completed"
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Workflow execution failed: {WorkflowId}", workflowId);
            await _usageLogService.LogUsageAsync(AIOperationType.WorkflowRun, "Workflow", workflowDef.Name, 0, 0, stopwatch.ElapsedMilliseconds, false, ex.Message, ct: ct);
            return Fail<WorkflowExecutionResultDto>($"Workflow failed: {ex.Message}", 500, ErrorCodes.WorkflowFailed);
        }
    }

    public async IAsyncEnumerable<WorkflowExecutionResultDto> RunStreamingAsync(Guid workflowId, string input, Guid? userId = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var workflowDef = await _repository.GetAsync(workflowId, ct);
        if (workflowDef == null || workflowDef.IsDeleted)
            throw new BusinessException("Workflow not found", ErrorCodes.WorkflowNotFound, 404);
        if (!workflowDef.IsEnabled)
            throw new BusinessException("Workflow is disabled", ErrorCodes.WorkflowDisabled, 400);

        var (agents, executionMode) = await _workflowBuilderFactory.BuildWorkflowAsync(workflowDef, ct);

        if (executionMode is WorkflowExecutionMode.Parallel)
        {
            // 并行模式无法真正流式，退化为批量返回
            var response = await WorkflowRunner.RunParallelAsync(agents, input, ct);
            yield return new WorkflowExecutionResultDto
            {
                Output = response.Text ?? string.Empty,
                Status = "Completed"
            };
        }
        else
        {
            // 顺序模式: 逐步流式，每步 yield 中间结果
            var currentInput = input;
            for (var i = 0; i < agents.Count; i++)
            {
                var response = await agents[i].ExecuteAsync(
                    new List<ChatMessage> { new(ChatRole.User, currentInput) }, ct);
                currentInput = response.Text ?? string.Empty;

                yield return new WorkflowExecutionResultDto
                {
                    Output = currentInput,
                    Status = i == agents.Count - 1 ? "Completed" : $"Step {i + 1}/{agents.Count}"
                };
            }
        }
    }

    private static WorkflowDefinitionDto MapToDto(WorkflowDefinition entity)
    {
        var dto = entity.MapTo<WorkflowDefinitionDto>();
        dto.Steps = string.IsNullOrWhiteSpace(entity.Steps)
            ? new List<WorkflowStepDtoType>()
            : JsonSerializer.Deserialize<List<WorkflowStepDtoType>>(entity.Steps) ?? new();
        return dto;
    }
}
