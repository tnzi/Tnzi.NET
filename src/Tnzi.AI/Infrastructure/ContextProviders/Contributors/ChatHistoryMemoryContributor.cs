
namespace Tnzi.AI.Infrastructure.ContextProviders.Contributors;

/// <summary>
/// 聊天历史记忆上下文贡献者
/// </summary>
internal sealed class ChatHistoryMemoryContributor : IContextProviderContributor
{
    // 开关可在运行时热改，只在启动期告警不够；进程内只告警一次，免得每次运行刷一条
    private static int _unsupportedWarningLogged;

    private readonly ITextSearchService _textSearchService;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly ILoggerFactory _loggerFactory;

    public int Order => ContextProviderOrders.Rag + 1;

    public ChatHistoryMemoryContributor(
        ITextSearchService textSearchService,
        IOptionsMonitor<AIOptions> options,
        IAgentExecutionContextAccessor executionContextAccessor,
        ILoggerFactory loggerFactory)
    {
        _textSearchService = Check.NotNull(textSearchService);
        _options = Check.NotNull(options);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _loggerFactory = Check.NotNull(loggerFactory);
    }

    public IContextProvider? TryCreate(ContextProviderCreationContext context)
    {
        if (!_options.CurrentValue.ContextProviders.ChatHistoryMemory.Enabled) return null;

        // 失败关闭：检索实现不按用户 / 线程限定结果时（内置的知识库检索就是这样），
        // 「历史对话」检索会变成跨全部知识库的检索，绕过 Agent 的知识库授权
        if (!_textSearchService.SupportsConversationScope)
        {
            if (Interlocked.Exchange(ref _unsupportedWarningLogged, 1) == 0)
            {
                _loggerFactory.CreateLogger<ChatHistoryMemoryContributor>().LogWarning(
                    "Chat history memory is enabled but the registered ITextSearchService ({Implementation}) does not support conversation-scoped search; the provider is disabled. Register a search service that indexes chat history and honours UserId/ThreadId filters.",
                    _textSearchService.GetType().Name);
            }
            return null;
        }

        try
        {
            var chatHistoryOptions = _options.CurrentValue.ContextProviders.ChatHistoryMemory;
            var scope = new ChatHistoryMemoryScope
            {
                UserId = context.UserId?.ToString(),
                AgentId = context.AgentId?.ToString()
            };
            var logger = _loggerFactory.CreateLogger<ChatHistoryMemoryProvider>();
            return new ChatHistoryMemoryProvider(_textSearchService, chatHistoryOptions, scope, logger, _executionContextAccessor);
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<ChatHistoryMemoryContributor>()
                .LogError(ex, "Failed to create ChatHistoryMemoryProvider");
            return null;
        }
    }
}
