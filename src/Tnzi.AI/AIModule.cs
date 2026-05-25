using McpClientFactory = Tnzi.AI.Infrastructure.Mcp.McpClientFactory;

namespace Tnzi.AI;

/// <summary>
/// AI 模块 - 基于 Microsoft.Extensions.AI 的自定义 Agent 引擎
/// </summary>
[DependsOn(typeof(EFCoreModule))]
[DependsOn(typeof(AspNetCoreModule))]
public partial class AIModule : TnziApplicationModule
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
        // 绑定配置选项
        BindAndValidate<AIOptions, AIOptionsValidator>(context, "AI");
        BindAndValidate<AiUtilityOptions, AiUtilityOptionsValidator>(context, "AI:Utility");
        BindAndValidate<ThreadOptions, ThreadOptionsValidator>(context, "AI:Thread");
        BindAndValidate<LoopDetectionOptions, LoopDetectionOptionsValidator>(context, "AI:LoopDetection");
        BindAndValidate<SubAgentOptions, SubAgentOptionsValidator>(context, "AI:SubAgent");
        BindAndValidate<BudgetOptions, BudgetOptionsValidator>(context, "AI:Budget");
        BindAndValidate<SuggestionOptions, SuggestionOptionsValidator>(context, "AI:Suggestions");
        BindAndValidate<TodoOptions, TodoOptionsValidator>(context, "AI:Todo");
        BindAndValidate<Tools.Sql.SqlToolOptions, Tools.Sql.SqlToolOptionsValidator>(context, "AI:Sql");

        // 从环境变量补充 API Key，并回填 Provider Name（用于 Polly pipeline 路由）
        context.Services.PostConfigure<AIOptions>(options =>
        {
            foreach (var (providerName, providerOptions) in options.Providers)
            {
                // 回填 Name — configuration dictionary key 是权威来源
                providerOptions.Name = providerName;

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

    private static void BindAndValidate<TOptions, TValidator>(
        ServiceConfigurationContext context, string section)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        context.Services.AddOptions<TOptions>()
            .Bind(context.Configuration.GetSection(section))
            .ValidateWith<TOptions, TValidator>();
    }

    private static void AddAiMiddleware<T>(IServiceCollection services)
        where T : class, IAiMiddleware
    {
        services.AddScoped<T>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<T>());
    }

    private static void AddAiMiddlewareSingleton<T>(IServiceCollection services,
        bool forwardAsSingleton = false)
        where T : class, IAiMiddleware
    {
        services.AddSingleton<T>();
        if (forwardAsSingleton)
            services.AddSingleton<IAiMiddleware>(sp => sp.GetRequiredService<T>());
        else
            services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<T>());
    }

    private static void AddToolMiddleware<T>(IServiceCollection services)
        where T : class, IAiMiddleware, IToolExecutionMiddleware
    {
        services.AddScoped<T>();
        services.AddScoped<IAiMiddleware>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IToolExecutionMiddleware>(sp => sp.GetRequiredService<T>());
    }

    private static void AddInputGuardrail<T>(IServiceCollection services)
        where T : class, IInputGuardrail, IGuardrailProvider
    {
        services.AddScoped<T>();
        services.AddScoped<IInputGuardrail>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<T>());
    }

    private static void AddOutputGuardrail<T>(IServiceCollection services)
        where T : class, IOutputGuardrail, IGuardrailProvider
    {
        services.AddScoped<T>();
        services.AddScoped<IOutputGuardrail>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<T>());
    }

    private static void AddDualGuardrail<T>(IServiceCollection services)
        where T : class, IInputGuardrail, IOutputGuardrail, IGuardrailProvider
    {
        services.AddScoped<T>();
        services.AddScoped<IInputGuardrail>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IOutputGuardrail>(sp => sp.GetRequiredService<T>());
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<T>());
    }

    private static void AddProviderOnlyGuardrail<T>(IServiceCollection services)
        where T : class, IGuardrailProvider
    {
        services.AddScoped<T>();
        services.AddScoped<IGuardrailProvider>(sp => sp.GetRequiredService<T>());
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        services.AddHttpClient();

        // Per-provider resilience pipelines — Polly keys its circuit state by HttpClient
        // name, so giving each provider a unique name isolates their breakers. A 429 on
        // one provider cannot open circuits on the others.
        // Thinking injection / reasoning extraction run inside the OpenAI SDK pipeline
        // (ThinkingRequestPolicy), not as DelegatingHandlers — the latter don't compose
        // with HttpClientPipelineTransport.
        static void ConfigureAiResilience(HttpStandardResilienceOptions options)
        {
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.Delay = TimeSpan.FromSeconds(1);
            options.Retry.MaxDelay = TimeSpan.FromSeconds(10);

            // SamplingDuration must be >= 2 * AttemptTimeout (Polly invariant).
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        }

        foreach (var providerChild in context.Configuration.GetSection("AI:Providers").GetChildren())
        {
            var providerName = providerChild.Key;
            if (string.IsNullOrWhiteSpace(providerName)) continue;

            // Skip providers explicitly disabled in config — no point wiring up a
            // circuit for a client that will never be resolved.
            if (providerChild.GetValue("Enabled", defaultValue: true) == false) continue;

            services.AddHttpClient(ResilientHttpClientNames.For(providerName))
                .AddStandardResilienceHandler(ConfigureAiResilience);
        }

        // Fallback client — shared pipeline for providers added dynamically at runtime
        // (not listed in AI:Providers). Same-name circuit state is shared across them;
        // static configuration is the recommended production setup.
        services.AddHttpClient(ResilientHttpClientNames.Fallback)
            .AddStandardResilienceHandler(ConfigureAiResilience);

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

        // Workflow 子接口转发 — NoOpWorkflowService 已实现 IWorkflowService
        //（继承 IWorkflowExecutionControlService + IWorkflowExecutionQueryService），
        // 但 DI 不会自动转发子接口，需显式注册以消除 GetService<T>() null-check 脆弱性
        services.TryAddScoped<IWorkflowExecutionControlService>(sp =>
            (IWorkflowExecutionControlService)sp.GetRequiredService<IWorkflowService>());
        services.TryAddScoped<IWorkflowExecutionQueryService>(sp =>
            (IWorkflowExecutionQueryService)sp.GetRequiredService<IWorkflowService>());

        // 其他 Category D NoOp 回退 — 已在核心被 GetService<T>() 消费
        services.TryAddScoped<IExternalCliExecutor, NoOpExternalCliExecutor>();
        services.TryAddScoped<IWorkflowExecutionMailbox, NoOpWorkflowExecutionMailbox>();
        services.TryAddScoped<IAgentStreamForwarder, NoOpAgentStreamForwarder>();

        // Category C NoOp 回退 — 接口在核心定义、实现在子模块，防止 DI 解析失败
        services.TryAddScoped<ISkillService, NoOpSkillService>();
        services.TryAddScoped<ISkillStore, NoOpSkillStore>();
        services.TryAddScoped<ISkillTemplateEngine, NoOpSkillTemplateEngine>();
        services.TryAddScoped<ISkillConstraintEnforcer, NoOpSkillConstraintEnforcer>();
        services.TryAddScoped<ISkillSearchService, NoOpSkillSearchService>();
        services.TryAddScoped<ISkillRequirementsValidator, NoOpSkillRequirementsValidator>();
        services.TryAddScoped<IVectorStore, NoOpVectorStore>();

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

        // Provider entity CRUD (Phase 5 backend prereq) — entity-driven provider management
        // with encrypted credential storage. IDataProtectionProvider is provided by the host
        // (added automatically when ASP.NET Core is referenced).
        services.AddDataProtection();
        services.AddScoped<IProviderService, ProviderService>();
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
        AddInputGuardrail<MaxLengthGuardrail>(services);
        AddInputGuardrail<PromptInjectionGuardrail>(services);
        AddInputGuardrail<PiiDetectionGuardrail>(services);

        // 输出 guardrails（同时注册为 IGuardrailProvider）
        AddOutputGuardrail<ContentFilterGuardrail>(services);

        // LLM-as-Judge guardrail（同时作为输入、输出和统一 guardrail provider）
        AddDualGuardrail<LlmJudgeGuardrail>(services);

        // 工具白名单/黑名单 guardrail provider
        AddProviderOnlyGuardrail<AllowlistGuardrailProvider>(services);

        // 注册中间件管道组件（手动注册，框架程序集不使用自动注册）
        // 每个中间件同时注册具体类型和 IAiMiddleware 接口转发，支持用户通过接口扩展
        AddAiMiddlewareSingleton<RetryMiddleware>(services, forwardAsSingleton: true);
        AddAiMiddleware<ThinkingMiddleware>(services);
        AddAiMiddleware<PromptCachingMiddleware>(services);
        AddAiMiddleware<QuotaMiddleware>(services);
        AddAiMiddleware<InputGuardrailMiddleware>(services);
        AddAiMiddleware<HistoryMiddleware>(services);

        // Scoped because the constructor consumes Scoped IContextProviderContributor instances
        // (e.g. MemoryContextProvider depends on Scoped repositories). Cost of per-request
        // construction is amortised by caching the contributor array internally.
        services.TryAddScoped<CompositeContextProviderFactory>();
        AddAiMiddleware<ContextInjectionMiddleware>(services);
        AddAiMiddleware<UsageLoggingMiddleware>(services);
        AddAiMiddleware<OutputGuardrailMiddleware>(services);

        AddToolMiddleware<ToolGuardrailMiddleware>(services);
        AddAiMiddlewareSingleton<LoopDetectionMiddleware>(services);
        AddToolMiddleware<ToolErrorRecoveryMiddleware>(services);

        AddAiMiddleware<SubAgentLimitMiddleware>(services);
        AddAiMiddleware<DeferredToolFilterMiddleware>(services);

        AddAiMiddleware<SummarizationMiddleware>(services);
        AddAiMiddleware<FileUploadMiddleware>(services);
        AddAiMiddleware<ViewImageMiddleware>(services);
        AddAiMiddleware<TodoMiddleware>(services);
        AddAiMiddleware<ClarificationMiddleware>(services);

        // Document converter (default: native .NET, can be overridden with CliDocumentConverter)
        services.TryAddSingleton<IDocumentConverter, NativeDocumentConverter>();

        // 注册 Runtime（统一 AI 执行入口 + Run/Trace 持久化）
        services.AddScoped<IRunStore, RunStore>();
        services.AddScoped<ITraceStore, TraceStore>();
        services.AddScoped<IRunTracker, RunTracker>();
        services.AddScoped<IEventPublisher>(sp => new EventPublisher(
            sp.GetService<IEventBus>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<EventPublisher>>()));
        services.AddScoped<IWorkflowDelegator, WorkflowDelegator>();
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

        // Phase 3: AI 工具（Clarification / Todo / Artifact）
        services.TryAddScoped<ClarificationTools>();
        services.TryAddScoped<TodoTools>();
        services.TryAddScoped<ArtifactTools>();
        services.TryAddScoped<AgentRunControlTools>();

        // IAiUtility — 轻量级系统级 AI 调用
        services.TryAddScoped<IAiUtility, AiUtilityService>();

        // Workspace agent provider (file-based AGENT.md discovery)
        services.AddSingleton<IWorkspaceAgentProvider, WorkspaceAgentProvider>();
        // Admin-facing read-only listing of workspace agents/personas
        services.AddScoped<IWorkspaceAgentAdminService, WorkspaceAgentAdminService>();

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

        // SQL tool suite — manual registration per framework rule #1.
        // Permission check defaults to DenyAll (fail-secure); applications opt into a permissive
        // implementation by replacing this registration with FrameworkPermissionSqlCheck or
        // their own IReadOnlySqlPermissionCheck. The DbConnection factory MUST be registered
        // by the application — without it, IReadOnlySqlExecutor cannot be resolved.
        services.AddSingleton<Tools.Sql.ISqlValidator, Tools.Sql.RestrictiveSqlValidator>();
        services.AddSingleton<Tools.Sql.ISqlColumnInferrer, Tools.Sql.HeuristicSqlColumnInferrer>();
        services.AddSingleton<Tools.Sql.ISqlSchemaProvider, Tools.Sql.TSqlSchemaProvider>();
        services.AddSingleton<Tools.Sql.ISqlSchemaProvider, Tools.Sql.PostgreSqlSchemaProvider>();
        services.AddSingleton<Tools.Sql.ISqlSchemaProvider, Tools.Sql.MySqlSchemaProvider>();
        services.AddSingleton<Tools.Sql.ISqlSchemaProvider, Tools.Sql.SqliteSchemaProvider>();
        services.AddScoped<Tools.Sql.IReadOnlySqlExecutor, Tools.Sql.ReadOnlySqlExecutor>();
        services.AddScoped<Tools.Sql.ISchemaInspector, Tools.Sql.SchemaInspector>();
        services.TryAddScoped<Tools.Sql.IReadOnlySqlPermissionCheck, Tools.Sql.DenyAllSqlPermissionCheck>();

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

    // Validation and tool-registration helpers live in AIModule.Validation.cs
    // (partial class) to keep this file focused on module wiring.
}
