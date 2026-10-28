
namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 聊天历史记忆上下文提供器 - 基于向量存储的语义搜索
/// </summary>
/// <remarks>
/// <para>
/// 此提供器在 AI 调用前从向量存储中检索语义相关的历史消息作为额外上下文。
/// 支持两种搜索模式：
/// <list type="bullet">
/// <item><description>BeforeAIInvoke - 每次调用前自动执行搜索并注入结果</description></item>
/// <item><description>OnDemandFunctionCalling - 将搜索暴露为工具，由模型按需调用</description></item>
/// </list>
/// </para>
/// <para>
/// ★ 失败关闭：只在检索实现声明 <see cref="ITextSearchService.SupportsConversationScope"/>，且本次检索带得出
/// 用户或线程范围时才发起检索。框架内置的检索实现只认知识库范围、忽略用户 / 线程字段，拿它检索「历史对话」
/// 就是一次跨全部知识库的检索：绕过 Agent 的知识库授权，结果还被当作以前的对话注入。不带范围的检索同理。
/// 框架本身不索引聊天历史；要用这项能力，需要提供索引聊天历史并按上述字段限定结果的实现。
/// </para>
/// </remarks>
public sealed class ChatHistoryMemoryProvider : IContextProvider
{
    private readonly ITextSearchService _textSearchService;
    private readonly ChatHistoryMemoryOptions _options;
    private readonly ChatHistoryMemoryScope _scope;
    private readonly ILogger<ChatHistoryMemoryProvider> _logger;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;

    /// <summary>
    /// 初始化 ChatHistoryMemoryProvider
    /// </summary>
    public ChatHistoryMemoryProvider(
        ITextSearchService textSearchService,
        ChatHistoryMemoryOptions options,
        ChatHistoryMemoryScope scope,
        ILogger<ChatHistoryMemoryProvider> logger,
        IAgentExecutionContextAccessor? executionContextAccessor = null)
    {
        _textSearchService = Check.NotNull(textSearchService);
        _options = Check.NotNull(options);
        _scope = Check.NotNull(scope);
        _logger = Check.NotNull(logger);
        _executionContextAccessor = executionContextAccessor;
    }

    /// <summary>
    /// 在 AI 调用前检索相关的历史消息
    /// </summary>
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0)
        {
            return ContextInjection.Empty;
        }

        // OnDemand 模式: 注入搜索工具而非自动搜索
        if (_options.SearchTime == ContextSearchTime.OnDemandFunctionCalling)
        {
            return GetOnDemandInjection();
        }

        // BeforeAIInvoke 模式: 自动搜索并注入结果
        return await SearchAndInjectAsync(messages, ct);
    }

    /// <summary>
    /// 调用完成后的清理（当前无操作）
    /// </summary>
    public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 自动搜索并注入结果
    /// </summary>
    private async Task<ContextInjection> SearchAndInjectAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        try
        {
            // 提取最后一条用户消息作为搜索查询
            var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
            var query = lastUserMessage?.Text;
            if (string.IsNullOrWhiteSpace(query))
            {
                return ContextInjection.Empty;
            }

            // 构建过滤条件：给不出对话范围（或检索实现不按它限定结果）就不检索
            var filter = BuildConversationFilter();
            if (filter == null)
            {
                return ContextInjection.Empty;
            }

            // 执行语义搜索
            var maxResults = _options.MaxResults > 0 ? _options.MaxResults : 5;
            var results = await _textSearchService.SearchAsync(query, filter, maxResults: maxResults, ct: ct);
            var resultList = results.ToList();

            if (resultList.Count == 0)
            {
                return ContextInjection.Empty;
            }

            _logger.LogDebug(
                "ChatHistoryMemoryProvider retrieved {Count} relevant history items for scope {ScopeApp}/{ScopeAgent}",
                resultList.Count, _scope.ApplicationId, _scope.AgentId);

            // 格式化结果为上下文消息
            var contextPrompt = _options.ContextPrompt ?? "## Relevant Chat History\nThe following are relevant excerpts from previous conversations:";
            var sb = new StringBuilder();
            sb.AppendLine(contextPrompt);

            foreach (var result in resultList)
            {
                sb.AppendLine($"- {result.Text}");
            }

            return new ContextInjection
            {
                Messages = [new ChatMessage(ChatRole.System, sb.ToString())]
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve chat history memory context");
            return ContextInjection.Empty;
        }
    }

    /// <summary>
    /// OnDemand 模式: 将搜索暴露为工具
    /// </summary>
    private ContextInjection GetOnDemandInjection()
    {
        var searchTool = AIFunctionFactory.Create(
            SearchChatHistoryAsync,
            name: "search_chat_history",
            description: "Search relevant past conversations by keyword. Returns excerpts from previous chat history.");

        return new ContextInjection
        {
            Tools = [searchTool]
        };
    }

    /// <summary>
    /// 供 OnDemand 工具调用的搜索方法
    /// </summary>
    private async Task<string> SearchChatHistoryAsync(
        [Description("Search query to find relevant past conversations")] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Please provide a non-empty search query.";
        }

        var filter = BuildConversationFilter();
        if (filter == null)
        {
            return "Chat history search is not available in this conversation.";
        }

        try
        {
            var maxResults = _options.MaxResults > 0 ? _options.MaxResults : 5;
            var results = await _textSearchService.SearchAsync(query, filter, maxResults, cancellationToken);
            var resultList = results.ToList();

            if (resultList.Count == 0)
            {
                return $"No relevant chat history found for: {query}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {resultList.Count} relevant conversation(s):");
            sb.AppendLine();

            foreach (var result in resultList)
            {
                sb.AppendLine($"- {result.Text}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat history search failed for query: {Query}", query);
            return $"Search failed: {ex.Message}";
        }
    }

    /// <summary>
    /// 构建对话范围的 TextSearchFilter；检索不能被限定在某个用户或线程之内时返回 null（调用方据此不检索）。
    /// </summary>
    /// <remarks>
    /// 两个条件缺一不可：检索实现声明按这些字段限定结果（<see cref="ITextSearchService.SupportsConversationScope"/>），
    /// 且本次至少带得出用户或线程。只有 Agent / 集合名不算对话范围：同一 Agent 下所有用户的历史会混在一起。
    /// </remarks>
    private TextSearchFilter? BuildConversationFilter()
    {
        if (!_textSearchService.SupportsConversationScope)
        {
            return null;
        }

        var request = _executionContextAccessor?.CurrentRequest;
        var userId = request?.UserId?.ToString() ?? _scope.UserId;
        var agentId = request?.AgentId?.ToString() ?? _scope.AgentId;
        var threadId = request?.ThreadId?.ToString() ?? _scope.ThreadId;

        var filter = new TextSearchFilter
        {
            CollectionName = _options.CollectionName
        };

        if (Guid.TryParse(userId, out var parsedUserId))
            filter.UserId = parsedUserId;
        if (Guid.TryParse(agentId, out var parsedAgentId))
            filter.AgentId = parsedAgentId;
        if (!string.IsNullOrWhiteSpace(threadId))
            filter.ThreadId = threadId;

        return filter.UserId.HasValue || filter.ThreadId != null ? filter : null;
    }
}

/// <summary>
/// 聊天历史记忆范围
/// </summary>
public class ChatHistoryMemoryScope
{
    /// <summary>
    /// 应用程序 ID（可选）
    /// </summary>
    public string? ApplicationId { get; set; }

    /// <summary>
    /// Agent ID（可选）
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// 用户 ID（可选）
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 线程 ID（可选）
    /// </summary>
    public string? ThreadId { get; set; }
}
