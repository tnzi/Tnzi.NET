namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 默认 Agent 评估器实现 - 通过 IChatService 发送输入并与期望输出对比
/// </summary>
[ExperimentalApi(Reason = "Agent evaluation is in preview")]
public class DefaultAgentEvaluator : IAgentEvaluator
{
    private readonly IChatService _chatService;
    private readonly IRepository<EvaluationRun, Guid> _repository;
    private readonly IAiUtility _aiUtility;
    private readonly ILogger<DefaultAgentEvaluator> _logger;

    public DefaultAgentEvaluator(
        IChatService chatService,
        IRepository<EvaluationRun, Guid> repository,
        IAiUtility aiUtility,
        ILogger<DefaultAgentEvaluator> logger)
    {
        _chatService = Check.NotNull(chatService);
        _repository = Check.NotNull(repository);
        _aiUtility = Check.NotNull(aiUtility);
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

            // 评估结果（有期望输出且非精确匹配时用 LLM-as-judge 语义评分）
            var (passed, score, reason) = await EvaluateOutputAsync(
                evaluationCase.Input, actualOutput, evaluationCase.ExpectedOutput, ct);

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
            Status = EvaluationRunStatus.Running
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
            run.Status = EvaluationRunStatus.Completed;
            run.ResultsJson = JsonSerializer.Serialize(results, TnziJsonDefaults.Options);
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
            run.Status = EvaluationRunStatus.Failed;
            run.ResultsJson = JsonSerializer.Serialize(results, TnziJsonDefaults.Options);
            run.Duration = totalStopwatch.Elapsed;
            await _repository.UpdateAsync(run, ct);

            throw;
        }
    }

    /// <summary>
    /// 评估输出。优先精确匹配（快速、零成本），否则用 LLM-as-judge 做语义评分，
    /// LLM 不可用/失败时回退到字符串包含匹配（fail-safe，保留可观察的近似评分）。
    /// </summary>
    private async Task<(bool Passed, double Score, string Reason)> EvaluateOutputAsync(
        string input, string actualOutput, string? expectedOutput, CancellationToken ct)
    {
        // 无期望输出时，只要有实际输出即视为通过
        if (string.IsNullOrEmpty(expectedOutput))
        {
            var hasOutput = !string.IsNullOrWhiteSpace(actualOutput);
            return (hasOutput, hasOutput ? 1.0 : 0.0,
                hasOutput ? "Output generated (no expected output specified)" : "No output generated");
        }

        // 精确匹配（忽略大小写和首尾空白）- 快速路径，省去 LLM 调用
        if (string.Equals(actualOutput.Trim(), expectedOutput.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (true, 1.0, "Exact match");
        }

        // LLM-as-judge 语义评分（取代脆弱的字符串包含匹配 - "巴黎" vs "答案是巴黎" 应判等价）
        var judged = await TryLlmJudgeAsync(input, actualOutput, expectedOutput, ct);
        if (judged != null)
        {
            return judged.Value;
        }

        // LLM 不可用或解析失败 → 回退到字符串匹配
        return FallbackStringMatch(actualOutput, expectedOutput);
    }

    /// <summary>
    /// 用 LLM 作为评判，对实际输出 vs 期望输出做 0.0-1.0 语义评分。
    /// 返回 null 表示 LLM 不可用/响应无法解析，由调用方回退。
    /// </summary>
    private async Task<(bool Passed, double Score, string Reason)?> TryLlmJudgeAsync(
        string input, string actualOutput, string expectedOutput, CancellationToken ct)
    {
        const string systemPrompt =
            "You are a strict evaluation judge. Given the user input, the expected output, and the AI's actual output, " +
            "score how well the actual output satisfies the expected output's intent and content on a scale from 0.0 to 1.0. " +
            "Semantically equivalent answers should score high even if the wording differs. " +
            "Respond with ONLY a compact JSON object: {\"score\": <number 0.0-1.0>, \"pass\": <true|false>, \"reason\": \"<brief>\"}.";

        var userMessage = $"Input:\n{input}\n\nExpected output:\n{expectedOutput}\n\nActual output:\n{actualOutput}";

        try
        {
            var response = await _aiUtility.ExecuteAsync(systemPrompt, userMessage, options: null, ct);
            if (string.IsNullOrWhiteSpace(response))
            {
                return null; // LLM 不可用 → 调用方回退
            }

            return ParseJudgeResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM-as-judge evaluation failed, falling back to string match");
            return null;
        }
    }

    /// <summary>解析 LLM 评判响应 {"score","pass","reason"}；无法解析返回 null。</summary>
    private static (bool Passed, double Score, string Reason)? ParseJudgeResponse(string response)
    {
        var json = ExtractJsonObject(response);
        if (json == null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var score = root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
                ? Math.Clamp(s.GetDouble(), 0.0, 1.0)
                : 0.0;
            var pass = root.TryGetProperty("pass", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? p.GetBoolean()
                : score >= 0.7;
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;

            return (pass, score, $"LLM judge: {reason ?? "no reason given"}");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>从 LLM 响应中提取第一个 JSON 对象（容忍 markdown 围栏或额外文本）。</summary>
    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    /// <summary>LLM 不可用时的字符串匹配回退（保留旧的近似评分语义）。</summary>
    private static (bool Passed, double Score, string Reason) FallbackStringMatch(string actualOutput, string expectedOutput)
    {
        if (actualOutput.Contains(expectedOutput, StringComparison.OrdinalIgnoreCase))
        {
            return (true, 0.8, "Contains expected output (string-match fallback)");
        }

        if (expectedOutput.Contains(actualOutput, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(actualOutput))
        {
            return (false, 0.4, "Actual output is a subset of expected (string-match fallback)");
        }

        return (false, 0.0, "Output does not match expected (string-match fallback)");
    }
}
