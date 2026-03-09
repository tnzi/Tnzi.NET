using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using McpClientFactory = Tnzi.AI.Infrastructure.Mcp.McpClientFactory;
using TnziMcpServerOptions = Tnzi.AI.Options.McpServerOptions;

namespace Tnzi.AI;

/// <summary>
/// AI 模块 - 基于 Microsoft.Extensions.AI 的自定义 Agent 引擎
/// </summary>
[DependsOn(typeof(EFCoreModule))]
[DependsOn(typeof(Tnzi.AspNetCore.AspNetCoreModule))]
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

        context.Services.AddOptions<TnziMcpServerOptions>()
            .Bind(context.Configuration.GetSection("AI:McpServer"))
            .ValidateWith<TnziMcpServerOptions, McpServerOptionsValidator>();

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
        var mcpServerOptions = context.Configuration.GetSection("AI:McpServer").Get<TnziMcpServerOptions>() ?? new TnziMcpServerOptions();
        var mcpHttpServiceProviderAccessor = new McpServerServiceProviderAccessor();
        var mcpHttpHandlerBridge = new McpServerHttpHandlerBridge(mcpHttpServiceProviderAccessor);

        // 注册 HttpClient 工厂
        services.AddHttpClient();

        // 注册推理内容捕获 Handler（拦截 SSE 流提取 reasoning_content）
        services.AddTransient<ReasoningCapturingHandler>();

        // 配置带重试和熔断的命名 HttpClient（用于 AI 提供商调用）
        services.AddHttpClient("Tnzi.AI.Resilient")
            .AddHttpMessageHandler<ReasoningCapturingHandler>()
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

        // 注册基础设施
        services.AddSingleton<IChatClientProvider, OpenAIChatClientProvider>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
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
        services.AddSingleton<IMcpClientFactory, McpClientFactory>();
        services.AddSingleton<IMcpToolProvider, McpToolProvider>();

        // Token 估算器（TryAdd：允许用户注册 tiktoken 等精确实现）
        services.TryAddSingleton<ITokenEstimator, HeuristicTokenEstimator>();

        // 注册 Agent 解析器
        services.AddScoped<IAgentResolver, AgentResolver>();

        // 注册对话存储和记忆存储（使用 TryAdd，允许 Agent 模块替换）
        services.TryAddScoped<IConversationStore, DatabaseConversationStore>();
        services.TryAddScoped<IMemoryStore, DatabaseMemoryStore>();

        // 注册实体记忆存储和 LLM 实体抽取器
        services.TryAddScoped<IEntityMemoryStore, DatabaseEntityMemoryStore>();
        services.AddScoped<LlmEntityExtractor>();

        // 注册项目上下文提供器（从 DI 获取 IProjectContextLoader）
        services.AddScoped<ProjectContextProvider>();

        // 注册 Prompt 模板引擎（TryAdd：允许用户注册自定义模板引擎）
        services.TryAddScoped<IPromptTemplateEngine, SimplePromptTemplateEngine>();

        // 注册核心服务
        services.AddScoped<IUsageLogService, UsageLogService>();
        services.AddScoped<IUsageAnalyticsService, UsageAnalyticsService>();
        services.AddScoped<QuotaService>();
        services.AddScoped<IQuotaService>(sp => sp.GetRequiredService<QuotaService>());
        services.TryAddScoped<IQuotaProvider>(sp => sp.GetRequiredService<QuotaService>());
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<AgentThreadService>();
        services.AddScoped<IAgentThreadService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IAgentThreadInternalService>(sp => sp.GetRequiredService<AgentThreadService>());
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<IStructuredOutputService, StructuredOutputService>();

        // 注册 RAG 相关服务（使用 TryAdd 允许用户覆盖）
        // 用户可以注册自己的 ITextSearchService 实现来连接到向量存储（Redis、Qdrant、Pinecone 等）
        services.TryAddScoped<ITextSearchService, NoOpTextSearchService>();

        // 注册技能加载器与按需工具提供者
        services.AddSingleton<SkillLoader>();
        services.AddSingleton<SkillToolsProvider>();

        // 注册工具审批处理器（使用 TryAdd 允许用户覆盖）
        // 用户可以注册自己的 IToolApprovalHandler 实现来实现自定义审批逻辑
        services.TryAddSingleton<IToolApprovalHandler, AutoApprovalHandler>();

        // 注册内置工具（默认提供，运行时根据配置决定是否使用）
        services.TryAddScoped<DateTimeTools>();
        services.TryAddScoped<TextTools>();
        services.TryAddScoped<WebSearchTools>();

        // 注册工作流检查点存储（TryAdd：允许用户注册自定义实现）
        services.TryAddScoped<IWorkflowCheckpointStore, DatabaseWorkflowCheckpointStore>();

        // 注册 OpenAPI 工具生成器（运行时通过 OpenApiToolsOptions.Enabled 控制是否生效）
        services.AddSingleton<OpenApiToolGenerator>();

        // 注册 Guardrails（运行时通过 GuardrailsOptions.Enabled 控制是否生效）
        services.AddScoped<GuardrailRunner>();
        services.AddScoped<IInputGuardrail, MaxLengthGuardrail>();
        services.AddScoped<IInputGuardrail, PromptInjectionGuardrail>();
        services.AddScoped<IInputGuardrail, PiiDetectionGuardrail>();
        services.AddScoped<IOutputGuardrail, ContentFilterGuardrail>();

        // LLM-as-Judge guardrail（同时作为输入和输出 guardrail）
        services.AddScoped<LlmJudgeGuardrail>();
        services.AddScoped<IInputGuardrail>(sp => sp.GetRequiredService<LlmJudgeGuardrail>());
        services.AddScoped<IOutputGuardrail>(sp => sp.GetRequiredService<LlmJudgeGuardrail>());

        // 注册中间件管道组件（手动注册，框架程序集不使用自动注册）
        services.AddScoped<QuotaMiddleware>();
        services.AddScoped<InputGuardrailMiddleware>();
        services.AddScoped<HistoryMiddleware>();
        services.AddScoped<ContextInjectionMiddleware>();
        services.AddScoped<UsageLoggingMiddleware>();
        services.AddScoped<OutputGuardrailMiddleware>();

        // 注册 Runtime（统一 AI 执行入口 + Run/Trace 持久化）
        services.AddScoped<IRunStore, RunStore>();
        services.AddScoped<ITraceStore, TraceStore>();
        services.AddScoped<IAgentRuntime, AgentRuntime>();

        // 注册 Run 管理服务（查询、取消、审批、重试）
        services.AddScoped<IAgentRunService, AgentRunService>();
        services.AddScoped<IAgentTraceService, AgentTraceService>();

        // 注册工作流引擎组件
        services.AddScoped<WorkflowNodeExecutor>();
        services.AddScoped<WorkflowEngine>();

        // 注册工作流节点
        services.AddScoped<IWorkflowNode, Nodes.AgentNode>();
        services.AddScoped<IWorkflowNode, Nodes.ReviewNode>();
        services.AddScoped<IWorkflowNode, Nodes.ApprovalNode>();
        services.AddScoped<IWorkflowNode, Nodes.RouterNode>();
        services.AddScoped<IWorkflowNode, Nodes.ParallelNode>();
        services.AddScoped<IWorkflowNode, Nodes.SynthesizeNode>();
        services.AddScoped<IWorkflowNode, Nodes.DebateNode>();
        services.AddScoped<IWorkflowNode, Nodes.ConditionalNode>();
        services.AddScoped<IWorkflowNode, Nodes.TransformNode>();

        // 注册 A2A 客户端（TryAdd：允许用户注册自定义实现）
        services.TryAddScoped<IA2AClient, HttpA2AClient>();

        // 注册 Agent 评估器（TryAdd：允许用户注册自定义实现）
        services.TryAddScoped<IAgentEvaluator, DefaultAgentEvaluator>();

        // MCP Server Host（可选，通过 AI:McpServer:Enabled 激活）— Singleton 以保持速率限制状态
        services.AddSingleton<McpServerSecurityMiddleware>();
        services.AddSingleton(mcpHttpServiceProviderAccessor);
        services.AddSingleton(mcpHttpHandlerBridge);
        services.AddScoped<McpServerHttpSecurityMiddleware>();
        services.TryAddSingleton<IMcpServerHost, McpServerHost>();
        services.AddHostedService<McpServerHostedService>();

        if (mcpServerOptions.Enabled && !string.Equals(mcpServerOptions.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            services.AddMcpServer(serverOptions =>
                {
                    serverOptions.ServerInfo = new()
                    {
                        Name = "Tnzi.AI",
                        Version = typeof(AIModule).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                    };
                })
                .WithHttpTransport(options =>
                {
                    options.Stateless = false;
                })
                .WithListToolsHandler(mcpHttpHandlerBridge.HandleListToolsAsync)
                .WithCallToolHandler(mcpHttpHandlerBridge.HandleCallToolAsync);
        }

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
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

        var mcpServerOptions = serviceProvider.GetRequiredService<IOptions<TnziMcpServerOptions>>().Value;
        if (mcpServerOptions.Enabled && !string.Equals(mcpServerOptions.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            var mcpServiceProviderAccessor = serviceProvider.GetRequiredService<McpServerServiceProviderAccessor>();
            mcpServiceProviderAccessor.ServiceProvider = serviceProvider;

            if (context.App != null)
            {
                context.App.UseWhen(
                    httpContext => httpContext.Request.Path.StartsWithSegments(mcpServerOptions.Endpoint, StringComparison.OrdinalIgnoreCase),
                    branch => branch.UseMiddleware<McpServerHttpSecurityMiddleware>());
            }

            if (context.WebApp != null)
            {
                context.WebApp.MapMcp(mcpServerOptions.Endpoint);
                logger.LogInformation("Mapped MCP HTTP/SSE endpoint at {Endpoint}", mcpServerOptions.Endpoint);
            }
            else
            {
                logger.LogWarning(
                    "MCP Server transport '{Transport}' is enabled but no WebApplication is available. HTTP/SSE endpoint was not mapped.",
                    mcpServerOptions.Transport);
            }
        }

        // 根据 BuiltInToolsOptions 按 ProviderType 精确移除已禁用的内置工具
        // 使用 UnregisterByProviderType 而非 UnregisterGroup，避免误删用户注册的同名工具组
        var builtInOptions = serviceProvider.GetRequiredService<IOptions<AIOptions>>().Value.BuiltInTools;
        if (!builtInOptions.Enabled)
        {
            toolRegistry.UnregisterByProviderType(typeof(DateTimeTools));
            toolRegistry.UnregisterByProviderType(typeof(TextTools));
            toolRegistry.UnregisterByProviderType(typeof(WebSearchTools));
        }
        else
        {
            if (!builtInOptions.EnableDateTime) toolRegistry.UnregisterByProviderType(typeof(DateTimeTools));
            if (!builtInOptions.EnableText) toolRegistry.UnregisterByProviderType(typeof(TextTools));
            if (!builtInOptions.EnableWebSearch) toolRegistry.UnregisterByProviderType(typeof(WebSearchTools));
        }

        return Task.CompletedTask;
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
            if (textSearchService is NoOpTextSearchService
                && (options.ContextProviders.TextSearch.Enabled || options.ContextProviders.ChatHistoryMemory.Enabled))
            {
                throw new InvalidOperationException(
                    "AI text search or chat history memory is enabled, but ITextSearchService is still the default NoOpTextSearchService. Register a real ITextSearchService implementation.");
            }

            if (options.ContextProviders.Skills.Enabled && options.ContextProviders.Skills.Paths.Count == 0)
            {
                throw new InvalidOperationException(
                    "AI:ContextProviders:Skills is enabled, but no skill paths are configured.");
            }
        }

        logger.LogDebug("AI runtime configuration validation passed.");
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
