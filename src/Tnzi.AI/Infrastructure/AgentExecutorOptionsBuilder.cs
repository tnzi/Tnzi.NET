
namespace Tnzi.AI.Infrastructure;

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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentExecutorOptionsBuilder> _logger;

    public AgentExecutorOptionsBuilder(
        IOptions<AIOptions> options,
        IServiceProvider serviceProvider,
        ILogger<AgentExecutorOptionsBuilder> logger)
    {
        _options = Check.NotNull(options);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
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
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<PruneChatReducer>();

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
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<SummarizeChatReducer>();

        var chatClientFactory = _serviceProvider.GetService<IChatClientFactory>();
        if (chatClientFactory == null)
        {
            _logger.LogWarning("IChatClientFactory is not available. Summarize reducer requires a ChatClient. Falling back to no reduction.");
            return null;
        }

        try
        {
            var provName = summarizeOptions.Provider ?? _options.Value.DefaultProvider;
            var chatClient = chatClientFactory.GetChatClient(provName, summarizeOptions.SummaryModelId);

            _logger.LogDebug(
                "Creating SummarizeChatReducer: MessageThreshold={MessageThreshold}, TokenThreshold={TokenThreshold}, KeepRecentTurns={KeepRecentTurns}",
                summarizeOptions.MessageThreshold, summarizeOptions.TokenThreshold, summarizeOptions.KeepRecentTurns);

            return new SummarizeChatReducer(summarizeOptions, chatClient, logger);
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

        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        var compositeLogger = loggerFactory.CreateLogger<CompositeContextProvider>();
        var compositeProvider = new CompositeContextProvider(compositeLogger, _options);

        if (contextConfig.TextSearch.Enabled)
        {
            var provider = CreateTextSearchProvider(loggerFactory);
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (contextConfig.ChatHistoryMemory.Enabled)
        {
            var provider = CreateChatHistoryMemoryProvider(loggerFactory);
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (contextConfig.Skills.Enabled)
        {
            var provider = CreateSkillContextProvider(loggerFactory);
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (contextConfig.Memory.Enabled)
        {
            var provider = CreateMemoryContextProvider(loggerFactory, contextConfig.Memory.DefaultScope);
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (contextConfig.EntityMemory.Enabled)
        {
            var provider = CreateEntityMemoryContextProvider(loggerFactory);
            if (provider != null) compositeProvider.AddProvider(provider);
        }

        if (compositeProvider.ProviderCount == 0)
        {
            _logger.LogDebug("No context providers configured, skipping ContextProvider creation");
            return null;
        }

        _logger.LogDebug("Created CompositeContextProvider with {ProviderCount} sub-providers", compositeProvider.ProviderCount);
        return compositeProvider;
    }

    private IContextProvider? CreateTextSearchProvider(ILoggerFactory loggerFactory)
    {
        try
        {
            var textSearchService = _serviceProvider.GetService<ITextSearchService>();
            if (textSearchService == null)
            {
                _logger.LogWarning("ITextSearchService is not registered. TextSearchProvider will not be available.");
                return null;
            }
            var logger = loggerFactory.CreateLogger<TextSearchProvider>();
            return new TextSearchProvider(textSearchService, _options.Value.ContextProviders.TextSearch, logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TextSearchProvider");
            return null;
        }
    }

    private IContextProvider? CreateChatHistoryMemoryProvider(ILoggerFactory loggerFactory)
    {
        try
        {
            var textSearchService = _serviceProvider.GetService<ITextSearchService>();
            if (textSearchService == null)
            {
                _logger.LogWarning("ITextSearchService is not registered. ChatHistoryMemoryProvider will not be available.");
                return null;
            }

            var chatHistoryOptions = _options.Value.ContextProviders.ChatHistoryMemory;
            var scope = new ChatHistoryMemoryScope();
            var logger = loggerFactory.CreateLogger<ChatHistoryMemoryProvider>();
            return new ChatHistoryMemoryProvider(textSearchService, chatHistoryOptions, scope, logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ChatHistoryMemoryProvider");
            return null;
        }
    }

    private IContextProvider? CreateMemoryContextProvider(ILoggerFactory loggerFactory, string defaultScope)
    {
        try
        {
            var memoryStore = _serviceProvider.GetService<IMemoryStore>();
            if (memoryStore == null) return null;
            var logger = loggerFactory.CreateLogger<MemoryContextProvider>();
            return new MemoryContextProvider(memoryStore, defaultScope, logger);
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
        var middlewares = _serviceProvider.GetServices<IToolExecutionMiddleware>().ToList();
        return middlewares.Count > 0 ? middlewares : null;
    }

    private IContextProvider? CreateEntityMemoryContextProvider(ILoggerFactory loggerFactory)
    {
        try
        {
            var entityMemoryStore = _serviceProvider.GetService<IEntityMemoryStore>();
            if (entityMemoryStore == null)
            {
                _logger.LogWarning("IEntityMemoryStore is not registered. EntityMemoryContextProvider will not be available.");
                return null;
            }

            var extractor = _serviceProvider.GetService<LlmEntityExtractor>();
            if (extractor == null)
            {
                _logger.LogWarning("LlmEntityExtractor is not registered. EntityMemoryContextProvider will not be available.");
                return null;
            }

            var entityMemoryOptions = _options.Value.ContextProviders.EntityMemory;
            var currentUser = _serviceProvider.GetService<ICurrentUser>();
            var logger = loggerFactory.CreateLogger<EntityMemoryContextProvider>();

            return new EntityMemoryContextProvider(entityMemoryStore, entityMemoryOptions, extractor, logger, currentUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create EntityMemoryContextProvider");
            return null;
        }
    }

    private IContextProvider? CreateSkillContextProvider(ILoggerFactory loggerFactory)
    {
        try
        {
            var skillsOptions = _options.Value.ContextProviders.Skills;
            if (skillsOptions.Paths.Count == 0)
            {
                _logger.LogWarning("Skills context provider is enabled but no skill paths are configured.");
                return null;
            }

            var skillLoaderLogger = loggerFactory.CreateLogger<Skills.SkillLoader>();
            var skillLoader = new Skills.SkillLoader(skillLoaderLogger, _options);
            var logger = loggerFactory.CreateLogger<SkillContextProvider>();

            SkillToolsProvider? skillToolsProvider = null;
            if (skillsOptions.InjectionMode is SkillInjectionMode.OnDemandTools or SkillInjectionMode.Both)
            {
                skillToolsProvider = _serviceProvider.GetService<SkillToolsProvider>();
                if (skillToolsProvider == null)
                {
                    _logger.LogWarning("SkillToolsProvider is not registered but InjectionMode requires on-demand tools.");
                }
            }

            return new SkillContextProvider(skillLoader, skillsOptions, logger, skillToolsProvider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create SkillContextProvider");
            return null;
        }
    }
}
