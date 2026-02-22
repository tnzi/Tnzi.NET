using McpClientFactory = Tnzi.AI.Infrastructure.Mcp.McpClientFactory;

namespace Tnzi.AI;

/// <summary>
/// AI 模块 - 基于 Microsoft.Extensions.AI 的自定义 Agent 引擎
/// </summary>
[DependsOn(typeof(EFCoreModule))]
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
        services.AddHttpClient("Tnzi.AI.Resilient")
            .AddStandardResilienceHandler(options =>
            {
                // 配置重试策略
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
                options.Retry.MaxDelay = TimeSpan.FromSeconds(10);

                // 配置熔断器策略
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                options.CircuitBreaker.MinimumThroughput = 5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });

        // 注册基础设施
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IAgentFactory, AgentFactory>();
        services.AddSingleton<WorkflowBuilderFactory>();
        services.AddSingleton<ToolScanner>();

        // MCP：连接工厂与工具提供者（启用 AI:Mcp:Enabled 时生效）。IMcpToolProvider 为 Singleton 以匹配 IAgentFactory 生命周期，避免 captive dependency。
        services.AddSingleton<IMcpClientFactory, McpClientFactory>();
        services.AddSingleton<IMcpToolProvider, McpToolProvider>();

        // 注册执行管道
        services.AddScoped<ChatExecutionPipeline>();

        // 注册核心服务
        services.AddScoped<IUsageLogService, UsageLogService>();
        services.AddScoped<IQuotaService, QuotaService>();
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
        services.TryAddScoped<IToolApprovalHandler, AutoApprovalHandler>();

        // 注册内置工具（默认提供，运行时根据配置决定是否使用）
        services.TryAddScoped<DateTimeTools>();
        services.TryAddScoped<MathTools>();
        services.TryAddScoped<TextTools>();

        // 扫描并注册所有工具
        ScanAndRegisterTools(services);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 工具扫描缓存 - 避免重复扫描相同程序集
    /// </summary>
    private static readonly ConcurrentDictionary<string, IEnumerable<Tools.Models.ToolDefinition>> _toolScanCache = new();

    /// <summary>
    /// 扫描并注册所有工具
    /// </summary>
    private static void ScanAndRegisterTools(IServiceCollection services)
    {
        // 延迟注册，在服务提供者构建后执行
        services.AddSingleton<ToolRegistry>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<ToolRegistry>>();
            var registry = new ToolRegistry(logger);
            var scanner = serviceProvider.GetRequiredService<ToolScanner>();

            // 扫描当前程序集
            var assembly = Assembly.GetExecutingAssembly();
            var tools = GetOrScanAssembly(scanner, assembly);

            // 注册到注册表
            foreach (var tool in tools)
            {
                registry.Register(tool);
            }

            // 扫描应用程序集（用户定义的工具）
            var appAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic
                    && a.FullName != null
                    && !a.FullName.StartsWith("System.", StringComparison.Ordinal)
                    && !a.FullName.StartsWith("Microsoft.", StringComparison.Ordinal)
                    && !a.FullName.StartsWith("Tnzi.", StringComparison.Ordinal)
                    && a != assembly);

            foreach (var appAssembly in appAssemblies)
            {
                try
                {
                    var appTools = GetOrScanAssembly(scanner, appAssembly);
                    foreach (var tool in appTools)
                    {
                        registry.Register(tool);
                    }
                }
                catch (Exception ex)
                {
                    // 记录扫描失败的程序集，但继续扫描其他程序集
                    var moduleLogger = serviceProvider.GetService<ILogger<AIModule>>();
                    moduleLogger?.LogWarning(ex,
                        "Failed to scan assembly '{AssemblyName}' for AI tools. Skipping this assembly.",
                        appAssembly.FullName);
                }
            }

            return registry;
        });
    }

    /// <summary>
    /// 获取或扫描程序集的工具定义（使用缓存）
    /// </summary>
    private static IEnumerable<Tools.Models.ToolDefinition> GetOrScanAssembly(ToolScanner scanner, Assembly assembly)
    {
        var assemblyName = assembly.GetName().FullName;
        return _toolScanCache.GetOrAdd(assemblyName, _ => scanner.ScanAssembly(assembly));
    }
}