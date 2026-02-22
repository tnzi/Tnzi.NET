
namespace Tnzi.AI.Infrastructure;

/// <summary>
/// Agent 工厂 - 创建 AgentExecutor 实例
/// </summary>
public class AgentFactory : IAgentFactory
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IOptions<AIOptions> _options;
    private readonly ToolRegistry _toolRegistry;
    private readonly IMcpToolProvider _mcpToolProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentFactory> _logger;

    public AgentFactory(
        IChatClientFactory chatClientFactory,
        IOptions<AIOptions> options,
        ToolRegistry toolRegistry,
        IMcpToolProvider mcpToolProvider,
        IServiceProvider serviceProvider,
        ILogger<AgentFactory> logger)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _toolRegistry = Check.NotNull(toolRegistry);
        _mcpToolProvider = Check.NotNull(mcpToolProvider);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<AgentExecutor> CreateAgentAsync(
        string? providerName = null,
        string? model = null,
        string? instructions = null,
        string? name = null,
        IEnumerable<string>? toolGroups = null,
        double? temperature = null,
        int? maxTokens = null,
        AgentExecutorOptions? options = null,
        CancellationToken ct = default)
    {
        var resolvedProvider = providerName ?? _options.Value.DefaultProvider;

        if (!_options.Value.Providers.TryGetValue(resolvedProvider, out var providerConfig))
        {
            throw new InvalidOperationException($"AI provider '{resolvedProvider}' is not configured");
        }

        if (!providerConfig.Enabled)
        {
            throw new InvalidOperationException($"AI provider '{resolvedProvider}' is disabled");
        }

        var chatClient = _chatClientFactory.GetChatClient(providerName, model);
        var resolvedModel = model ?? providerConfig.DefaultModel;

        // C# 工具：仅当 toolGroups 非空时从 Registry 获取，并对 C# 工具做全局 Approval 包装（不对 MCP 工具做二次包装）
        IList<AITool>? csharpTools = null;
        if (toolGroups != null)
        {
            var toolDefinitions = _toolRegistry.GetToolsByGroups(toolGroups);
            csharpTools = ToolAdapter.ConvertToAITools(toolDefinitions, _serviceProvider);
            if (csharpTools.Count > 0 && _options.Value.ToolApproval.Enabled)
            {
                var approvalHandler = _serviceProvider.GetService<IToolApprovalHandler>();
                if (approvalHandler != null)
                {
                    var approvalOptions = _options.Value.ToolApproval;
                    var approvalLogger = _serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<ApprovalToolWrapper>();
                    var toolNameToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var td in toolDefinitions)
                    {
                        if (!string.IsNullOrEmpty(td.GroupName))
                        {
                            toolNameToGroup[td.Name] = td.GroupName;
                        }
                    }
                    csharpTools = ApprovalToolWrapper.Wrap(csharpTools, approvalHandler, approvalOptions, approvalLogger, toolNameToGroup);
                }
            }
        }

        // MCP 工具：当启用时拉取并合并，MCP 侧已在 McpToolProvider 内按每服务器配置完成审批包装（Mcp 为 null 时视为未启用）
        IList<AITool>? tools = null;
        if (_options.Value.Mcp?.Enabled == true)
        {
            var mcpTools = await _mcpToolProvider.GetToolsAsync(ct).ConfigureAwait(false);
            var merged = new List<AITool>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (csharpTools != null)
            {
                foreach (var t in csharpTools)
                {
                    if (t.Name != null && names.Add(t.Name))
                    {
                        merged.Add(t);
                    }
                }
            }
            foreach (var t in mcpTools)
            {
                if (t.Name != null && names.Add(t.Name))
                {
                    merged.Add(t);
                }
            }
            if (merged.Count > 0)
            {
                tools = merged;
            }
        }
        else if (csharpTools is { Count: > 0 })
        {
            tools = csharpTools;
        }

        var meaiChatClient = chatClient.AsIChatClient();

        // 构建 AgentExecutorOptions：调用方传入的 options 优先，否则根据配置创建
        var executorOptions = BuildExecutorOptions(options, name, instructions, tools, temperature, maxTokens);

        var agent = new AgentExecutor(meaiChatClient, executorOptions);

        _logger.LogDebug(
            "AgentExecutor created: Name={Name}, Provider={Provider}, Model={Model}, ToolCount={ToolCount}",
            executorOptions.Name, resolvedProvider, resolvedModel, tools?.Count ?? 0);

        return agent;
    }

    /// <summary>
    /// 构建 AgentExecutorOptions，合并调用方传入的选项与方法参数
    /// </summary>
    private AgentExecutorOptions BuildExecutorOptions(
        AgentExecutorOptions? callerOptions,
        string? name,
        string? instructions,
        IList<AITool>? tools,
        double? temperature,
        int? maxTokens)
    {
        if (callerOptions != null)
        {
            // 调用方已传入 options，基于副本合并参数
            var opts = new AgentExecutorOptions
            {
                Name = callerOptions.Name ?? name ?? "Agent",
                Instructions = !string.IsNullOrWhiteSpace(instructions) ? instructions : callerOptions.Instructions,
                Tools = tools is { Count: > 0 } ? tools : callerOptions.Tools,
                Temperature = temperature.HasValue ? (float)temperature.Value : callerOptions.Temperature,
                MaxOutputTokens = maxTokens ?? callerOptions.MaxOutputTokens,
                MaxToolIterations = callerOptions.MaxToolIterations,
                HistoryReducer = callerOptions.HistoryReducer,
                ContextProvider = callerOptions.ContextProvider
            };
            return opts;
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
            ContextProvider = contextProvider
        };
    }

    /// <summary>
    /// 根据配置创建 IHistoryReducer（如果启用历史压缩）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 根据 AI:History:Reduction:Mode 配置决定压缩模式：
    /// None - 不压缩；Prune - 裁剪旧消息；Summarize - AI 摘要压缩。
    /// </para>
    /// <para>
    /// PruneChatReducer/SummarizeChatReducer 直接实现 IHistoryReducer 接口。
    /// </para>
    /// </remarks>
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

        if (reducer == null)
        {
            return null;
        }

        _logger.LogDebug("History reducer created with mode {Mode}", reductionMode);
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

        // 获取用于摘要的 ChatClient
        var chatClientFactory = _serviceProvider.GetService<IChatClientFactory>();
        if (chatClientFactory == null)
        {
            _logger.LogWarning(
                "IChatClientFactory is not available. Summarize reducer requires a ChatClient. " +
                "Falling back to no reduction.");
            return null;
        }

        try
        {
            // 使用配置的提供商或默认提供商
            var provName = summarizeOptions.Provider ?? _options.Value.DefaultProvider;
            var openAiChatClient = chatClientFactory.GetChatClient(provName, summarizeOptions.SummaryModelId);
            var chatClient = openAiChatClient.AsIChatClient();

            _logger.LogDebug(
                "Creating SummarizeChatReducer: MessageThreshold={MessageThreshold}, TokenThreshold={TokenThreshold}, KeepRecentTurns={KeepRecentTurns}",
                summarizeOptions.MessageThreshold, summarizeOptions.TokenThreshold, summarizeOptions.KeepRecentTurns);

            return new SummarizeChatReducer(summarizeOptions, chatClient, logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create SummarizeChatReducer. Falling back to no reduction.");
            return null;
        }
    }

    /// <summary>
    /// 根据配置创建 IContextProvider（如果启用上下文提供器）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 根据 AI:ContextProviders 配置决定启用哪些子 provider：
    /// TextSearch - RAG 文本搜索；ChatHistoryMemory - 聊天历史记忆；Skills - 技能上下文。
    /// </para>
    /// <para>
    /// 通过 CompositeContextProvider 组合所有子 provider。
    /// TextSearchProvider、ChatHistoryMemoryProvider 和 SkillContextProvider 均直接实现 IContextProvider。
    /// </para>
    /// </remarks>
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

        // 注册 TextSearchProvider（如果启用）
        if (contextConfig.TextSearch.Enabled)
        {
            var textSearchProvider = CreateTextSearchProvider(loggerFactory);
            if (textSearchProvider != null)
            {
                compositeProvider.AddProvider(textSearchProvider);
                _logger.LogDebug("Added TextSearchProvider to CompositeContextProvider");
            }
        }

        // 注册 ChatHistoryMemoryProvider（如果启用）
        if (contextConfig.ChatHistoryMemory.Enabled)
        {
            var chatHistoryProvider = CreateChatHistoryMemoryProvider(loggerFactory);
            if (chatHistoryProvider != null)
            {
                compositeProvider.AddProvider(chatHistoryProvider);
                _logger.LogDebug("Added TnziChatHistoryMemoryProvider to CompositeContextProvider");
            }
        }

        // 注册 SkillContextProvider（如果启用）
        if (contextConfig.Skills.Enabled)
        {
            var skillProvider = CreateSkillContextProvider(loggerFactory);
            if (skillProvider != null)
            {
                compositeProvider.AddProvider(skillProvider);
                _logger.LogDebug("Added SkillContextProvider to CompositeContextProvider");
            }
        }

        if (compositeProvider.ProviderCount == 0)
        {
            _logger.LogDebug("No context providers configured, skipping ContextProvider creation");
            return null;
        }

        _logger.LogDebug("Created CompositeContextProvider with {ProviderCount} sub-providers", compositeProvider.ProviderCount);
        return compositeProvider;
    }

    /// <summary>
    /// 创建 TextSearchProvider
    /// </summary>
    private IContextProvider? CreateTextSearchProvider(ILoggerFactory loggerFactory)
    {
        try
        {
            var textSearchService = _serviceProvider.GetService<ITextSearchService>();
            if (textSearchService == null)
            {
                _logger.LogWarning(
                    "ITextSearchService is not registered. TextSearchProvider will not be available. " +
                    "Register an ITextSearchService implementation to enable RAG functionality.");
                return null;
            }

            var textSearchOptions = _options.Value.ContextProviders.TextSearch;
            var logger = loggerFactory.CreateLogger<TextSearchProvider>();

            return new TextSearchProvider(textSearchService, textSearchOptions, logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TextSearchProvider");
            return null;
        }
    }

    /// <summary>
    /// 创建 TnziChatHistoryMemoryProvider
    /// </summary>
    /// <remarks>
    /// <para>
    /// 需要用户注册 ITextSearchService 实现来提供文本搜索能力。
    /// 如果未注册，将返回 null 并记录警告。
    /// </para>
    /// </remarks>
    private IContextProvider? CreateChatHistoryMemoryProvider(ILoggerFactory loggerFactory)
    {
        try
        {
            var textSearchService = _serviceProvider.GetService<ITextSearchService>();
            if (textSearchService == null)
            {
                _logger.LogWarning(
                    "ITextSearchService is not registered. ChatHistoryMemoryProvider will not be available. " +
                    "Register an ITextSearchService implementation to enable chat history memory functionality.");
                return null;
            }

            var chatHistoryOptions = _options.Value.ContextProviders.ChatHistoryMemory;

            // 创建默认 scope（可以通过其他方式扩展，如从当前请求上下文获取）
            var scope = new ChatHistoryMemoryScope
            {
                // 默认不限定范围，搜索所有历史
                // 用户可以通过自定义 Agent 创建来指定 scope
            };

            var logger = loggerFactory.CreateLogger<ChatHistoryMemoryProvider>();

            return new ChatHistoryMemoryProvider(
                textSearchService,
                chatHistoryOptions,
                scope,
                logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ChatHistoryMemoryProvider");
            return null;
        }
    }

    /// <summary>
    /// 创建 SkillContextProvider
    /// </summary>
    private IContextProvider? CreateSkillContextProvider(ILoggerFactory loggerFactory)
    {
        try
        {
            var skillsOptions = _options.Value.ContextProviders.Skills;

            // 检查是否配置了技能路径
            if (skillsOptions.Paths.Count == 0)
            {
                _logger.LogWarning(
                    "Skills context provider is enabled but no skill paths are configured. " +
                    "Add paths to AI:ContextProviders:Skills:Paths configuration.");
                return null;
            }

            var skillLoaderLogger = loggerFactory.CreateLogger<Skills.SkillLoader>();
            var skillLoader = new Skills.SkillLoader(skillLoaderLogger, _options);

            var logger = loggerFactory.CreateLogger<SkillContextProvider>();

            // 按需工具模式需要 SkillToolsProvider
            SkillToolsProvider? skillToolsProvider = null;
            if (skillsOptions.InjectionMode == SkillInjectionMode.OnDemandTools
                || skillsOptions.InjectionMode == SkillInjectionMode.Both)
            {
                skillToolsProvider = _serviceProvider.GetService<SkillToolsProvider>();
                if (skillToolsProvider == null)
                {
                    _logger.LogWarning(
                        "SkillToolsProvider is not registered but InjectionMode requires on-demand tools. " +
                        "Skill tools will not be available.");
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
