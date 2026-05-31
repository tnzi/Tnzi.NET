namespace Tnzi.AI.Services;

/// <summary>
/// 评估运行管理服务实现
/// </summary>
public class EvaluationService : ApplicationService, IEvaluationService
{
    private readonly IRepository<EvaluationRun, Guid> _repository;
    private readonly IAgentEvaluator _evaluator;
    private readonly IServiceScopeFactory _scopeFactory;

    public EvaluationService(
        IServiceProvider serviceProvider,
        IRepository<EvaluationRun, Guid> repository,
        IAgentEvaluator evaluator,
        IServiceScopeFactory scopeFactory)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _evaluator = Check.NotNull(evaluator);
        _scopeFactory = Check.NotNull(scopeFactory);
    }

    public async Task<Result<EvaluationRunDetailDto>> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
            return Fail<EvaluationRunDetailDto>("Evaluation run not found", 404, ErrorCodes.EvaluationNotFound);

        return Ok(entity.MapTo<EvaluationRunDetailDto>());
    }

    public async Task<Result<IPagedList<EvaluationRunDto>>> GetListAsync(EvaluationRunQueryDto query)
    {
        Check.NotNull(query);

        var queryable = _repository
            .WhereIf(e => e.AgentId == query.AgentId!.Value, query.AgentId.HasValue)
            .WhereIf(e => e.Status == query.Status!.Value, query.Status.HasValue)
            .OrderByDescending(e => e.CreationTime);

        var pagedList = await queryable.ProjectTo<EvaluationRun, EvaluationRunDto>().CreateAsync(query);

        return Ok(pagedList);
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
            return Fail("Evaluation run not found", 404, ErrorCodes.EvaluationNotFound);

        await _repository.DeleteAsync(entity);
        return Ok();
    }

    public async Task<Result<EvaluationRunDetailDto>> CreateAndRunAsync(CreateEvaluationRunDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);
        Check.NotNullOrEmpty(dto.Cases);

        var cases = dto.Cases.Select(c => new EvaluationCase
        {
            Input = c.Input,
            ExpectedOutput = c.ExpectedOutput
        }).ToList();

        var stopwatch = Stopwatch.StartNew();

        // 创建评估运行记录
        var run = new EvaluationRun
        {
            AgentId = dto.AgentId,
            AgentVersionNumber = dto.VersionNumber,
            CaseCount = cases.Count,
            Status = EvaluationRunStatus.Running
        };
        await _repository.InsertAsync(run, ct);

        try
        {
            var results = new List<EvaluationResult>();

            foreach (var evaluationCase in cases)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _evaluator.EvaluateAsync(evaluationCase, ct);
                results.Add(result);
            }

            stopwatch.Stop();

            run.PassedCount = results.Count(r => r.Passed);
            run.AverageScore = results.Count > 0 ? results.Average(r => r.Score) : 0;
            run.Status = EvaluationRunStatus.Completed;
            run.ResultsJson = JsonSerializer.Serialize(results, TnziJsonDefaults.Options);
            run.Duration = stopwatch.Elapsed;
            await _repository.UpdateAsync(run, ct);

            Logger.LogInformation("Evaluation run {RunId} completed: {Passed}/{Total} passed, avg score {Score:F3}",
                run.Id, run.PassedCount, run.CaseCount, run.AverageScore);

            return Ok(run.MapTo<EvaluationRunDetailDto>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Evaluation run {RunId} failed", run.Id);

            run.Status = EvaluationRunStatus.Failed;
            run.Duration = stopwatch.Elapsed;
            await _repository.UpdateAsync(run, ct);

            return Fail<EvaluationRunDetailDto>($"Evaluation run failed: {ex.Message}", 500);
        }
    }

    public async Task<Result<BatchEvaluationResultDto>> RunBatchAsync(BatchEvaluationDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);
        Check.NotNullOrEmpty(dto.Targets);
        Check.NotNullOrEmpty(dto.Cases);

        var totalStopwatch = Stopwatch.StartNew();

        // 并行运行所有目标的评估（使用信号量限流，防止大量并发 LLM 请求）
        const int maxConcurrency = 3;
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = dto.Targets.Select(async target =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<IEvaluationService>();
                return await scopedService.CreateAndRunAsync(
                    new CreateEvaluationRunDto
                    {
                        AgentId = target.AgentId,
                        VersionNumber = target.VersionNumber,
                        Cases = dto.Cases
                    }, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        totalStopwatch.Stop();

        var successResults = results
            .Where(r => r.Succeeded && r.Data != null)
            .Select(r => r.Data!)
            .ToList();

        Logger.LogInformation("Batch evaluation completed: {Success}/{Total} targets succeeded",
            successResults.Count, dto.Targets.Count);

        return Ok(new BatchEvaluationResultDto
        {
            Results = successResults,
            TotalDuration = totalStopwatch.Elapsed
        });
    }
}
