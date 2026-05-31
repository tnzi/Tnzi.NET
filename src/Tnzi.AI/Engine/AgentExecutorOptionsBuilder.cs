
namespace Tnzi.AI.Engine;

/// <summary>
/// AgentExecutorOptions 构建器 — 负责构建 AgentExecutorOptions（含 HistoryReducer）。
/// </summary>
/// <remarks>
/// 从 AgentFactory 提取，职责为：
/// - 合并调用方传入的 Options 与方法参数
/// - 根据配置创建 HistoryReducer（Prune / Summarize）
/// Context injection is now owned by ContextInjectionMiddleware (uses IContextProviderContributor
/// directly), so this builder no longer creates IContextProvider instances.
/// </remarks>
public class AgentExecutorOptionsBuilder
{
    private readonly IOptions<AIOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IEnumerable<IToolExecutionMiddleware> _middlewares;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<AgentExecutorOptionsBuilder> _logger;

    public AgentExecutorOptionsBuilder(
        IOptions<AIOptions> options,
        ILoggerFactory loggerFactory,
        IChatClientFactory chatClientFactory,
        IEnumerable<IToolExecutionMiddleware> middlewares,
        ITokenEstimator tokenEstimator,
        ILogger<AgentExecutorOptionsBuilder> logger)
    {
        _options = Check.NotNull(options);
        _loggerFactory = Check.NotNull(loggerFactory);
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _middlewares = Check.NotNull(middlewares);
        _tokenEstimator = Check.NotNull(tokenEstimator);
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
        int? maxTokens,
        Guid? agentId = null,
        string? agentName = null)
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
                Middlewares = callerOptions.Middlewares ?? middlewares,
                StripTextFromToolCallMessages = callerOptions.StripTextFromToolCallMessages
            };
        }

        // 调用方未传入 options，根据配置自动创建 HistoryReducer
        var historyReducer = CreateHistoryReducer();

        return new AgentExecutorOptions
        {
            Name = name ?? "Agent",
            Instructions = instructions,
            Tools = tools is { Count: > 0 } ? tools : null,
            Temperature = temperature.HasValue ? (float)temperature.Value : null,
            MaxOutputTokens = maxTokens,
            HistoryReducer = historyReducer,
            Middlewares = middlewares,
            StripTextFromToolCallMessages = _options.Value.StripTextFromToolCallMessages
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
            HistoryReductionMode.PruneThenSummarize => CreateChainedReducer(),
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

        return new PruneChatReducer(pruneOptions, _tokenEstimator, logger);
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
    /// 创建链式压缩器: Prune → Summarize
    /// </summary>
    private IHistoryReducer? CreateChainedReducer()
    {
        var pruneReducer = CreatePruneChatReducer();
        var summarizeReducer = CreateSummarizeChatReducer();

        if (summarizeReducer == null)
        {
            _logger.LogWarning("PruneThenSummarize mode: Summarize reducer creation failed, falling back to Prune only");
            return pruneReducer;
        }

        _logger.LogDebug("Creating ChainedChatReducer: Prune → Summarize");
        return new ChainedChatReducer([pruneReducer, summarizeReducer]);
    }

    /// <summary>
    /// 从 DI 解析已注册的工具执行中间件
    /// </summary>
    private IReadOnlyList<IToolExecutionMiddleware>? ResolveMiddlewares()
    {
        var middlewares = _middlewares.ToList();
        return middlewares.Count > 0 ? middlewares : null;
    }
}
