
namespace Tnzi.AI.Infrastructure.ContextProviders.Contributors;

/// <summary>
/// 聊天历史记忆上下文贡献者
/// </summary>
internal sealed class ChatHistoryMemoryContributor : IContextProviderContributor
{
    private readonly ITextSearchService _textSearchService;
    private readonly IOptions<AIOptions> _options;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly ILoggerFactory _loggerFactory;

    public int Order => ContextProviderOrders.Rag + 1;

    public ChatHistoryMemoryContributor(
        ITextSearchService textSearchService,
        IOptions<AIOptions> options,
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
        if (!_options.Value.ContextProviders.ChatHistoryMemory.Enabled) return null;

        try
        {
            var chatHistoryOptions = _options.Value.ContextProviders.ChatHistoryMemory;
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
