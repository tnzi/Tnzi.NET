namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 文本搜索上下文提供器 - 实现 RAG 功能
/// </summary>
/// <remarks>
/// <para>
/// 此类实现 IContextProvider，用于在 AI 调用前注入检索到的相关文档内容。
/// 支持两种搜索模式：
/// <list type="bullet">
/// <item><description>BeforeAIInvoke - 每次调用前自动执行搜索并注入结果</description></item>
/// <item><description>OnDemandFunctionCalling - 将搜索暴露为工具，由模型按需调用</description></item>
/// </list>
/// </para>
/// <para>
/// 底层使用 ITextSearchService 执行实际搜索，用户需要注册自己的实现来连接到向量存储。
/// </para>
/// </remarks>
public sealed class TextSearchProvider : IContextProvider
{
    private const string DefaultContextPrompt = "## Additional Context\nConsider the following information from source documents when responding to the user:";
    private const string DefaultCitationsPrompt = "Include citations to the source document with document name and link if available.";
    private const string DefaultFunctionToolName = "SearchKnowledge";
    private const string DefaultFunctionToolDescription = "Search for additional information from the knowledge base to help answer the user's question.";

    private readonly ITextSearchService _textSearchService;
    private readonly TextSearchOptions _options;
    private readonly ILogger<TextSearchProvider> _logger;
    private readonly AITool[] _tools;
    private readonly IReadOnlyList<Guid>? _knowledgeBaseIds;

    /// <summary>
    /// 初始化 TextSearchProvider
    /// </summary>
    /// <param name="textSearchService">文本检索服务（RAG 抽象）</param>
    /// <param name="options">检索配置（Top-K、阈值、注入模板等）</param>
    /// <param name="logger">日志</param>
    /// <param name="knowledgeBaseIds">该 Agent 分配的知识库 ID（非空时检索仅限这些知识库）</param>
    public TextSearchProvider(
        ITextSearchService textSearchService,
        TextSearchOptions options,
        ILogger<TextSearchProvider> logger,
        IReadOnlyList<Guid>? knowledgeBaseIds = null)
    {
        _textSearchService = Check.NotNull(textSearchService);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _knowledgeBaseIds = knowledgeBaseIds is { Count: > 0 } ? knowledgeBaseIds : null;

        // 创建按需搜索工具（仅在 OnDemandFunctionCalling 模式下使用）
        _tools =
        [
            AIFunctionFactory.Create(
                SearchAsync,
                name: DefaultFunctionToolName,
                description: DefaultFunctionToolDescription)
        ];
    }

    /// <inheritdoc />
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        // 如果是按需模式，暴露搜索工具
        if (_options.SearchTime == ContextSearchTime.OnDemandFunctionCalling)
        {
            return new ContextInjection { Tools = _tools.ToList() };
        }

        try
        {
            // 构建搜索输入：最近消息 + 当前请求消息
            var inputText = BuildSearchInput(messages);
            if (string.IsNullOrWhiteSpace(inputText))
            {
                return ContextInjection.Empty;
            }

            // 执行搜索（按 Agent 分配的知识库范围过滤）
            var results = await _textSearchService.SearchAsync(
                inputText,
                BuildFilter(),
                maxResults: 5,
                ct: ct);

            var resultList = results.ToList();
            if (resultList.Count == 0)
            {
                return ContextInjection.Empty;
            }

            _logger.LogDebug(
                "TextSearchProvider retrieved {Count} search results",
                resultList.Count);

            // 格式化结果
            var formatted = FormatResults(resultList);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                return ContextInjection.Empty;
            }

            // 构建结构化 Citation 列表
            var citations = resultList.Select(r => new CitationDto
            {
                SourceName = r.SourceName,
                SourceLink = r.SourceLink,
                Text = r.Text,
                Score = r.Score
            }).ToList();

            return new ContextInjection
            {
                Messages = [new ChatMessage(ChatRole.System, formatted)],
                Citations = citations
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TextSearchProvider failed to search");
            return ContextInjection.Empty;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 不需要跨轮状态：检索查询里的「最近消息」取自本轮交来的历史（见 <see cref="BuildSearchInput"/>）。
    /// 框架按请求新建实例，记在实例上的上一轮消息永远读不到；同一实例被复用时又会与历史重复。
    /// </remarks>
    public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// 供模型按需调用的搜索方法
    /// </summary>
    internal async Task<string> SearchAsync(string userQuestion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userQuestion))
        {
            return string.Empty;
        }

        var results = await _textSearchService.SearchAsync(userQuestion, BuildFilter(), maxResults: 5, ct: cancellationToken);
        var resultList = results.ToList();

        _logger.LogDebug(
            "TextSearchProvider (on-demand) retrieved {Count} search results for query length {QueryLength}",
            resultList.Count, userQuestion.Length);

        return FormatResults(resultList);
    }

    /// <summary>构建按 Agent 分配知识库范围过滤的检索条件（无分配时返回 null，RAG 实现据此答空而不是跨库检索）。</summary>
    private TextSearchFilter? BuildFilter()
        => _knowledgeBaseIds is { Count: > 0 }
            ? new TextSearchFilter { KnowledgeBaseIds = _knowledgeBaseIds.ToList() }
            : null;

    /// <summary>
    /// 构建搜索输入文本：以最新一条用户消息为准，前面最多带 <see cref="TextSearchOptions.RecentMessageMemoryLimit"/>
    /// 条最近的用户 / 助手消息作上下文（追问「那欧盟客户呢？」要靠上一轮才知道在问什么）。
    /// </summary>
    /// <remarks>
    /// 传进来的是整段已加载的历史（最多 <c>History.Store.MaxLoadedMessages</c> 条）+ 本轮用户消息。
    /// 整段拼起来当查询，检索会被旧话题带走，长线程还会超出嵌入模型的输入上限。
    /// 没有用户消息（例如只有系统消息）时返回空串，不做检索。
    /// </remarks>
    private string BuildSearchInput(IReadOnlyList<ChatMessage> requestMessages)
    {
        var conversation = requestMessages
            .Where(m => (m.Role == ChatRole.User || m.Role == ChatRole.Assistant) && !string.IsNullOrWhiteSpace(m.Text))
            .ToList();
        var currentIndex = conversation.FindLastIndex(m => m.Role == ChatRole.User);
        if (currentIndex < 0)
        {
            return string.Empty;
        }

        var limit = Math.Max(0, _options.RecentMessageMemoryLimit);
        var parts = conversation
            .Take(currentIndex)
            .Skip(Math.Max(0, currentIndex - limit))
            .Select(m => m.Text)
            .Append(conversation[currentIndex].Text);

        return string.Join('\n', parts);
    }

    /// <summary>
    /// 格式化搜索结果
    /// </summary>
    private string FormatResults(List<TextSearchResult> results)
    {
        if (results.Count == 0)
        {
            return string.Empty;
        }

        var contextPrompt = _options.ContextPrompt ?? DefaultContextPrompt;
        var citationsPrompt = _options.CitationsPrompt ?? DefaultCitationsPrompt;

        var sb = new StringBuilder();
        sb.AppendLine(contextPrompt);

        foreach (var result in results)
        {
            if (!string.IsNullOrWhiteSpace(result.SourceName))
            {
                sb.AppendLine($"SourceDocName: {result.SourceName}");
            }
            if (!string.IsNullOrWhiteSpace(result.SourceLink))
            {
                sb.AppendLine($"SourceDocLink: {result.SourceLink}");
            }
            sb.AppendLine($"Contents: {result.Text}");
            sb.AppendLine("----");
        }

        sb.AppendLine(citationsPrompt);
        return sb.ToString();
    }
}
