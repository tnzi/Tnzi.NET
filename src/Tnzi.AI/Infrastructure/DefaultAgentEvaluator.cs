namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 默认 Agent 评估器实现 — 通过 IChatService 发送输入并与期望输出对比
/// </summary>
[ExperimentalApi(Reason = "Agent evaluation is in preview")]
public class DefaultAgentEvaluator : IAgentEvaluator
{
    private readonly IChatService _chatService;
    private readonly IRepository<EvaluationRun, Guid> _repository;
    private readonly ILogger<DefaultAgentEvaluator> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DefaultAgentEvaluator(
        IChatService chatService,
        IRepository<EvaluationRun, Guid> repository,
        ILogger<DefaultAgentEvaluator> logger)
    {
        _chatService = Check.NotNull(chatService);
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<EvaluationResult> EvaluateAsync(EvaluationCase evaluationCase, CancellationToken ct = default)
    {
        Check.NotNull(evaluationCase);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 发送输入到 ChatService
            var request = new ChatRequestDto { Message = evaluationCase.Input };
            var chatResult = await _chatService.ChatAsync(request, ct);

            stopwatch.Stop();

            var actualOutput = chatResult.Succeeded
                ? chatResult.Data?.Content ?? string.Empty
                : string.Empty;

            // 评估结果
            var (passed, score, reason) = EvaluateOutput(actualOutput, evaluationCase.ExpectedOutput);

            return new EvaluationResult
            {
                CaseId = evaluationCase.CaseId,
                ActualOutput = actualOutput,
                Passed = passed,
                Score = score,
                Reason = reason,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Evaluation failed for case {CaseId}", evaluationCase.CaseId);

            return new EvaluationResult
            {
                CaseId = evaluationCase.CaseId,
                ActualOutput = string.Empty,
                Passed = false,
                Score = 0,
                Reason = $"Evaluation failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<EvaluationSummary> EvaluateBatchAsync(List<EvaluationCase> cases, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(cases);

        var totalStopwatch = Stopwatch.StartNew();
        var agentId = Guid.Empty; // 默认 Agent ID（无指定 Agent 场景）

        // 创建评估运行记录
        var run = new EvaluationRun
        {
            AgentId = agentId,
            CaseCount = cases.Count,
            Status = "running"
        };
        await _repository.InsertAsync(run, ct);

        var results = new List<EvaluationResult>();

        try
        {
            // 逐一评估每个用例
            foreach (var evaluationCase in cases)
            {
                ct.ThrowIfCancellationRequested();
                var result = await EvaluateAsync(evaluationCase, ct);
                results.Add(result);
            }

            totalStopwatch.Stop();

            var passedCount = results.Count(r => r.Passed);
            var averageScore = results.Count > 0 ? results.Average(r => r.Score) : 0;

            // 更新评估运行记录
            run.PassedCount = passedCount;
            run.AverageScore = averageScore;
            run.Status = "completed";
            run.ResultsJson = JsonSerializer.Serialize(results, JsonOptions);
            run.Duration = totalStopwatch.Elapsed;
            await _repository.UpdateAsync(run, ct);

            return new EvaluationSummary
            {
                Results = results,
                TotalCases = cases.Count,
                PassedCases = passedCount,
                TotalDuration = totalStopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _logger.LogError(ex, "Batch evaluation failed after processing {Count}/{Total} cases", results.Count, cases.Count);

            // 更新运行记录为失败
            run.PassedCount = results.Count(r => r.Passed);
            run.AverageScore = results.Count > 0 ? results.Average(r => r.Score) : 0;
            run.Status = "failed";
            run.ResultsJson = JsonSerializer.Serialize(results, JsonOptions);
            run.Duration = totalStopwatch.Elapsed;
            await _repository.UpdateAsync(run, ct);

            throw;
        }
    }

    /// <summary>
    /// 评估输出 — 精确匹配或包含匹配
    /// </summary>
    private static (bool Passed, double Score, string Reason) EvaluateOutput(string actualOutput, string? expectedOutput)
    {
        // 无期望输出时，只要有实际输出即视为通过
        if (string.IsNullOrEmpty(expectedOutput))
        {
            var hasOutput = !string.IsNullOrWhiteSpace(actualOutput);
            return (hasOutput, hasOutput ? 1.0 : 0.0,
                hasOutput ? "Output generated (no expected output specified)" : "No output generated");
        }

        // 精确匹配（忽略大小写和首尾空白）
        if (string.Equals(actualOutput.Trim(), expectedOutput.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (true, 1.0, "Exact match");
        }

        // 包含匹配（忽略大小写）
        if (actualOutput.Contains(expectedOutput, StringComparison.OrdinalIgnoreCase))
        {
            return (true, 0.8, "Contains expected output");
        }

        // 反向包含（期望输出包含实际输出）
        if (expectedOutput.Contains(actualOutput, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(actualOutput))
        {
            return (false, 0.4, "Actual output is a subset of expected output");
        }

        return (false, 0.0, "Output does not match expected");
    }
}
