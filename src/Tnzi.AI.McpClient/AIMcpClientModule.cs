namespace Tnzi.AI.McpClient;

/// <summary>
/// MCP Client 管理模块 — 管理本框架作为 MCP Client 连接外部 MCP Server 的配置、
/// OAuth Token 生命周期和注册表 CRUD，与 AIMcpModule（暴露 Tnzi 自身为 MCP Server）完全独立。
/// </summary>
[DependsOn(typeof(AIModule))]
public class AIMcpClientModule : TnziApplicationModule
{
    /// <summary>
    /// 表名前缀（与 AI 核心共享 "AI_" 前缀）
    /// </summary>
    public override string? TableNamePrefix => "AI";

    /// <summary>
    /// 加载顺序（在 AIModule(50) 之后；避开 AICoderModule 占用的 54，用空位 59）
    /// </summary>
    public override int LoadOrder => 59;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<McpClientOAuthOptions>()
            .Bind(context.Configuration.GetSection("AI:McpClient:OAuth"));

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // MCP Server Registration registry — entity-driven CRUD for external MCP server client registrations.
        // AuthToken encrypted via IDataProtectionProvider (registered by AIModule.AddDataProtection()).
        services.AddScoped<IMcpServerRegistryService, McpServerRegistryService>();

        // MCP OAuth Token Manager — per-server SemaphoreSlim, double-checked locking, proactive refresh
        services.AddSingleton<IMcpOAuthTokenManager, McpOAuthTokenManager>();

        // Named HttpClient for OAuth token fetching
        services.AddHttpClient("Tnzi.AI.MCP.OAuth");

        return Task.CompletedTask;
    }
}
