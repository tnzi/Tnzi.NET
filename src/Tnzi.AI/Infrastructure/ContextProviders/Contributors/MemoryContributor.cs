
namespace Tnzi.AI.Infrastructure.ContextProviders.Contributors;

/// <summary>
/// 持久化记忆上下文贡献者
/// </summary>
internal sealed class MemoryContributor : IContextProviderContributor
{
    private readonly IMemoryStore _memoryStore;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly IMemoryConsolidator? _memoryConsolidator;
    private readonly ILoggerFactory _loggerFactory;

    public int Order => ContextProviderOrders.Memory;

    public MemoryContributor(
        IMemoryStore memoryStore,
        IChatClientFactory chatClientFactory,
        IOptionsMonitor<AIOptions> options,
        IAgentExecutionContextAccessor executionContextAccessor,
        ILoggerFactory loggerFactory,
        IMemoryConsolidator? memoryConsolidator = null)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _loggerFactory = Check.NotNull(loggerFactory);
        _memoryConsolidator = memoryConsolidator;
    }

    public IContextProvider? TryCreate(ContextProviderCreationContext context)
    {
        if (!_options.CurrentValue.ContextProviders.Memory.Enabled) return null;

        try
        {
            var memoryOptions = _options.CurrentValue.ContextProviders.Memory;
            var scope = MemoryScopeResolver.BuildLocalScope(
                memoryOptions.DefaultScope,
                memoryOptions.EnableUserIsolation,
                memoryOptions.EnableAgentIsolation,
                context.UserId,
                context.AgentId);

            // Agent-bound 范围：绑定到当前 Agent 的记忆（通过结构化 AgentId 列检索），
            // 与当前用户无关，确保 headless 运行也能加载（如管理员为该 Agent 预置的人设/知识记忆）。
            var agentBoundScope = context.AgentId.HasValue
                ? MemoryScope.ForAgent(context.AgentId.Value, memoryOptions.DefaultScope)
                : null;

            var logger = _loggerFactory.CreateLogger<MemoryContextProvider>();
            return new MemoryContextProvider(
                _memoryStore,
                scope,
                logger,
                _chatClientFactory,
                memoryOptions,
                _memoryConsolidator,
                _executionContextAccessor,
                agentBoundScope);
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<MemoryContributor>()
                .LogError(ex, "Failed to create MemoryContextProvider");
            return null;
        }
    }
}
