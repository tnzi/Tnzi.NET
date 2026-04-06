
namespace Tnzi.AI.Infrastructure.ContextProviders.Contributors;

/// <summary>
/// 持久化记忆上下文贡献者
/// </summary>
internal sealed class MemoryContributor : IContextProviderContributor
{
    private readonly IMemoryStore _memoryStore;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IOptions<AIOptions> _options;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly IMemoryConsolidator? _memoryConsolidator;
    private readonly ICurrentUser? _currentUser;
    private readonly ILoggerFactory _loggerFactory;

    public int Order => ContextProviderOrders.Memory;

    public MemoryContributor(
        IMemoryStore memoryStore,
        IChatClientFactory chatClientFactory,
        IOptions<AIOptions> options,
        IAgentExecutionContextAccessor executionContextAccessor,
        ILoggerFactory loggerFactory,
        IMemoryConsolidator? memoryConsolidator = null,
        ICurrentUser? currentUser = null)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _loggerFactory = Check.NotNull(loggerFactory);
        _memoryConsolidator = memoryConsolidator;
        _currentUser = currentUser;
    }

    public IContextProvider? TryCreate(ContextProviderCreationContext context)
    {
        if (!_options.Value.ContextProviders.Memory.Enabled) return null;

        try
        {
            var memoryOptions = _options.Value.ContextProviders.Memory;
            var scope = MemoryScopeResolver.BuildLocalScope(
                memoryOptions.DefaultScope,
                memoryOptions.EnableUserIsolation,
                memoryOptions.EnableAgentIsolation,
                context.UserId,
                context.AgentId);
            var logger = _loggerFactory.CreateLogger<MemoryContextProvider>();
            return new MemoryContextProvider(
                _memoryStore,
                scope,
                logger,
                _chatClientFactory,
                memoryOptions,
                _memoryConsolidator,
                _executionContextAccessor);
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<MemoryContributor>()
                .LogError(ex, "Failed to create MemoryContextProvider");
            return null;
        }
    }
}
