using ModelContextProtocol.AspNetCore;
using Tnzi.AI.Mcp.Options;
using Tnzi.AI.Mcp.Services;
using Tnzi.AI.Mcp.Services.Interfaces;
using TnziMcpServerOptions = Tnzi.AI.Mcp.Options.McpServerOptions;

namespace Tnzi.AI.Mcp;

/// <summary>
/// MCP Server 模块 — 将 Tnzi.AI Agent 暴露为 MCP Server，支持 HTTP/SSE 和 stdio 传输
/// </summary>
[DependsOn(typeof(AIModule))]
public class AIMcpModule : TnziApplicationModule
{
    /// <summary>
    /// 表名前缀（与 AI 核心共享）
    /// </summary>
    public override string? TableNamePrefix => "AI";

    /// <summary>
    /// 加载顺序（在 AIModule(50) 之后）
    /// </summary>
    public override int LoadOrder => 53;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<TnziMcpServerOptions>()
            .Bind(context.Configuration.GetSection("AI:McpServer"))
            .ValidateWith<TnziMcpServerOptions, McpServerOptionsValidator>();

        context.Services.AddOptions<McpOAuthOptions>()
            .Bind(context.Configuration.GetSection("AI:McpServer:OAuth"));

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var mcpServerOptions = context.Configuration.GetSection("AI:McpServer").Get<TnziMcpServerOptions>() ?? new TnziMcpServerOptions();
        var mcpHttpServiceProviderAccessor = new McpServerServiceProviderAccessor();
        var mcpHttpHandlerBridge = new McpServerHttpHandlerBridge(mcpHttpServiceProviderAccessor);

        // MCP Tool Analytics
        services.AddScoped<IMcpToolAnalyticsService, McpToolAnalyticsService>();

        // MCP OAuth Token Manager（Singleton — per-server 锁和缓存）
        services.AddSingleton<IMcpOAuthTokenManager, McpOAuthTokenManager>();

        // MCP Server Host（可选，通过 AI:McpServer:Enabled 激活）— Singleton 以保持速率限制状态
        services.AddSingleton<McpServerSecurityMiddleware>();
        services.AddSingleton(mcpHttpServiceProviderAccessor);
        services.AddSingleton(mcpHttpHandlerBridge);
        // McpServerHttpSecurityMiddleware is convention-based ASP.NET middleware (RequestDelegate ctor)
        // — instantiated by UseMiddleware<T>(), NOT by DI container
        services.TryAddSingleton<IMcpServerHost, McpServerHost>();
        services.AddHostedService<McpServerHostedService>();

        if (mcpServerOptions.Enabled && !string.Equals(mcpServerOptions.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            services.AddMcpServer(serverOptions =>
                {
                    serverOptions.ServerInfo = new()
                    {
                        Name = "Tnzi.AI",
                        Version = typeof(AIMcpModule).Assembly.GetName().Version?.ToString() ?? "1.0.0"
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
        var logger = serviceProvider.GetRequiredService<ILogger<AIMcpModule>>();
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

        return Task.CompletedTask;
    }
}
