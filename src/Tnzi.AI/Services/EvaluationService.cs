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

        // 被评估的 Agent 与版本随每个用例走：评估器据此真的跑那个 Agent，而不是只把它们写进运行记录
        var cases = dto.Cases.Select(c => new EvaluationCase
        {
            Input = c.Input,
            ExpectedOutput = c.ExpectedOutput,
            AgentId = dto.AgentId,
            VersionNumber = dto.VersionNumber
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 请求被取消（管理员关掉页面、网关超时）：行已落库为 Running，不收尾就永远停在 Running，
            // 污染列表与趋势。按失败收尾（状态枚举没有「已取消」），落库不跟已取消的令牌，再把取消原样抛出。
            stopwatch.Stop();
            Logger.LogInformation("Evaluation run {RunId} was cancelled after {Elapsed}", run.Id, stopwatch.Elapsed);

            run.Status = EvaluationRunStatus.Failed;
            run.Duration = stopwatch.Elapsed;
            await _repository.UpdateAsync(run, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Evaluation run {RunId} failed", run.Id);

            run.Status = EvaluationRunStatus.Failed;
            run.Duration = stopwatch.Elapsed;
            await _repository.UpdateAsync(run, CancellationToken.None);

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

        // 失败的目标单独报出来：此前只留成功的那些，三个目标挂了一个，响应里只有两条、状态 200，
        // 调用方分不清第三个是失败了还是根本没请求
        var successResults = new List<EvaluationRunDetailDto>();
        var failures = new List<BatchEvaluationFailureDto>();
        for (var i = 0; i < results.Length; i++)
        {
            if (results[i].Succeeded && results[i].Data != null)
            {
                successResults.Add(results[i].Data!);
                continue;
            }

            failures.Add(new BatchEvaluationFailureDto
            {
                AgentId = dto.Targets[i].AgentId,
                VersionNumber = dto.Targets[i].VersionNumber,
                Message = results[i].Message ?? "Evaluation run failed"
            });
        }

        Logger.LogInformation("Batch evaluation completed: {Success}/{Total} targets succeeded",
            successResults.Count, dto.Targets.Count);

        return Ok(new BatchEvaluationResultDto
        {
            Results = successResults,
            Failures = failures,
            TotalDuration = totalStopwatch.Elapsed
        });
    }
}
