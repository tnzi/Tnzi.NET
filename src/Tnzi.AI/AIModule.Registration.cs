using Tnzi.AI.Tools.Sql;
using McpClientFactory = Tnzi.AI.Infrastructure.Mcp.McpClientFactory;

namespace Tnzi.AI;

/// <summary>
/// AIModule 的服务注册细分（partial）。
/// <para>
/// 引擎注册：<see cref="AIModule.ConfigureServicesAsync"/> 仅作瘦编排器，按原有顺序依次调用下列私有
/// 方法；各方法之间的相对顺序必须保持，以维持 TryAdd「先注册者胜」语义不变。
/// </para>
/// <para>
/// NoOp 回退：<see cref="RegisterOptionalSubmoduleFallbacks"/> 由
/// <see cref="AIModule.PostConfigureServicesAsync"/> 调用（所有模块 Configure 之后）——
/// 子模块（Workflow/Skills/Rag）在 Configure 阶段的任意注册（TryAdd 或 Add）都先于回退，
/// 回退的 TryAdd 见已存在即跳过，因此真实实现永远胜出，无须 RemoveAll+Add 约定。
/// </para>
/// </summary>
public partial class AIModule
{
    // ───────────────────────── 可选子模块 NoOp 回退（PostConfigure 阶段） ─────────────────────────

    private static void RegisterOptionalSubmoduleFallbacks(IServiceCollection services)
    {
        // 可选子模块回退实现：允许不加载子模块时仍能解析核心服务
        services.TryAddScoped<IWorkflowService, NoOpWorkflowService>();
        services.TryAddScoped<ISkillLoadTracker, NoOpSkillLoadTracker>();

        // Workflow 子接口转发 - NoOpWorkflowService 已实现 IWorkflowService
        //（继承 IWorkflowExecutionControlService + IWorkflowExecutionQueryService），
        // 但 DI 不会自动转发子接口，需显式注册以消除 GetService<T>() null-check 脆弱性
        services.TryAddScoped<IWorkflowExecutionControlService>(sp =>
            (IWorkflowExecutionControlService)sp.GetRequiredService<IWorkflowService>());
        services.TryAddScoped<IWorkflowExecutionQueryService>(sp =>
            (IWorkflowExecutionQueryService)sp.GetRequiredService<IWorkflowService>());

        // 其他 Category D NoOp 回退 - 已在核心被 GetService<T>() 消费
        services.TryAddScoped<IWorkflowExecutionMailbox, NoOpWorkflowExecutionMailbox>();
        services.TryAddScoped<IAgentStreamForwarder, NoOpAgentStreamForwarder>();

        // Category C NoOp 回退 - 接口在核心定义、实现在子模块，防止 DI 解析失败
        services.TryAddScoped<ISkillService, NoOpSkillService>();
        services.TryAddScoped<ISkillStore, NoOpSkillStore>();
        services.TryAddScoped<ISkillTemplateEngine, NoOpSkillTemplateEngine>();
        services.TryAddScoped<ISkillConstraintEnforcer, NoOpSkillConstraintEnforcer>();
        services.TryAddScoped<ISkillSearchService, NoOpSkillSearchService>();
        services.TryAddScoped<ISkillRequirementsValidator, NoOpSkillRequirementsValidator>();
        services.TryAddScoped<IVectorStore, NoOpVectorStore>();

        // RAG 文本检索回退 - 用户/子模块（Tnzi.AI.Rag）可注册真实 ITextSearchService 连接向量存储
        services.TryAddScoped<ITextSearchService, NoOpTextSearchService>();

        // 外部 CLI agent 回退（Tnzi.AI.Cli）。绑定服务的回退刻意不叫 NoOp 也不带
        // INoOpService：它在读路径上返回 null = 「全部走内建」，那是正确答案而非降级
        //（抛 501 会让未装子模块的部署连普通聊天都跑不起来）。
        services.TryAddScoped<ICliAgentDispatcher, NoOpCliAgentDispatcher>();
        services.TryAddScoped<ICliAgentBindingService, BuiltInOnlyCliAgentBindingService>();
        services.TryAddScoped<ICliRuntimeService, NoOpCliRuntimeService>();
    }

    // ───────────────────────── 引擎注册助手 ─────────────────────────

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

    // ───────────────────────── 引擎服务注册（Configure 阶段） ─────────────────────────

    // Per-provider resilience pipelines - Polly keys its circuit state by HttpClient
    // name, so giving each provider a unique name isolates their breakers. A 429 on
    // one provider cannot open circuits on the others.
    // Thinking injection / reasoning extraction run inside the OpenAI SDK pipeline
    // (ThinkingRequestPolicy), not as DelegatingHandlers - the latter don't compose
    // with HttpClientPipelineTransport.
    private static void ConfigureAiResilience(HttpStandardResilienceOptions options)
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

    private static void ConfigureHttpClients(ServiceConfigurationContext context, IServiceCollection services)
    {
        services.AddHttpClient();

        foreach (var providerChild in context.Configuration.GetSection("AI:Providers").GetChildren())
        {
            var providerName = providerChild.Key;
            if (string.IsNullOrWhiteSpace(providerName)) continue;

            // Skip providers explicitly disabled in config - no point wiring up a
            // circuit for a client that will never be resolved.
            if (providerChild.GetValue("Enabled", defaultValue: true) == false) continue;

            services.AddHttpClient(ResilientHttpClientNames.For(providerName))
                .AddStandardResilienceHandler(ConfigureAiResilience);
        }

        // Fallback client - shared pipeline for providers added dynamically at runtime
        // (not listed in AI:Providers). Same-name circuit state is shared across them;
        // static configuration is the recommended production setup.
        services.AddHttpClient(ResilientHttpClientNames.Fallback)
            .AddStandardResilienceHandler(ConfigureAiResilience);

        // A2A 客户端 - 禁用自动重定向以防止 SSRF 通过 302 Location 绕过 EgressGuard
        services.AddHttpClient("Tnzi.AI.A2A")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        // OAuth 令牌请求专用 HttpClient（无重试/熔断，令牌端点自行处理错误）
        services.AddHttpClient("Tnzi.AI.OAuth");

        // DuckDuckGo 搜索专用 HttpClient（浏览器 UA，30 秒超时）
        services.AddHttpClient(DuckDuckGoSearchProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; TnziAgent/1.0)");
        });
    }

    private static void RegisterChatClientsAndProcessors(IServiceCollection services)
    {
        // 注册基础设施
        services.AddSingleton<IChatClientProvider, OpenAIChatClientProvider>();
        services.AddSingleton<IChatClientProvider, AnthropicChatClientProvider>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        // Multi-model provider message processors - 扩展点，用于处理特定提供商的消息格式差异。
        // ThinkTagChatMessageProcessorBase 处理 <think> 标签（MiniMax/Kimi/GLM 共用），DeepSeek/Gemini 为预留直通。
        // 应用代码可通过 IEnumerable<IChatMessageProcessor> 注入并按 ProviderName 匹配使用。
        services.AddSingleton<IChatMessageProcessor, DeepSeekChatMessageProcessor>();
        services.AddSingleton<IChatMessageProcessor, GeminiChatMessageProcessor>();
        services.AddSingleton<IChatMessageProcessor, MiniMaxChatMessageProcessor>();
        services.AddSingleton<IChatMessageProcessor, KimiChatMessageProcessor>();
        services.AddSingleton<IChatMessageProcessor, GlmChatMessageProcessor>();
        services.AddSingleton<IAgentExecutionContextAccessor, AgentExecutionContextAccessor>();
    }

    private static void RegisterToolAndAgentInfrastructure(IServiceCollection services)
    {
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
        // IMcpServerCatalog 合并部署配置（AI:Mcp:Servers）与 DB 注册表（McpServerRegistration）为有效服务器列表。
        services.TryAddSingleton<McpOAuthClientHandler>();
        services.AddSingleton<IMcpClientFactory, McpClientFactory>();
        services.AddSingleton<IMcpServerCatalog, McpServerCatalog>();
        services.AddSingleton<IMcpToolProvider, McpToolProvider>();
        services.TryAddSingleton<IMcpResourceProvider, McpResourceProvider>();
        services.TryAddSingleton<IMcpPromptProvider, McpPromptProvider>();

        // MCP Client 管理面：外部 MCP Server 注册表 CRUD（运行时凭证经 IMcpServerCatalog 消费）。
        // Data Protection - 模块内唯一注册点（幂等，内部 TryAdd，与应用注册共存）；
        // 消费方：McpServerRegistryService/McpServerCatalog（MCP AuthToken）、ProviderService/ChatClientFactory（Provider ApiKey）。
        services.AddDataProtection();
        services.AddScoped<IMcpServerRegistryService, McpServerRegistryService>();

        // Token 估算器（TryAdd：允许用户注册 tiktoken 等精确实现）
        services.TryAddSingleton<ITokenEstimator, HeuristicTokenEstimator>();

        // Web 搜索提供者（DuckDuckGo 默认实现，TryAdd: 用户可替换为商业 API）
        services.TryAddSingleton<IWebSearchProvider, DuckDuckGoSearchProvider>();

        // 注册 Agent 解析器
        services.AddScoped<IAgentResolver, AgentResolver>();
    }

    private static void RegisterConversationMemoryAndContext(IServiceCollection services)
    {
        // 注册对话存储和记忆存储（使用 TryAdd，允许 Agent 模块替换）
        services.TryAddScoped<IConversationStore, DatabaseConversationStore>();
        services.TryAddScoped<IMemoryStore, DatabaseMemoryStore>();
        services.TryAddScoped<IMemoryConsolidator, LlmMemoryConsolidator>();
        services.TryAddScoped<IMemorySideQuery, MemorySideQuery>();

        // 注册实体记忆存储和 LLM 实体抽取器
        services.TryAddScoped<IEntityMemoryStore, DatabaseEntityMemoryStore>();
        services.AddScoped<LlmEntityExtractor>();

        // 注册上下文提供器贡献者（DI 驱动，取代 AgentExecutorOptionsBuilder 中硬编码 new）
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.TextSearchContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.ChatHistoryMemoryContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.MemoryContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.EntityMemoryContributor>();
        services.AddScoped<IContextProviderContributor, Infrastructure.ContextProviders.Contributors.SkillContributor>();

        // 注册 Prompt 模板引擎（TryAdd：允许用户注册自定义模板引擎）
        services.TryAddScoped<IPromptTemplateEngine, SimplePromptTemplateEngine>();
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        // 注册核心服务
        services.AddSingleton<ICostCalculator, CostCalculator>();
        services.AddScoped<IUsageLogService, UsageLogService>();
        services.AddScoped<IUsageAnalyticsService, UsageAnalyticsService>();
        services.AddScoped<QuotaService>();
        services.AddScoped<IQuotaService>(sp => sp.GetRequiredService<QuotaService>());
        services.TryAddScoped<IQuotaProvider>(sp => sp.GetRequiredService<QuotaService>());

        // Provider entity CRUD (Phase 5 backend prereq) - entity-driven provider management
        // with encrypted credential storage. IDataProtectionProvider 由 RegisterToolAndAgentInfrastructure
        // 中的单次 AddDataProtection() 提供（本方法在其后执行，无需重复注册）。
        services.AddScoped<IProviderService, ProviderService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<IAgentMemoryService, AgentMemoryService>();
        services.AddScoped<IAgentVersionRouter, AgentVersionRouter>();
        services.AddScoped<AgentThreadService>();
        services.AddScoped<IAgentThreadService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAgentThreadInternalService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IMessageFeedbackService, MessageFeedbackService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<IStructuredOutputService, StructuredOutputService>();

        // 注册工具审批处理器（使用 TryAdd 允许用户覆盖）
        // 用户可以注册自己的 IToolApprovalHandler 实现来实现自定义审批逻辑
        services.TryAddSingleton<IToolApprovalHandler, AutoApprovalHandler>();
        services.TryAddSingleton<IToolPermissionEvaluator, ConfiguredToolPermissionEvaluator>();
        // IShellCommandAnalyzer 在 ConfigureServicesAsync 主方法中注册（被 Sandbox 子模块消费的核心工具）

        // 注册工具权限规则持久化存储（Scoped：需要 IRepository）
        services.AddScoped<IToolPermissionRuleStore, DatabaseToolPermissionRuleStore>();
        services.AddScoped<IToolPermissionRuleService, ToolPermissionRuleService>();
        services.AddScoped<ISubAgentTypeService, SubAgentTypeService>();
    }

    private static void RegisterBuiltInToolsAndGuardrails(IServiceCollection services)
    {
        // [RequiresSkill] 兜底中间件 - 工具调用前检查 Skill 是否已加载
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
    }

    private static void RegisterMiddlewarePipeline(IServiceCollection services)
    {
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

        AddAiMiddleware<SummarizationMiddleware>(services);
        AddAiMiddleware<FileUploadMiddleware>(services);
        AddAiMiddleware<ViewImageMiddleware>(services);
        AddAiMiddleware<TodoMiddleware>(services);
        AddAiMiddleware<ClarificationMiddleware>(services);
    }

    private static void RegisterRuntimeAndRunServices(IServiceCollection services)
    {
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

        // 执行路由门面：全框架唯一的「内建 vs 外部 CLI」分支点。ChatService / AgentService
        // 注入它而不是 IAgentRuntime，于是「这个 Agent 走哪条路」的判断只存在于一个类里。
        services.AddScoped<IAgentDispatchFacade, AgentDispatchFacade>();

        // Sub-agent cancellation registry - Singleton so the background Task.Run closure
        // and the cancel signal path both see the same in-process registry.
        services.AddSingleton<ISubAgentRunCancellationRegistry, SubAgentRunCancellationRegistry>();

        // 注册 Run 管理服务（查询、取消、审批、重试、反馈）
        services.AddScoped<ISubAgentExecutionService, SubAgentExecutionService>();
        services.AddScoped<IAgentRunService, AgentRunService>();
        services.AddScoped<IAgentTraceService, AgentTraceService>();
        services.AddScoped<IAgentRunSignalDispatcher, AgentRunSignalDispatcher>();
        services.AddScoped<IAgentRuntimeControlService, AgentRuntimeControlService>();

        // 注册 Agent 授权服务（工具/技能/知识库 junction grant 的投影、reconcile、反向查询）
        services.AddScoped<IAgentGrantService, AgentGrantService>();

        // 注册 Agent 验证服务（配置有效性检查）
        services.AddScoped<IAgentValidationService, AgentValidationService>();
        services.AddScoped<IEvaluationService, EvaluationService>();
        services.AddScoped<IEvaluationMetricsService, EvaluationMetricsService>();

        // 注册 UserProfile 和 AgentArtifact 服务
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
    }

    private static void RegisterUtilitiesWorkspaceAndEvents(IServiceCollection services)
    {
        // IAiUtility - 轻量级系统级 AI 调用
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
        // Options 绑定在 PreConfigureServicesAsync 统一完成（框架约定），此处只注册服务。
        services.AddSingleton<IPortAllocator, PortAllocator>();
    }

    private static void RegisterSqlToolSuite(IServiceCollection services)
    {
        // SQL tool suite - manual registration per framework rule #1.
        // Permission check defaults to DenyAll (fail-secure); applications opt into a permissive
        // implementation by replacing this registration with FrameworkPermissionSqlCheck or
        // their own IReadOnlySqlPermissionCheck. The DbConnection factory MUST be registered
        // by the application - without it, IReadOnlySqlExecutor cannot be resolved.
        services.AddSingleton<ISqlValidator, RestrictiveSqlValidator>();
        services.AddSingleton<ISqlColumnInferrer, HeuristicSqlColumnInferrer>();
        services.AddSingleton<ISqlSchemaProvider, TSqlSchemaProvider>();
        services.AddSingleton<ISqlSchemaProvider, PostgreSqlSchemaProvider>();
        services.AddSingleton<ISqlSchemaProvider, MySqlSchemaProvider>();
        services.AddSingleton<ISqlSchemaProvider, SqliteSchemaProvider>();
        services.AddScoped<IReadOnlySqlExecutor, ReadOnlySqlExecutor>();
        services.AddScoped<ISchemaInspector, SchemaInspector>();
        services.TryAddScoped<IReadOnlySqlPermissionCheck, DenyAllSqlPermissionCheck>();
    }
}
