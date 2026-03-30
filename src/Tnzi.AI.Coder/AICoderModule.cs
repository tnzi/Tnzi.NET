namespace Tnzi.AI.Coder;

/// <summary>
/// AI Coder 模块 — 提供文件系统、Shell、代码搜索、记忆、项目上下文等本地编码工具
/// </summary>
/// <remarks>
/// <para>
/// 此模块为 CLI AI 编码助手场景（类似 Claude Code）提供本地操作能力。
/// 仅依赖 Tnzi 核心项目（工具框架、记忆存储、项目上下文接口）。
/// </para>
/// <para>
/// 与 Tnzi.AI 模块（AIModule）互不依赖，可独立使用或协作：
/// - TnziCustomModule (ModuleType=400) 天然晚于 TnziApplicationModule (ModuleType=300) 加载
/// - 共享服务（ToolScanner, ToolRegistry, IToolApprovalHandler）通过 TryAdd 避免重复注册
/// - IMemoryStore 通过 TryAdd 注册文件存储（AIModule 先加载时使用数据库存储）
/// </para>
/// </remarks>
public class AICoderModule : TnziCustomModule
{
    /// <summary>
    /// 加载顺序（在 AIModule 之后）
    /// </summary>
    public override int LoadOrder => 54;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<CoderOptions>()
            .Bind(context.Configuration.GetSection("AI:Coder"))
            .ValidateWith<CoderOptions, CoderOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册命名 HttpClient（WebTools SSRF 防护 + 统一配置）
        services.AddHttpClient("Tnzi.AI.Coder", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Tnzi-AI-Coder/1.0");
        });

        // DuckDuckGo 搜索专用 HttpClient（浏览器 UA，30 秒超时）
        services.AddHttpClient("Tnzi.AI.Coder.DuckDuckGo", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; TnziAgent/1.0)");
        });

        // 安全组件
        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddSingleton<ICommandSanitizer, CommandSanitizer>();

        // 项目上下文加载器
        services.AddSingleton<IProjectContextLoader, DefaultProjectContextLoader>();

        // Web 搜索提供者（DuckDuckGo 默认实现，TryAdd: 用户可替换为商业 API）
        services.TryAddSingleton<IWebSearchProvider, DuckDuckGoSearchProvider>();

        // 工具基础设施（TryAdd: 如果 AIModule 已注册则跳过）
        services.TryAddSingleton<IToolScanner, ToolScanner>();
        services.TryAddSingleton<IToolRegistry, ToolRegistry>();
        services.TryAddSingleton<IToolApprovalHandler, AutoApprovalHandler>();

        // 文件记忆存储（TryAdd: AIModule 已注册 DatabaseMemoryStore 时跳过）
        services.TryAddSingleton<IMemoryStore, FileMemoryStore>();

        // 手动注册所有工具提供者（框架程序集 MUST 手动注册）
        // 工具类是无状态函数容器，其依赖均为 Singleton，必须注册为 Singleton
        // （因为 ToolAdapter 从 Singleton ServiceProvider 解析工具实例）
        services.AddSingleton<FileSystemTools>();
        services.AddSingleton<ShellTools>();
        services.AddSingleton<CodeSearchTools>();
        services.AddSingleton<WebTools>();
        services.AddSingleton<MemoryTools>();
        services.AddSingleton<ProjectTools>();
        services.AddSingleton<ProcessTools>();
        services.AddSingleton<GitTools>();
        services.AddSingleton<DiffTools>();
        services.AddSingleton<ReplTools>();
        services.AddSingleton<ContextTools>();
        services.AddSingleton<TaskTrackerTools>();

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // 扫描并注册本模块的工具到 ToolRegistry
        var serviceProvider = context.ServiceProvider;
        var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
        var toolScanner = serviceProvider.GetRequiredService<IToolScanner>();
        var logger = serviceProvider.GetRequiredService<ILogger<AICoderModule>>();

        try
        {
            var assembly = typeof(AICoderModule).Assembly;
            var tools = toolScanner.ScanAssembly(assembly);
            var count = 0;

            foreach (var tool in tools)
            {
                toolRegistry.Register(tool);
                count++;
            }

            logger.LogInformation("Registered {Count} AI coder tools from {Assembly}",
                count, assembly.GetName().Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to scan and register AI coder tools");
        }

        return Task.CompletedTask;
    }

    public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        // 终止所有后台托管进程，防止资源泄漏
        var logger = context.ServiceProvider.GetRequiredService<ILogger<AICoderModule>>();
        try
        {
            ProcessRegistry.KillAllAndCleanup();
            logger.LogDebug("Cleaned up all managed background processes");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during managed process cleanup");
        }

        return Task.CompletedTask;
    }
}
