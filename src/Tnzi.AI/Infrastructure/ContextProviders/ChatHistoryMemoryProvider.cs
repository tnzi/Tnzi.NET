
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
/// 注意：此功能需要用户注册 ITextSearchService 实现来提供文本搜索能力。
/// 当前为轻量实现，使用 ITextSearchService 执行搜索。
/// 用户可以注册自己的 IContextProvider 实现来替代此默认行为。
/// </para>
/// </remarks>
public sealed class ChatHistoryMemoryProvider : IContextProvider
{
    private readonly ITextSearchService _textSearchService;
    private readonly ChatHistoryMemoryOptions _options;
    private readonly ChatHistoryMemoryScope _scope;
    private readonly ILogger<ChatHistoryMemoryProvider> _logger;

    /// <summary>
    /// 初始化 ChatHistoryMemoryProvider
    /// </summary>
    public ChatHistoryMemoryProvider(
        ITextSearchService textSearchService,
        ChatHistoryMemoryOptions options,
        ChatHistoryMemoryScope scope,
        ILogger<ChatHistoryMemoryProvider> logger)
    {
        _textSearchService = Check.NotNull(textSearchService);
        _options = Check.NotNull(options);
        _scope = Check.NotNull(scope);
        _logger = Check.NotNull(logger);
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

        try
        {
            // 提取最后一条用户消息作为搜索查询
            var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
            var query = lastUserMessage?.Text;
            if (string.IsNullOrWhiteSpace(query))
            {
                return ContextInjection.Empty;
            }

            // 执行语义搜索
            var maxResults = _options.MaxResults > 0 ? _options.MaxResults : 5;
            var results = await _textSearchService.SearchAsync(query, maxResults: maxResults, ct: ct);
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
    /// 调用完成后的清理（当前无操作）
    /// </summary>
    public Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// 聊天历史记忆范围
/// </summary>
/// <remarks>
/// 用于限定聊天历史的存储和搜索范围
/// </remarks>
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

    /// <summary>
    /// 搜索范围（可选，默认与存储范围相同）
    /// </summary>
    public ChatHistoryMemoryScope? SearchScope { get; set; }
}
