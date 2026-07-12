
namespace Tnzi.AI.Infrastructure.ContextProviders.Contributors;

/// <summary>
/// 实体记忆上下文贡献者
/// </summary>
internal sealed class EntityMemoryContributor : IContextProviderContributor
{
    private readonly IEntityMemoryStore _entityMemoryStore;
    private readonly LlmEntityExtractor _entityExtractor;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly ICurrentUser? _currentUser;
    private readonly ILoggerFactory _loggerFactory;

    public int Order => ContextProviderOrders.Memory + 1;

    public EntityMemoryContributor(
        IEntityMemoryStore entityMemoryStore,
        LlmEntityExtractor entityExtractor,
        IOptionsMonitor<AIOptions> options,
        IAgentExecutionContextAccessor executionContextAccessor,
        ILoggerFactory loggerFactory,
        ICurrentUser? currentUser = null)
    {
        _entityMemoryStore = Check.NotNull(entityMemoryStore);
        _entityExtractor = Check.NotNull(entityExtractor);
        _options = Check.NotNull(options);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _loggerFactory = Check.NotNull(loggerFactory);
        _currentUser = currentUser;
    }

    public IContextProvider? TryCreate(ContextProviderCreationContext context)
    {
        if (!_options.CurrentValue.ContextProviders.EntityMemory.Enabled) return null;

        try
        {
            var entityMemoryOptions = _options.CurrentValue.ContextProviders.EntityMemory;
            var logger = _loggerFactory.CreateLogger<EntityMemoryContextProvider>();
            return new EntityMemoryContextProvider(
                _entityMemoryStore,
                entityMemoryOptions,
                _entityExtractor,
                logger,
                _currentUser,
                context.AgentId,
                executionContextAccessor: _executionContextAccessor);
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<EntityMemoryContributor>()
                .LogError(ex, "Failed to create EntityMemoryContextProvider");
            return null;
        }
    }
}
