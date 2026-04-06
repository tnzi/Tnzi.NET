namespace Tnzi.AI.Coder;

/// <summary>
/// AI Coder 模块 — 提供文件系统、Shell、代码搜索、记忆、项目上下文等本地编码工具
/// </summary>
/// <remarks>
/// 此模块为 CLI AI 编码助手场景（类似 Claude Code）提供本地操作能力。
/// 依赖 AIModule 提供的工具基础设施（IToolScanner, IToolRegistry）和记忆存储（IMemoryStore）。
/// </remarks>
[DependsOn(typeof(AIModule))]
public class AICoderModule : TnziApplicationModule
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
        services.TryAddSingleton<BashShellAdapter>();
        services.TryAddSingleton<PowerShellShellAdapter>();
        services.TryAddSingleton<IShellAdapter>(sp => OperatingSystem.IsWindows()
            ? sp.GetRequiredService<PowerShellShellAdapter>()
            : sp.GetRequiredService<BashShellAdapter>());

        // 项目上下文加载器
        services.AddSingleton<IProjectContextLoader, DefaultProjectContextLoader>();

        // Web 搜索提供者（DuckDuckGo 默认实现，TryAdd: 用户可替换为商业 API）
        services.TryAddSingleton<IWebSearchProvider, DuckDuckGoSearchProvider>();

        // 手动注册所有工具提供者（框架程序集 MUST 手动注册）
        // 工具类是无状态函数容器，其依赖均为 Singleton，必须注册为 Singleton
        // （因为 ToolAdapter 从 Singleton ServiceProvider 解析工具实例）
        services.AddSingleton<FileSystemTools>();
        services.AddSingleton<ShellTools>();
        services.AddSingleton<BashTools>();
        services.AddSingleton<PowerShellTools>();
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
