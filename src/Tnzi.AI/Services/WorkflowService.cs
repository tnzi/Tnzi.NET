using WorkflowStepDtoType = Tnzi.AI.Dtos.WorkflowStepDto;

namespace Tnzi.AI.Services;

/// <summary>
/// 工作流服务实现
/// </summary>
public class WorkflowService : ApplicationService, IWorkflowService
{
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IWorkflowBuilderFactory _workflowBuilderFactory;
    private readonly IUsageLogService _usageLogService;
    private readonly IQuotaService _quotaService;

    public WorkflowService(
        IRepository<WorkflowDefinition, Guid> repository,
        IWorkflowBuilderFactory workflowBuilderFactory,
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
        Check.NotNull(input);
        var entity = input.MapTo<WorkflowDefinition>();
        entity.Steps = JsonSerializer.Serialize(input.Steps);
        await _repository.InsertAsync(entity);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<WorkflowDefinitionDto>> UpdateAsync(Guid id, UpdateWorkflowDefinitionDto input)
    {
        Check.NotNull(input);
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail<WorkflowDefinitionDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);

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
        if (workflowDef == null) return Fail<WorkflowExecutionResultDto>("Workflow not found", 404, ErrorCodes.WorkflowNotFound);
        if (!workflowDef.IsEnabled) return Fail<WorkflowExecutionResultDto>("Workflow is disabled", 400, ErrorCodes.WorkflowDisabled);

        QuotaReservation? reservation = null;
        if (userId.HasValue)
        {
            // 步骤数影响 Token 用量：每步输出作为下一步输入，总量约为单步的 N 倍
            var stepCount = 1;
            if (!string.IsNullOrWhiteSpace(workflowDef.Steps))
            {
                try
                {
                    using var doc = JsonDocument.Parse(workflowDef.Steps);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        stepCount = Math.Max(1, doc.RootElement.GetArrayLength());
                }
                catch { /* 解析失败时退化为单步估算 */ }
            }
            var estimatedTokens = (input.Length / 4 + 2000) * stepCount;
            var reserveResult = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, ct);
            if (!reserveResult.Succeeded)
            {
                return Fail<WorkflowExecutionResultDto>(
                    reserveResult.Message ?? "Quota reservation failed",
                    reserveResult.Code ?? 500,
                    reserveResult.Code == 429 ? ErrorCodes.QuotaExceeded : ErrorCodes.QuotaCheckFailed);
            }
            reservation = reserveResult.Data;
        }

        try
        {
            WorkflowExecutionResultDto resultDto;

            if (workflowDef.ExecutionMode == WorkflowExecutionMode.Dag)
            {
                // DAG 模式：构建 DAG 步骤并按拓扑排序执行
                var dagSteps = await _workflowBuilderFactory.BuildDagStepsAsync(workflowDef, ct);
                var dagResult = await WorkflowRunner.RunDagAsync(dagSteps, input, ct);

                resultDto = new WorkflowExecutionResultDto
                {
                    Output = dagResult.FinalOutput,
                    Status = "Completed",
                    StepResults = dagResult.StepResults
                };
            }
            else
            {
                // Sequential/Parallel 模式
                var (agents, executionMode) = await _workflowBuilderFactory.BuildWorkflowAsync(workflowDef, ct);
                var response = executionMode is WorkflowExecutionMode.Parallel
                    ? await WorkflowRunner.RunParallelAsync(agents, input, ct)
                    : await WorkflowRunner.RunSequentialAsync(agents, input, ct);

                var status = response.FinishReason != null && response.FinishReason.Contains("failed", StringComparison.OrdinalIgnoreCase)
                    ? $"PartialFailure: {response.FinishReason}"
                    : "Completed";

                resultDto = new WorkflowExecutionResultDto
                {
                    Output = response.Text ?? string.Empty,
                    Status = status
                };
            }

            await _usageLogService.LogUsageAsync(AIOperationType.WorkflowRun, "Workflow", workflowDef.Name, 0, 0, stopwatch.ElapsedMilliseconds, true, ct: ct);

            // 结算配额
            if (userId.HasValue && reservation != null)
            {
                try { await _quotaService.SettleQuotaAsync(userId.Value, reservation, 0, ct); }
                catch (Exception settleEx) { Logger.LogError(settleEx, "Failed to settle workflow quota: WorkflowId={WorkflowId}", workflowId); }
            }

            return Ok(resultDto);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Workflow execution failed: {WorkflowId}", workflowId);
            await _usageLogService.LogUsageAsync(AIOperationType.WorkflowRun, "Workflow", workflowDef.Name, 0, 0, stopwatch.ElapsedMilliseconds, false, ex.Message, ct: ct);

            // 释放预留配额
            if (userId.HasValue && reservation != null)
            {
                try { await _quotaService.SettleQuotaAsync(userId.Value, reservation, 0, CancellationToken.None); }
                catch (Exception settleEx) { Logger.LogError(settleEx, "Failed to release workflow quota reservation"); }
            }

            return Fail<WorkflowExecutionResultDto>("Workflow execution failed.", 500, ErrorCodes.WorkflowFailed);
        }
    }

    /// <summary>
    /// 流式运行工作流。
    /// 注意：并行模式（Parallel）无法真正逐步流式，会退化为单次批量返回所有步骤合并后的结果。
    /// 仅顺序模式（Sequential）支持逐步 yield 每个步骤的中间结果。
    /// </summary>
    public async IAsyncEnumerable<WorkflowExecutionResultDto> RunStreamingAsync(Guid workflowId, string input, Guid? userId = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var workflowDef = await _repository.GetAsync(workflowId, ct);
        if (workflowDef == null)
            throw new BusinessException("Workflow not found", ErrorCodes.WorkflowNotFound, 404);
        if (!workflowDef.IsEnabled)
            throw new BusinessException("Workflow is disabled", ErrorCodes.WorkflowDisabled, 400);

        // 配额预留
        QuotaReservation? reservation = null;
        if (userId.HasValue)
        {
            var stepCount = 1;
            if (!string.IsNullOrWhiteSpace(workflowDef.Steps))
            {
                try
                {
                    using var doc = JsonDocument.Parse(workflowDef.Steps);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        stepCount = Math.Max(1, doc.RootElement.GetArrayLength());
                }
                catch { /* 解析失败时退化为单步估算 */ }
            }
            var estimatedTokens = (input.Length / 4 + 2000) * stepCount;
            var reserveResult = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, ct);
            if (!reserveResult.Succeeded)
            {
                throw new BusinessException(
                    reserveResult.Message ?? "Quota reservation failed",
                    reserveResult.Code == 429 ? ErrorCodes.QuotaExceeded : ErrorCodes.QuotaCheckFailed,
                    reserveResult.Code ?? 500);
            }
            reservation = reserveResult.Data;
        }

        if (workflowDef.ExecutionMode == WorkflowExecutionMode.Dag)
        {
            // DAG 模式：逐层流式返回步骤结果
            var dagSteps = await _workflowBuilderFactory.BuildDagStepsAsync(workflowDef, ct);
            var dagResult = await WorkflowRunner.RunDagAsync(dagSteps, input, ct);

            foreach (var stepResult in dagResult.StepResults)
            {
                yield return new WorkflowExecutionResultDto
                {
                    Output = stepResult.Output,
                    Status = $"Step '{stepResult.StepId}'" + (stepResult.Skipped ? " (skipped)" : ""),
                    StepResults = [stepResult]
                };
            }

            yield return new WorkflowExecutionResultDto
            {
                Output = dagResult.FinalOutput,
                Status = "Completed",
                StepResults = dagResult.StepResults
            };
        }
        else
        {
            var (agents, executionMode) = await _workflowBuilderFactory.BuildWorkflowAsync(workflowDef, ct);

            if (executionMode is WorkflowExecutionMode.Parallel)
            {
                var response = await WorkflowRunner.RunParallelAsync(agents, input, ct);
                yield return new WorkflowExecutionResultDto
                {
                    Output = response.Text ?? string.Empty,
                    Status = "Completed"
                };
            }
            else
            {
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

        // 使用日志和配额结算（使用 CancellationToken.None 防止客户端断连导致配额泄漏）
        try
        {
            await _usageLogService.LogUsageAsync(AIOperationType.WorkflowRun, "Workflow", workflowDef.Name, 0, 0, stopwatch.ElapsedMilliseconds, true, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to log workflow streaming usage: WorkflowId={WorkflowId}", workflowId);
        }

        if (userId.HasValue && reservation != null)
        {
            try
            {
                await _quotaService.SettleQuotaAsync(userId.Value, reservation, 0, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to settle workflow streaming quota: WorkflowId={WorkflowId}", workflowId);
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
