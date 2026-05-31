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
                var gatewayOptions = context.RequestServices.GetRequiredService<IOptions<GatewayOptions>>();

                var handler = new GatewayWebSocketHandler(gateway, presence, logger,
                    gatewayOptions.Value.MaxConnectionsPerUser,
                    gatewayOptions.Value.HeartbeatIntervalSeconds,
                    gatewayOptions.Value.RequireAuthentication);
                var userId = context.User?.Identity?.IsAuthenticated == true
                    ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    : null;

                // 要求认证时，在 AcceptWebSocketAsync 之前用 HTTP 401 拒绝匿名连接
                if (gatewayOptions.Value.RequireAuthentication && string.IsNullOrEmpty(userId))
                {
                    logger.LogWarning("Rejecting anonymous WebSocket connection with 401: RequireAuthentication is enabled");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                await handler.HandleAsync(ws, userId, context.RequestAborted);
            }
            else
            {
                await next();
            }
        });

        return app;
    }
}
