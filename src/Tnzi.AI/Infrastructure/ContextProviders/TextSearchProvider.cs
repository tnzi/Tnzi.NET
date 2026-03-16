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
    private readonly Queue<string> _recentMessagesText = new();

    /// <summary>
    /// 初始化 TextSearchProvider
    /// </summary>
    public TextSearchProvider(
        ITextSearchService textSearchService,
        TextSearchOptions options,
        ILogger<TextSearchProvider> logger)
    {
        _textSearchService = Check.NotNull(textSearchService);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);

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

            // 执行搜索
            var results = await _textSearchService.SearchAsync(
                inputText,
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
    public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        var limit = _options.RecentMessageMemoryLimit;
        if (limit <= 0)
        {
            return Task.CompletedTask; // 记忆功能已禁用
        }

        // 收集要记忆的消息（只保留用户和助手消息）
        var messagesToRemember = messages
            .Where(m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant)
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => m.Text!)
            .ToList();

        // 如果当前消息超过限制，只保留最近的
        if (messagesToRemember.Count > limit)
        {
            messagesToRemember = messagesToRemember.Skip(messagesToRemember.Count - limit).ToList();
        }

        // 添加到记忆队列
        foreach (var message in messagesToRemember)
        {
            _recentMessagesText.Enqueue(message);
        }

        // 保持队列在限制内
        while (_recentMessagesText.Count > limit)
        {
            _recentMessagesText.Dequeue();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 供模型按需调用的搜索方法
    /// </summary>
    internal async Task<string> SearchAsync(string userQuestion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userQuestion))
        {
            return string.Empty;
        }

        var results = await _textSearchService.SearchAsync(userQuestion, maxResults: 5, ct: cancellationToken);
        var resultList = results.ToList();

        _logger.LogDebug(
            "TextSearchProvider (on-demand) retrieved {Count} search results for query length {QueryLength}",
            resultList.Count, userQuestion.Length);

        return FormatResults(resultList);
    }

    /// <summary>
    /// 构建搜索输入文本
    /// </summary>
    private string BuildSearchInput(IEnumerable<ChatMessage> requestMessages)
    {
        var sb = new StringBuilder();

        // 添加最近记忆的消息
        foreach (var text in _recentMessagesText)
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }
            sb.Append(text);
        }

        // 添加当前请求消息
        foreach (var message in requestMessages)
        {
            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(message.Text);
            }
        }

        return sb.ToString();
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
