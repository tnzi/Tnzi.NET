namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// Gateway WebSocket 中间件扩展 — 将 WebSocket 请求路由到 GatewayWebSocketHandler
/// </summary>
public static class GatewayMiddlewareExtensions
{
    /// <summary>
    /// 启用 Gateway WebSocket 端点
    /// </summary>
    /// <param name="app">应用构建器</param>
    /// <param name="path">WebSocket 路径（默认 /ws/gateway）</param>
    public static IApplicationBuilder UseGatewayWebSocket(this IApplicationBuilder app, string path = "/ws/gateway")
    {
        app.UseWebSockets();

        app.Use(async (context, next) =>
        {
            if (context.Request.Path == path && context.WebSockets.IsWebSocketRequest)
            {
                var gateway = context.RequestServices.GetRequiredService<IGateway>();
                var presence = context.RequestServices.GetRequiredService<IPresenceTracker>();
                var logger = context.RequestServices.GetRequiredService<ILogger<GatewayWebSocketHandler>>();
                // 每次连接接受时读取 CurrentValue，使 MaxConnectionsPerUser / HeartbeatIntervalSeconds
                // 的热更新对随后建立的连接生效（既有连接保留其接受时的值）。
                var gatewayOptions = context.RequestServices.GetRequiredService<IOptionsMonitor<GatewayOptions>>().CurrentValue;

                var handler = new GatewayWebSocketHandler(gateway, presence, logger,
                    gatewayOptions.MaxConnectionsPerUser,
                    gatewayOptions.HeartbeatIntervalSeconds,
                    gatewayOptions.RequireAuthentication);
                var userId = context.User?.Identity?.IsAuthenticated == true
                    ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    : null;

                // 要求认证时，在 AcceptWebSocketAsync 之前用 HTTP 401 拒绝匿名连接
                if (gatewayOptions.RequireAuthentication && string.IsNullOrEmpty(userId))
                {
                    logger.LogWarning("Rejecting anonymous WebSocket connection with 401: RequireAuthentication is enabled");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                // 从已认证连接的租户上下文解析归属租户（匿名连接 = null，单租户行为不变）。
                // ICurrentTenant 由框架按请求作用域从用户声明解析；服务端解析，绝不信任客户端输入。
                var tenantId = !string.IsNullOrEmpty(userId)
                    ? context.RequestServices.GetService<ICurrentTenant>()?.Id
                    : null;

                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                await handler.HandleAsync(ws, userId, context.RequestAborted, tenantId);
            }
            else
            {
                await next();
            }
        });

        return app;
    }
}
