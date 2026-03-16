
namespace Tnzi.AI.Engine;

/// <summary>
/// AgentExecutorOptions 构建器 — 负责构建 HistoryReducer 和 ContextProvider
/// </summary>
/// <remarks>
/// 从 AgentFactory 提取，职责为：
/// - 合并调用方传入的 Options 与方法参数
/// - 根据配置创建 HistoryReducer（Prune / Summarize）
/// - 根据配置创建 ContextProvider（TextSearch / ChatHistoryMemory / Memory / Skills）
/// </remarks>
public class AgentExecutorOptionsBuilder
{
    private readonly IOptions<AIOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ITextSearchService _textSearchService;
    private readonly IMemoryStore _memoryStore;
    private readonly IEntityMemoryStore _entityMemoryStore;
    private readonly LlmEntityExtractor _entityExtractor;
    private readonly IEnumerable<IToolExecutionMiddleware> _middlewares;
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillTemplateEngine _skillTemplateEngine;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ProjectContextProvider _projectContextProvider;
    private readonly IAgentExecutionContextAccessor _executionContextAccessor;
    private readonly IMemoryConsolidator? _memoryConsolidator;
    private readonly ICurrentUser? _currentUser;
    private readonly ILogger<AgentExecutorOptionsBuilder> _logger;

    public AgentExecutorOptionsBuilder(
        IOptions<AIOptions> options,
        ILoggerFactory loggerFactory,
        IChatClientFactory chatClientFactory,
        ITextSearchService textSearchService,
        IMemoryStore memoryStore,
        IEntityMemoryStore entityMemoryStore,
        LlmEntityExtractor entityExtractor,
        IEnumerable<IToolExecutionMiddleware> middlewares,
        ISkillRegistry skillRegistry,
        ISkillTemplateEngine skillTemplateEngine,
        ITokenEstimator tokenEstimator,
        ProjectContextProvider projectContextProvider,
        IAgentExecutionContextAccessor executionContextAccessor,
        ILogger<AgentExecutorOptionsBuilder> logger,
        ICurrentUser? currentUser = null,
        IMemoryConsolidator? memoryConsolidator = null)
    {
        _options = Check.NotNull(options);
        _loggerFactory = Check.NotNull(loggerFactory);
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _textSearchService = Check.NotNull(textSearchService);
        _memoryStore = Check.NotNull(memoryStore);
        _entityMemoryStore = Check.NotNull(entityMemoryStore);
        _entityExtractor = Check.NotNull(entityExtractor);
        _middlewares = Check.NotNull(middlewares);
        _skillRegistry = Check.NotNull(skillRegistry);
        _skillTemplateEngine = Check.NotNull(skillTemplateEngine);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _projectContextProvider = Check.NotNull(projectContextProvider);
        _executionContextAccessor = Check.NotNull(executionContextAccessor);
        _logger = Check.NotNull(logger);
        _currentUser = currentUser;
        _memoryConsolidator = memoryConsolidator;
    }

    /// <summary>
    /// 构建 AgentExecutorOptions，合并调用方传入的选项与方法参数
    /// </summary>
    public AgentExecutorOptions Build(
        AgentExecutorOptions? callerOptions,
        string? name,
        string? instructions,
        IList<AITool>? tools,
        double? temperature,
        int? maxTokens,
        Guid? agentId = null)
    {
        // 从 DI 解析已注册的中间件
        var middlewares = ResolveMiddlewares();

        if (callerOptions != null)
        {
            // 调用方已传入 options，基于副本合并参数
            return new AgentExecutorOptions
            {
                Name = callerOptions.Name ?? name ?? "Agent",
                Instructions = !string.IsNullOrWhiteSpace(instructions) ? instructions : callerOptions.Instructions,
                Tools = tools is { Count: > 0 } ? tools : callerOptions.Tools,
                Temperature = temperature.HasValue ? (float)temperature.Value : callerOptions.Temperature,
                MaxOutputTokens = maxTokens ?? callerOptions.MaxOutputTokens,
                MaxToolIterations = callerOptions.MaxToolIterations,
                HistoryReducer = callerOptions.HistoryReducer,
                ContextProvider = callerOptions.ContextProvider,
                Middlewares = callerOptions.Middlewares ?? middlewares
            };
        }

        // 调用方未传入 options，根据配置自动创建 HistoryReducer 和 ContextProvider
        var historyReducer = CreateHistoryReducer();
        var contextProvider = CreateContextProvider(agentId);

        return new AgentExecutorOptions
        {
            Name = name ?? "Agent",
            Instructions = instructions,
            Tools = tools is { Count: > 0 } ? tools : null,
            Temperature = temperature.HasValue ? (float)temperature.Value : null,
            MaxOutputTokens = maxTokens,
            HistoryReducer = historyReducer,
            ContextProvider = contextProvider,
            Middlewares = middlewares
        };
    }

    /// <summary>
    /// 根据配置创建 IHistoryReducer
    /// </summary>
    private IHistoryReducer? CreateHistoryReducer()
    {
        var historyConfig = _options.Value.History;
        var reductionMode = historyConfig.Reduction.Mode;

        if (reductionMode == HistoryReductionMode.None)
        {
            return null;
        }

        IHistoryReducer? reducer = reductionMode switch
        {
            HistoryReductionMode.Prune => CreatePruneChatReducer(),
            HistoryReductionMode.Summarize => CreateSummarizeChatReducer(),
            _ => null
        };

        if (reducer != null)
        {
            _logger.LogDebug("History reducer created with mode {Mode}", reductionMode);
        }
        return reducer;
    }

    /// <summary>
    /// 创建 PruneChatReducer
    /// </summary>
    private IHistoryReducer CreatePruneChatReducer()
    {
        var pruneOptions = _options.Value.History.Reduction.Prune;
        var logger = _loggerFactory.CreateLogger<PruneChatReducer>();

        _logger.LogDebug(
            "Creating PruneChatReducer: KeepLastTurns={KeepLastTurns}, DropToolOutputsOlderThan={DropToolOutputsOlderThan}",
            pruneOptions.KeepLastTurns, pruneOptions.DropToolOutputsOlderThan);

        return new PruneChatReducer(pruneOptions, logger);
    }

    /// <summary>
    /// 创建 SummarizeChatReducer
    /// </summary>
    private IHistoryReducer? CreateSummarizeChatReducer()
    {
        var summarizeOptions = _options.Value.History.Reduction.Summarize;
        var logger = _loggerFactory.CreateLogger<SummarizeChatReducer>();

        try
        {
            var provName = summarizeOptions.Provider ?? _options.Value.DefaultProvider;
            var chatClient = _chatClientFactory.GetChatClient(provName, summarizeOptions.SummaryModelId);

            _logger.LogDebug(
                "Creating SummarizeChatReducer: MessageThreshold={MessageThreshold}, TokenThreshold={TokenThreshold}, KeepRecentTurns={KeepRecentTurns}",
                summarizeOptions.MessageThreshold, summarizeOptions.TokenThreshold, summarizeOptions.KeepRecentTurns);

            return new SummarizeChatReducer(summarizeOptions, chatClient, _tokenEstimator, logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create SummarizeChatReducer. Falling back to no reduction.");
            return null;
        }
    }

    /// <summary>
    /// 根据配置创建 IContextProvider（组合多个子 Provider）
    /// </summary>
    private IContextProvider? CreateContextProvider(Guid? agentId = null)
    {
        var contextConfig = _options.Value.ContextProviders;
        if (!contextConfig.Enabled)
        {
            return null;
        }

        var compositeLogger = _loggerFactory.CreateLogger<CompositeContextProvider>();
        var compositeProvider = new CompositeContextProvider(compositeLogger, _options, _tokenEstimator);

        if (contextConfig.TextSearch.Enabled)
        {
            var provider = CreateTextSearchProvider();
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (contextConfig.ChatHistoryMemory.Enabled)
        {
            var provider = CreateChatHistoryMemoryProvider(agentId);
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (contextConfig.Skills.Enabled)
        {
            var provider = CreateSkillContextProvider();
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (contextConfig.Memory.Enabled)
        {
            var provider = CreateMemoryContextProvider(contextConfig.Memory.DefaultScope);
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (contextConfig.EntityMemory.Enabled)
        {
            var provider = CreateEntityMemoryContextProvider(agentId);
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (contextConfig.ProjectContext.Enabled)
        {
            compositeProvider.AddProvider(_projectContextProvider);
        }

        if (compositeProvider.ProviderCount == 0)
        {
            _logger.LogDebug("No context providers configured, skipping ContextProvider creation");
            return null;
        }

        _logger.LogDebug("Created CompositeContextProvider with {ProviderCount} sub-providers", compositeProvider.ProviderCount);
        return compositeProvider;
    }

    private IContextProvider? CreateTextSearchProvider()
    {
        try
        {
            var logger = _loggerFactory.CreateLogger<TextSearchProvider>();
            return new TextSearchProvider(_textSearchService, _options.Value.ContextProviders.TextSearch, logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TextSearchProvider");
            return null;
        }
    }

    private IContextProvider? CreateChatHistoryMemoryProvider(Guid? agentId = null)
    {
        try
        {
            var chatHistoryOptions = _options.Value.ContextProviders.ChatHistoryMemory;
            var scope = new ChatHistoryMemoryScope
            {
                UserId = ResolveCurrentUserId()?.ToString(),
                AgentId = agentId?.ToString()
            };
            var logger = _loggerFactory.CreateLogger<ChatHistoryMemoryProvider>();
            return new ChatHistoryMemoryProvider(_textSearchService, chatHistoryOptions, scope, logger, _executionContextAccessor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ChatHistoryMemoryProvider");
            return null;
        }
    }

    private IContextProvider? CreateMemoryContextProvider(string defaultScope)
    {
        try
        {
            var memoryOptions = _options.Value.ContextProviders.Memory;
            var userId = memoryOptions.EnableUserIsolation ? ResolveCurrentUserId() : null;
            var scope = new MemoryScope(defaultScope, userId);
            var logger = _loggerFactory.CreateLogger<MemoryContextProvider>();
            return new MemoryContextProvider(_memoryStore, scope, logger, _chatClientFactory, memoryOptions, _memoryConsolidator);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MemoryContextProvider");
            return null;
        }
    }

    /// <summary>
    /// 从 DI 解析已注册的工具执行中间件
    /// </summary>
    private IReadOnlyList<IToolExecutionMiddleware>? ResolveMiddlewares()
    {
        var middlewares = _middlewares.ToList();
        return middlewares.Count > 0 ? middlewares : null;
    }

    private IContextProvider? CreateEntityMemoryContextProvider(Guid? agentId = null)
    {
        try
        {
            var entityMemoryOptions = _options.Value.ContextProviders.EntityMemory;
            var logger = _loggerFactory.CreateLogger<EntityMemoryContextProvider>();

            return new EntityMemoryContextProvider(
                _entityMemoryStore,
                entityMemoryOptions,
                _entityExtractor,
                logger,
                _currentUser,
                agentId,
                executionContextAccessor: _executionContextAccessor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create EntityMemoryContextProvider");
            return null;
        }
    }

    private IContextProvider? CreateSkillContextProvider()
    {
        try
        {
            var skillsOptions = _options.Value.ContextProviders.Skills;
            var logger = _loggerFactory.CreateLogger<SkillContextProvider>();
            return new SkillContextProvider(_skillRegistry, _skillTemplateEngine, skillsOptions, logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create SkillContextProvider");
            return null;
        }
    }

    private Guid? ResolveCurrentUserId()
    {
        return _currentUser?.Id ?? _executionContextAccessor.CurrentRequest?.UserId;
    }
}
