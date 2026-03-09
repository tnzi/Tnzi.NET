
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
    private readonly SkillToolsProvider _skillToolsProvider;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ProjectContextProvider _projectContextProvider;
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
        SkillToolsProvider skillToolsProvider,
        ITokenEstimator tokenEstimator,
        ProjectContextProvider projectContextProvider,
        ILogger<AgentExecutorOptionsBuilder> logger,
        ICurrentUser? currentUser = null)
    {
        _options = Check.NotNull(options);
        _loggerFactory = Check.NotNull(loggerFactory);
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _textSearchService = Check.NotNull(textSearchService);
        _memoryStore = Check.NotNull(memoryStore);
        _entityMemoryStore = Check.NotNull(entityMemoryStore);
        _entityExtractor = Check.NotNull(entityExtractor);
        _middlewares = Check.NotNull(middlewares);
        _skillToolsProvider = Check.NotNull(skillToolsProvider);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _projectContextProvider = Check.NotNull(projectContextProvider);
        _logger = Check.NotNull(logger);
        _currentUser = currentUser;
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
        int? maxTokens)
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
        var contextProvider = CreateContextProvider();

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
        if (!historyConfig.Store.Enabled)
        {
            return null;
        }

        var reductionMode = historyConfig.Reduction.Mode;

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
    private IContextProvider? CreateContextProvider()
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
            var provider = CreateChatHistoryMemoryProvider();
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
            var provider = CreateEntityMemoryContextProvider();
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

    private IContextProvider? CreateChatHistoryMemoryProvider()
    {
        try
        {
            var chatHistoryOptions = _options.Value.ContextProviders.ChatHistoryMemory;
            var scope = new ChatHistoryMemoryScope();
            var logger = _loggerFactory.CreateLogger<ChatHistoryMemoryProvider>();
            return new ChatHistoryMemoryProvider(_textSearchService, chatHistoryOptions, scope, logger);
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
            var logger = _loggerFactory.CreateLogger<MemoryContextProvider>();
            return new MemoryContextProvider(_memoryStore, defaultScope, logger);
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

    private IContextProvider? CreateEntityMemoryContextProvider()
    {
        try
        {
            var entityMemoryOptions = _options.Value.ContextProviders.EntityMemory;
            var logger = _loggerFactory.CreateLogger<EntityMemoryContextProvider>();

            return new EntityMemoryContextProvider(_entityMemoryStore, entityMemoryOptions, _entityExtractor, logger, _currentUser);
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
            if (skillsOptions.Paths.Count == 0)
            {
                _logger.LogWarning("Skills context provider is enabled but no skill paths are configured.");
                return null;
            }

            var skillLoaderLogger = _loggerFactory.CreateLogger<Skills.SkillLoader>();
            var skillLoader = new Skills.SkillLoader(skillLoaderLogger, _options);
            var logger = _loggerFactory.CreateLogger<SkillContextProvider>();
            var skillToolsProvider = skillsOptions.InjectionMode is SkillInjectionMode.OnDemandTools or SkillInjectionMode.Both
                ? _skillToolsProvider
                : null;

            return new SkillContextProvider(skillLoader, skillsOptions, logger, skillToolsProvider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create SkillContextProvider");
            return null;
        }
    }
}
