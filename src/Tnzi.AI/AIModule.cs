using McpClientFactory = Tnzi.AI.Infrastructure.Mcp.McpClientFactory;

namespace Tnzi.AI;

/// <summary>
/// AI 模块 - 基于 Microsoft.Extensions.AI 的自定义 Agent 引擎
/// </summary>
[DependsOn(typeof(EFCoreModule))]
[DependsOn(typeof(AspNetCoreModule))]
public class AIModule : TnziApplicationModule
{
    /// <summary>
    /// AI 模块加载顺序
    /// </summary>
    public override int LoadOrder => 50;

    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "AI";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 绑定配置选项（框架标准方式）
        context.Services.AddOptions<AIOptions>()
            .Bind(context.Configuration.GetSection("AI"))
            .ValidateWith<AIOptions, AIOptionsValidator>();

        context.Services.AddOptions<AiUtilityOptions>()
            .Bind(context.Configuration.GetSection("AI:Utility"))
            .ValidateWith<AiUtilityOptions, AiUtilityOptionsValidator>();

        context.Services.AddOptions<ThreadOptions>()
            .Bind(context.Configuration.GetSection("AI:Thread"))
            .ValidateWith<ThreadOptions, ThreadOptionsValidator>();

        context.Services.AddOptions<LoopDetectionOptions>()
            .Bind(context.Configuration.GetSection("AI:LoopDetection"))
            .ValidateWith<LoopDetectionOptions, LoopDetectionOptionsValidator>();
        context.Services.AddOptions<SubAgentOptions>()
            .Bind(context.Configuration.GetSection("AI:SubAgent"))
            .ValidateWith<SubAgentOptions, SubAgentOptionsValidator>();

        context.Services.AddOptions<BudgetOptions>()
            .Bind(context.Configuration.GetSection("AI:Budget"))
            .ValidateWith<BudgetOptions, BudgetOptionsValidator>();

        context.Services.AddOptions<SuggestionOptions>()
            .Bind(context.Configuration.GetSection("AI:Suggestions"))
            .ValidateWith<SuggestionOptions, SuggestionOptionsValidator>();

        context.Services.AddOptions<TodoOptions>()
            .Bind(context.Configuration.GetSection("AI:Todo"))
            .ValidateWith<TodoOptions, TodoOptionsValidator>();

        // 从环境变量补充 API Key（移自 Validator 的副作用）
        context.Services.PostConfigure<AIOptions>(options =>
        {
            foreach (var (providerName, providerOptions) in options.Providers)
            {
                if (string.IsNullOrWhiteSpace(providerOptions.ApiKey))
                {
                    var envVarName = $"AI__{providerName.ToUpperInvariant()}__APIKEY";
                    var envKey = Environment.GetEnvironmentVariable(envVarName);
                    if (!string.IsNullOrWhiteSpace(envKey))
                    {
                        providerOptions.ApiKey = envKey;
                    }
                }
            }
        });

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册 HttpClient 工厂
        services.AddHttpClient();

        // 配置带重试和熔断的命名 HttpClient（用于 AI 提供商调用）
        // Note: Thinking injection and reasoning extraction are handled by
        // ThinkingRequestPolicy (PipelinePolicy) inside the OpenAI SDK pipeline,
        // not via DelegatingHandlers (which don't work with HttpClientPipelineTransport).
        services.AddHttpClient("Tnzi.AI.Resilient")
            .AddStandardResilienceHandler(options =>
            {
                // 配置重试策略
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
                options.Retry.MaxDelay = TimeSpan.FromSeconds(10);

                // 配置熔断器策略（SamplingDuration 必须 >= 2 * AttemptTimeout）
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.MinimumThroughput = 5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });

        // OAuth 令牌请求专用 HttpClient（无重试/熔断，令牌端点自行处理错误）
        services.AddHttpClient("Tnzi.AI.OAuth");

        // 注册基础设施
        services.AddSingleton<IChatClientProvider, OpenAIChatClientProvider>();
        services.AddSingleton<IChatClientProvider, AnthropicChatClientProvider>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        // Multi-model provider message processors — 扩展点，用于处理特定提供商的消息格式差异。
        // ThinkTagChatMessageProcessorBase 处理 <think> 标签（MiniMax/Kimi/GLM 共用），DeepSeek/Gemini 为预留直通。
        // 应用代码可通过 IEnumerable<IChatMessageProcessor> 注入并按 ProviderName 匹配使用。
        services.AddSingleton<IChatMessageProcessor, DeepSeekChatMessageProcessor>();
        services.AddSingleton<IChatMessageProcessor, GeminiChatMessageProcessor>();
        services.AddSingleton<IChatMessageProcessor, MiniMaxChatMessageProcessor>();
        services.AddSingleton<IChatMessageProcessor, KimiChatMessageProcessor>();
        services.AddSingleton<IChatMessageProcessor, GlmChatMessageProcessor>();
        services.AddSingleton<IAgentExecutionContextAccessor, AgentExecutionContextAccessor>();
        // Scoped: AgentFactory/ToolResolver/OptionsBuilder must be Scoped so that
        // ToolAdapter captures the request-scoped IServiceProvider, enabling resolution of
        // Scoped tool providers (CommonTools, CandidateTools, etc.) without relying on
        // AsyncLocal propagation through async-iterator yield boundaries.
        services.AddScoped<IToolResolver, ToolResolver>();
        services.AddScoped<AgentExecutorOptionsBuilder>();
        services.AddScoped<IAgentFactory, AgentFactory>();

        // 工具基础设施（TryAdd: 允许 Agent 模块提前注册）
        services.TryAddSingleton<IToolScanner, ToolScanner>();
        services.TryAddSingleton<IToolRegistry, ToolRegistry>();

        // MCP：连接工厂与工具提供者（启用 AI:Mcp:Enabled 时生效）。IMcpToolProvider/McpClientFactory 为 Singleton（无请求级状态），Scoped ToolResolver 可安全注入它们。
        services.TryAddSingleton<McpOAuthClientHandler>();
        services.AddSingleton<IMcpClientFactory, McpClientFactory>();
        services.AddSingleton<IMcpToolProvider, McpToolProvider>();
        services.TryAddSingleton<IMcpResourceProvider, McpResourceProvider>();
        services.TryAddSingleton<IMcpPromptProvider, McpPromptProvider>();

        // Token 估算器（TryAdd：允许用户注册 tiktoken 等精确实现）
        services.TryAddSingleton<ITokenEstimator, HeuristicTokenEstimator>();

        // 注册 Agent 解析器
        services.AddScoped<IAgentResolver, AgentResolver>();

        // 可选子模块回退实现：允许只加载 AIModule 时仍能解析核心服务
        services.TryAddScoped<IWorkflowService, NoOpWorkflowService>();
        services.TryAddScoped<ISkillLoadTracker, NoOpSkillLoadTracker>();

        // 注册对话存储和记忆存储（使用 TryAdd，允许 Agent 模块替换）
        services.TryAddScoped<IConversationStore, DatabaseConversationStore>();
        services.TryAddScoped<IMemoryStore, DatabaseMemoryStore>();
        services.TryAddScoped<IMemoryConsolidator, LlmMemoryConsolidator>();
        services.TryAddScoped<IMemorySideQuery, MemorySideQuery>();

        // 注册实体记忆存储和 LLM 实体抽取器
        services.TryAddScoped<IEntityMemoryStore, DatabaseEntityMemoryStore>();
        services.AddScoped<LlmEntityExtractor>();

        // 注册项目上下文提供器（从 DI 获取 IProjectContextLoader）
        services.AddScoped<ProjectContextProvider>();

        // 注册上下文提供器贡献者（DI 驱动，取代 AgentExecutorOptionsBuilder 中硬编码 new）
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.TextSearchContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.ChatHistoryMemoryContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.MemoryContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.EntityMemoryContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.SkillContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.ProjectContextContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.DeferredToolContributor>();

        // 注册 Prompt 模板引擎（TryAdd：允许用户注册自定义模板引擎）
        services.TryAddScoped<IPromptTemplateEngine, SimplePromptTemplateEngine>();

        // 注册核心服务
        services.AddSingleton<ICostCalculator, CostCalculator>();
        services.AddScoped<IUsageLogService, UsageLogService>();
        services.AddScoped<IUsageAnalyticsService, UsageAnalyticsService>();
        services.AddScoped<QuotaService>();
        services.AddScoped<IQuotaService>(sp => sp.GetRequiredService<QuotaService>());
        services.TryAddScoped<IQuotaProvider>(sp => sp.GetRequiredService<QuotaService>());
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IAgentVersionRouter, AgentVersionRouter>();
        services.AddScoped<AgentThreadService>();
        services.AddScoped<IAgentThreadService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAgentThreadInternalService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IMessageFeedbackService, MessageFeedbackService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<IStructuredOutputService, StructuredOutputService>();

        // 注册 RAG 相关服务（使用 TryAdd 允许用户覆盖）
        // 用户可以注册自己的 ITextSearchService 实现来连接到向量存储（Redis、Qdrant、Pinecone 等）
        services.TryAddScoped<ITextSearchService, NoOpTextSearchService>();

        // 注册工具审批处理器（使用 TryAdd 允许用户覆盖）
        // 用户可以注册自己的 IToolApprovalHandler 实现来实现自定义审批逻辑
        services.TryAddSingleton<IToolApprovalHandler, AutoApprovalHandler>();
        services.TryAddSingleton<IToolPermissionEvaluator, ConfiguredToolPermissionEvaluator>();
        services.TryAddSingleton<IShellCommandAnalyzer, ShellCommandAnalyzer>();

        // 注册工具权限规则持久化存储（Scoped：需要 IRepository）
        services.AddScoped<IToolPermissionRuleStore, DatabaseToolPermissionRuleStore>();

        // [RequiresSkill] 兜底中间件 — 工具调用前检查 Skill 是否已加载
        services.AddScoped<IToolExecutionMiddleware, RequiresSkillToolMiddleware>();

        // 注册内置工具（默认提供，运行时根据配置决定是否使用）
        services.TryAddScoped<DateTimeTools>();
        services.TryAddScoped<TextTools>();
        services.TryAddScoped<WebSearchTools>();
        services.TryAddScoped<MemoryTools>();

        // A2A 工具（调用远程 Agent）
        services.TryAddScoped<A2ATools>();

        // 注册 OpenAPI 工具生成器（运行时通过 OpenApiToolsOptions.Enabled 控制是否生效）
        services.AddSingleton<OpenApiToolGenerator>();

        // 注册 Guardrails（运行时通过 GuardrailsOptions.Enabled 控制是否生效）
        services.AddScoped<GuardrailRunner>();

        // 输入 guardrails（同时注册为 IGuardrailProvider）
        services.AddScoped<MaxLengthGuardrail>();
        services.AddScoped<IInputGuardrail>(sp => sp.GetRequiredService<MaxLengthGuardrail>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<MaxLengthGuardrail>());

        services.AddScoped<PromptInjectionGuardrail>();
        services.AddScoped<IInputGuardrail>(sp => sp.GetRequiredService<PromptInjectionGuardrail>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<PromptInjectionGuardrail>());

        services.AddScoped<PiiDetectionGuardrail>();
        services.AddScoped<IInputGuardrail>(sp => sp.GetRequiredService<PiiDetectionGuardrail>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<PiiDetectionGuardrail>());

        // 输出 guardrails（同时注册为 IGuardrailProvider）
        services.AddScoped<ContentFilterGuardrail>();
        services.AddScoped<IOutputGuardrail>(sp => sp.GetRequiredService<ContentFilterGuardrail>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<ContentFilterGuardrail>());

        // LLM-as-Judge guardrail（同时作为输入、输出和统一 guardrail provider）
        services.AddScoped<LlmJudgeGuardrail>();
        services.AddScoped<IInputGuardrail>(sp => sp.GetRequiredService<LlmJudgeGuardrail>());
        services.AddScoped<IOutputGuardrail>(sp => sp.GetRequiredService<LlmJudgeGuardrail>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<LlmJudgeGuardrail>());

        // 工具白名单/黑名单 guardrail provider
        services.AddScoped<AllowlistGuardrailProvider>();
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<AllowlistGuardrailProvider>());

        // 注册中间件管道组件（手动注册，框架程序集不使用自动注册）
        // 每个中间件同时注册具体类型和 IAiMiddleware 接口转发，支持用户通过接口扩展
        services.AddSingleton<RetryMiddleware>();
        services.AddSingleton<IAiMiddleware>(sp => sp.GetRequiredService<RetryMiddleware>());

        services.AddScoped<ThinkingMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<ThinkingMiddleware>());

        services.AddScoped<PromptCachingMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<PromptCachingMiddleware>());

        services.AddScoped<QuotaMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<QuotaMiddleware>());

        services.AddScoped<InputGuardrailMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<InputGuardrailMiddleware>());

        services.AddScoped<HistoryMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<HistoryMiddleware>());

        services.TryAddScoped<CompositeContextProvider>();

        services.AddScoped<ContextInjectionMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<ContextInjectionMiddleware>());

        services.AddScoped<UsageLoggingMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<UsageLoggingMiddleware>());

        services.AddScoped<OutputGuardrailMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<OutputGuardrailMiddleware>());

        // Phase 5: Tool guardrail middleware
        services.AddScoped<ToolGuardrailMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<ToolGuardrailMiddleware>());
        services.AddScoped<IToolExecutionMiddleware>(sp => sp.GetRequiredService<ToolGuardrailMiddleware>());

        // Phase 1 middlewares
        // Singleton: tracks tool call patterns across requests via LRU dictionary
        services.AddSingleton<LoopDetectionMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<LoopDetectionMiddleware>());

        services.AddScoped<ToolErrorRecoveryMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<ToolErrorRecoveryMiddleware>());
        services.AddScoped<IToolExecutionMiddleware>(sp => sp.GetRequiredService<ToolErrorRecoveryMiddleware>());

        services.AddScoped<SubAgentLimitMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<SubAgentLimitMiddleware>());

        services.AddScoped<DeferredToolFilterMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<DeferredToolFilterMiddleware>());

        // Phase 2: Context Intelligence middlewares
        services.AddScoped<SummarizationMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<SummarizationMiddleware>());

        services.AddScoped<FileUploadMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<FileUploadMiddleware>());

        services.AddScoped<ViewImageMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<ViewImageMiddleware>());

        // Document converter (default: native .NET, can be overridden with CliDocumentConverter)
        services.TryAddSingleton<IDocumentConverter, NativeDocumentConverter>();

        // 注册 Runtime（统一 AI 执行入口 + Run/Trace 持久化）
        services.AddScoped<IRunStore, RunStore>();
        services.AddScoped<ITraceStore, TraceStore>();
        services.AddScoped<IAgentRuntime, AgentRuntime>();

        // 注册 Run 管理服务（查询、取消、审批、重试、反馈）
        services.AddScoped<ISubAgentExecutionService, SubAgentExecutionService>();
        services.AddScoped<IAgentRunService, AgentRunService>();
        services.AddScoped<IAgentTraceService, AgentTraceService>();
        services.AddScoped<IAgentRunSignalDispatcher, AgentRunSignalDispatcher>();
        services.AddScoped<IAgentRuntimeControlService, AgentRuntimeControlService>();

        // 注册 Agent 验证服务（配置有效性检查）
        services.AddScoped<IAgentValidationService, AgentValidationService>();
        services.AddScoped<IEvaluationService, EvaluationService>();
        services.AddScoped<IEvaluationMetricsService, EvaluationMetricsService>();

        // 注册 AgentPersona、UserProfile 和 AgentArtifact 服务
        services.AddScoped<IAgentPersonaService, AgentPersonaService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IAgentArtifactService, AgentArtifactService>();
        services.AddScoped<IAgentTaskService, AgentTaskService>();

        // Phase 3: 后续建议生成服务
        services.TryAddScoped<ISuggestionService, SuggestionService>();

        // Phase 3: Todo 和 Clarification 中间件
        services.AddScoped<TodoMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<TodoMiddleware>());

        services.AddScoped<ClarificationMiddleware>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<ClarificationMiddleware>());

        // Phase 3: AI 工具（Clarification / Todo / Artifact）
        services.TryAddScoped<ClarificationTools>();
        services.TryAddScoped<TodoTools>();
        services.TryAddScoped<ArtifactTools>();
        services.TryAddScoped<AgentRunControlTools>();

        // IAiUtility — 轻量级系统级 AI 调用
        services.TryAddScoped<IAiUtility, AiUtilityService>();

        // Workspace agent provider (file-based AGENT.md discovery)
        services.AddSingleton<IWorkspaceAgentProvider, WorkspaceAgentProvider>();

        // YAML Agent 定义文件加载与数据库同步
        services.AddSingleton<IAgentDefinitionProvider, YamlAgentDefinitionProvider>();
        services.AddHostedService<AgentDefinitionSyncService>();

        // Thread title generation event handler
        services.AddEventHandler<ThreadFirstReplyCompletedEvent, ThreadTitleGenerationHandler>();
        services.AddEventHandler<ThreadDeletedEvent, ThreadCleanupHandler>();

        // 嵌入式 AI 客户端（直接调用 IAgentRuntime，绕过 HTTP）
        services.TryAddScoped<ITnziAiClient, TnziAiClient>();

        // 配置变更检测器
        services.TryAddSingleton<IConfigChangeDetector, FileConfigChangeDetector>();

        // 注册 A2A 客户端（TryAdd：允许用户注册自定义实现）
        services.TryAddScoped<IA2AClient, HttpA2AClient>();

        // 注册 Agent 评估器（TryAdd：允许用户注册自定义实现）
        services.TryAddScoped<IAgentEvaluator, DefaultAgentEvaluator>();

        // Phase 6: 子 Agent 注册表（全局单例，3 内置类型 + 运行时扩展）
        services.AddSingleton<ISubAgentRegistry, SubAgentRegistry>();

        // Phase 6: HTML 可读性提取（SmartReader + 标签剥离降级）
        services.AddSingleton<IReadabilityExtractor, SmartReaderExtractor>();

        // Phase 6: 端口分配器（线程安全，socket 绑定验证）
        services.AddOptions<PortAllocatorOptions>()
            .Bind(context.Configuration.GetSection("AI:PortAllocator"))
            .ValidateWith<PortAllocatorOptions, PortAllocatorOptionsValidator>();
        services.AddSingleton<IPortAllocator, PortAllocator>();

        return Task.CompletedTask;
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var serviceProvider = context.ServiceProvider;
        var logger = serviceProvider.GetRequiredService<ILogger<AIModule>>();
        ValidateRuntimeConfiguration(serviceProvider, logger);

        // 扫描并注册工具（在应用初始化阶段执行一次）
        var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
        var toolScanner = serviceProvider.GetRequiredService<IToolScanner>();

        // 扫描自身程序集
        var assembly = Assembly.GetExecutingAssembly();
        RegisterTools(toolRegistry, toolScanner, assembly, logger);

        // 扫描应用程序集（排除框架程序集和系统程序集）
        var appAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic
                && a.FullName != null
                && !a.FullName.StartsWith("System.", StringComparison.Ordinal)
                && !a.FullName.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !IsFrameworkCoreAssembly(a)
                && a != assembly);

        foreach (var appAssembly in appAssemblies)
        {
            RegisterTools(toolRegistry, toolScanner, appAssembly, logger);
        }

        // 校验 [RequiresSkill] 引用的 slug 是否存在
        await ValidateRequiresSkillReferencesAsync(toolRegistry, serviceProvider, logger);

        // Auto-disable framework built-in tools when an application assembly registers the same tool group.
        // This prevents duplicate tools when an app ships its own MemoryTools (or similar) in the "memory" group.
        var frameworkBuiltInTypes = new[] { typeof(DateTimeTools), typeof(TextTools), typeof(WebSearchTools), typeof(MemoryTools) };
        foreach (var builtInType in frameworkBuiltInTypes)
        {
            var builtInGroupAttr = builtInType.GetCustomAttribute<AIToolGroupAttribute>();
            if (builtInGroupAttr == null) continue;

            var groupName = builtInGroupAttr.GroupName;
            var toolsInGroup = toolRegistry.GetToolsByGroup(groupName);

            // Check if any tool in this group comes from a non-framework provider type
            var hasAppProvider = toolsInGroup.Any(t =>
                t.ProviderType != builtInType
                && !IsFrameworkCoreAssembly(t.ProviderType.Assembly));

            if (hasAppProvider)
            {
                toolRegistry.UnregisterByProviderType(builtInType);
                logger.LogInformation(
                    "Auto-disabled built-in {ToolType} because application registered a provider in the same tool group '{Group}'",
                    builtInType.Name, groupName);
            }
        }

        // 根据 BuiltInToolsOptions 按 ProviderType 精确移除已禁用的内置工具
        // 使用 UnregisterByProviderType 而非 UnregisterGroup，避免误删用户注册的同名工具组
        var aiOptions = serviceProvider.GetRequiredService<IOptions<AIOptions>>().Value;
        var builtInOptions = aiOptions.BuiltInTools;
        if (!builtInOptions.Enabled)
        {
            toolRegistry.UnregisterByProviderType(typeof(DateTimeTools));
            toolRegistry.UnregisterByProviderType(typeof(TextTools));
            toolRegistry.UnregisterByProviderType(typeof(WebSearchTools));
            toolRegistry.UnregisterByProviderType(typeof(MemoryTools));
        }
        else
        {
            if (!builtInOptions.EnableDateTime) toolRegistry.UnregisterByProviderType(typeof(DateTimeTools));
            if (!builtInOptions.EnableText) toolRegistry.UnregisterByProviderType(typeof(TextTools));
            if (!builtInOptions.EnableWebSearch) toolRegistry.UnregisterByProviderType(typeof(WebSearchTools));
            if (!builtInOptions.EnableMemory) toolRegistry.UnregisterByProviderType(typeof(MemoryTools));
        }

        // 从数据库加载持久化的子 Agent 类型定义
        try
        {
            var subAgentRegistry = serviceProvider.GetRequiredService<ISubAgentRegistry>();
            using var subAgentScope = serviceProvider.CreateScope();
            var subAgentTypeRepo = subAgentScope.ServiceProvider.GetRequiredService<IRepository<SubAgentType, Guid>>();
            await subAgentRegistry.LoadFromStoreAsync(subAgentTypeRepo);
            logger.LogDebug("Sub-agent type definitions loaded from database.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load sub-agent type definitions from database. Using built-in types only.");
        }

        // Memory 工具依赖 ContextProviders 的记忆召回能力 —— 如果召回不可用，
        // 注册 save_memory 工具只会产生永远不会被想起的记忆，误导 AI 和用户。
        // 移除整个 "memory" 工具组（包含框架内置和应用自定义的 memory 工具）。
        var contextProviders = aiOptions.ContextProviders;
        if (!contextProviders.Enabled || !contextProviders.Memory.Enabled)
        {
            toolRegistry.UnregisterGroup("memory");
            logger.LogInformation(
                "Memory tool group disabled: ContextProviders.Enabled={ContextEnabled}, Memory.Enabled={MemoryEnabled}. " +
                "Enable both to allow AI to save and recall memories",
                contextProviders.Enabled, contextProviders.Memory.Enabled);
        }
    }

    private static void ValidateRuntimeConfiguration(IServiceProvider serviceProvider, ILogger logger)
    {
        var options = serviceProvider.GetRequiredService<IOptions<AIOptions>>().Value;
        var hasEnabledProvider = options.Providers.Values.Any(p => p.Enabled);

        if (options.History.Reduction.Mode == HistoryReductionMode.Summarize && !hasEnabledProvider)
        {
            throw new InvalidOperationException(
                "AI:History:Reduction:Mode is set to Summarize, but no enabled AI provider is configured.");
        }

        if (options.Guardrails.Enabled && options.Guardrails.LlmJudge.Enabled && !hasEnabledProvider)
        {
            throw new InvalidOperationException(
                "AI:Guardrails:LlmJudge is enabled, but no enabled AI provider is configured.");
        }

        if (options.ContextProviders.Enabled && options.ContextProviders.EntityMemory.Enabled && !hasEnabledProvider)
        {
            throw new InvalidOperationException(
                "AI:ContextProviders:EntityMemory is enabled, but no enabled AI provider is configured for entity extraction.");
        }

        if (options.BuiltInTools.Enabled && options.BuiltInTools.EnableWebSearch
            && serviceProvider.GetService<IWebSearchProvider>() == null)
        {
            throw new InvalidOperationException(
                "AI:BuiltInTools:EnableWebSearch is enabled, but no IWebSearchProvider is registered.");
        }

        if (options.ContextProviders.Enabled)
        {
            using var scope = serviceProvider.CreateScope();
            var textSearchService = scope.ServiceProvider.GetRequiredService<ITextSearchService>();
            if (textSearchService is INoOpService
                && (options.ContextProviders.TextSearch.Enabled || options.ContextProviders.ChatHistoryMemory.Enabled))
            {
                throw new InvalidOperationException(
                    "AI text search or chat history memory is enabled, but ITextSearchService is still a no-op fallback. Register a real ITextSearchService implementation.");
            }

            // Paths 为空时不再报错 — FileSystemSkillStore 有自动发现机制（��描模块程序集目录的 Skills/ 文件夹）
        }

        var workflowService = serviceProvider.GetRequiredService<IWorkflowService>();
        if (workflowService is INoOpService)
        {
            logger.LogInformation(
                "IWorkflowService is a no-op fallback; workflow APIs will return 501 until a workflow module is loaded.");
        }

        if (options.ContextProviders.Enabled
            && options.ContextProviders.Skills.Enabled
            && serviceProvider.GetService<ISkillRegistry>() == null)
        {
            logger.LogWarning(
                "AI skills context provider is enabled but no ISkillRegistry is registered. Skill context injection will be unavailable.");
        }

        logger.LogDebug("AI runtime configuration validation passed.");
    }

    /// <summary>
    /// 校验所有工具的 [RequiresSkill] 引用是否指向已注册的 Skill
    /// </summary>
    private static async Task ValidateRequiresSkillReferencesAsync(IToolRegistry toolRegistry, IServiceProvider serviceProvider, ILogger logger)
    {
        var allTools = toolRegistry.GetAllTools();
        var toolsWithSkills = allTools.Where(t => t.RequiresSkillSlugs is { Count: > 0 }).ToList();
        if (toolsWithSkills.Count == 0) return;

        // ISkillRegistry is scoped — create a temporary scope for startup validation
        using var scope = serviceProvider.CreateScope();
        var skillRegistry = scope.ServiceProvider.GetService<ISkillRegistry>();
        if (skillRegistry == null) return;

        var allSlugs = toolsWithSkills.SelectMany(t => t.RequiresSkillSlugs!).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in allSlugs)
        {
            var skill = await skillRegistry.GetBySlugAsync(slug, CancellationToken.None);
            if (skill == null)
            {
                logger.LogWarning(
                    "[RequiresSkill] references skill '{Slug}' which does not exist. " +
                    "Tools referencing this skill: {Tools}. Check for typos or missing SKILL.md files.",
                    slug,
                    string.Join(", ", toolsWithSkills.Where(t => t.RequiresSkillSlugs!.Contains(slug, StringComparer.OrdinalIgnoreCase)).Select(t => t.Name)));
            }
        }
    }

    /// <summary>
    /// 扫描程序集并注册工具到 ToolRegistry
    /// </summary>
    private static void RegisterTools(IToolRegistry registry, IToolScanner scanner, Assembly assembly, ILogger logger)
    {
        try
        {
            var tools = scanner.ScanAssembly(assembly);
            foreach (var tool in tools)
            {
                registry.Register(tool);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to scan assembly '{AssemblyName}' for AI tools. Skipping this assembly.",
                assembly.FullName);
        }
    }

    /// <summary>
    /// 判断是否为框架核心程序集
    /// </summary>
    /// <remarks>
    /// 所有 Tnzi.* 程序集（包括 Tnzi.AI.Coder）由各自模块负责工具注册。
    /// AIModule 只扫描自身程序集和用户应用程序集。
    /// </remarks>
    private static bool IsFrameworkCoreAssembly(Assembly a)
    {
        var name = a.GetName().Name;
        if (name == null) return false;
        return name.StartsWith("Tnzi.", StringComparison.Ordinal);
    }
}
