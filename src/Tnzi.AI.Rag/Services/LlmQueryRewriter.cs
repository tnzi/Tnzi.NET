namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 基于 LLM 的查询改写器 - 使用 LLM 将用户查询改写为更适合语义搜索的形式
/// </summary>
/// <remarks>
/// <para>
/// 通过 IChatClientFactory 获取 LLM 客户端，使用系统提示词引导 LLM 改写查询。
/// 采用 fail-open 策略：当 LLM 调用失败时，返回原始查询以避免阻塞搜索流程。
/// </para>
/// <para>
/// 通过 <see cref="AIRagOptions"/> 配置 LLM 提供商和模型。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "LLM query rewriting is in preview")]
public class LlmQueryRewriter : IQueryRewriter
{
    private const string SystemPrompt =
        "Rewrite the following search query to improve semantic search results. Return ONLY the rewritten query, nothing else.";

    private readonly IChatClientFactory _chatClientFactory;
    private readonly AIRagOptions _options;
    private readonly ILogger<LlmQueryRewriter> _logger;

    public LlmQueryRewriter(IChatClientFactory chatClientFactory, IOptions<AIRagOptions> options, ILogger<LlmQueryRewriter> logger)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<string> RewriteAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return query;
        }

        try
        {
            var chatClient = _chatClientFactory.GetChatClient(_options.QueryRewriteProvider, _options.QueryRewriteModel);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, query)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var rewritten = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(rewritten))
            {
                _logger.LogWarning("LLM query rewriter returned empty response, using original query");
                return query;
            }

            _logger.LogDebug("Query rewritten: '{Original}' -> '{Rewritten}'", query, rewritten);
            return rewritten;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // fail-open: 返回原始查询
            _logger.LogWarning(ex, "LLM query rewriting failed, using original query (fail-open)");
            return query;
        }
    }
}
