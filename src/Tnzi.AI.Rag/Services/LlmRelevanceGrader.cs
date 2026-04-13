namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 基于 LLM 的相关性评分器 — 使用 LLM 评估检索结果与查询的相关性
/// </summary>
/// <remarks>
/// <para>
/// 通过 IChatClientFactory 获取 LLM 客户端，对每个搜索结果并行调用 LLM 评估相关性。
/// 采用 fail-open 策略：当单个结果的 LLM 调用失败时，标记该结果为相关以避免误过滤。
/// </para>
/// <para>
/// 通过 <see cref="AIRagOptions"/> 配置 LLM 提供商、模型和相关性阈值。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "LLM relevance grading is in preview")]
public class LlmRelevanceGrader : IRelevanceGrader
{
    private const string GradePromptTemplate =
        "Is this document relevant to the query? Query: {0}\nDocument: {1}\nRespond with ONLY 'YES' or 'NO'";

    private readonly IChatClientFactory _chatClientFactory;
    private readonly AIRagOptions _options;
    private readonly ILogger<LlmRelevanceGrader> _logger;

    public LlmRelevanceGrader(IChatClientFactory chatClientFactory, IOptions<AIRagOptions> options, ILogger<LlmRelevanceGrader> logger)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<List<GradedResult>> GradeAsync(string query, List<TextSearchResult> results, CancellationToken ct = default)
    {
        if (results.Count == 0)
        {
            return [];
        }

        // 并行评估所有结果
        var gradeTasks = results.Select(result => GradeResultAsync(query, result, ct));
        var graded = await Task.WhenAll(gradeTasks);

        return graded.ToList();
    }

    /// <summary>
    /// 评估单个搜索结果的相关性
    /// </summary>
    private async Task<GradedResult> GradeResultAsync(string query, TextSearchResult result, CancellationToken ct)
    {
        try
        {
            var chatClient = _chatClientFactory.GetChatClient(_options.RelevanceGradeProvider, _options.RelevanceGradeModel);

            var prompt = string.Format(GradePromptTemplate, query, result.Text);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, prompt)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var responseText = response.Text?.Trim() ?? string.Empty;

            var isRelevant = responseText.StartsWith("YES", StringComparison.OrdinalIgnoreCase);
            var relevanceScore = isRelevant ? 1.0 : 0.0;

            return new GradedResult
            {
                Result = result,
                RelevanceScore = relevanceScore,
                IsRelevant = isRelevant
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // fail-open: 标记为相关
            _logger.LogWarning(ex, "LLM relevance grading failed for result, marking as relevant (fail-open)");
            return new GradedResult
            {
                Result = result,
                RelevanceScore = result.Score ?? 1.0,
                IsRelevant = true
            };
        }
    }
}
