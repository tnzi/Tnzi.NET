namespace Tnzi.AI.Device.Transport;

/// <summary>
/// Extension methods to mount the Device WebSocket endpoint into the ASP.NET Core middleware pipeline.
/// </summary>
public static class DeviceWebSocketMiddleware
{
    /// <summary>
    /// Enables the device WebSocket endpoint at <paramref name="path"/> (default <c>/ws/device</c>).
    /// Requires <c>UseWebSockets()</c> to be present in the pipeline — this call adds it if not already present.
    /// </summary>
    public static IApplicationBuilder UseDeviceWebSocket(this IApplicationBuilder app, string path = "/ws/device")
    {
        // UseWebSockets 是幂等的，重复调用安全
        app.UseWebSockets();

        app.Use(async (context, next) =>
        {
            if (context.Request.Path == path && context.WebSockets.IsWebSocketRequest)
            {
                var handler = context.RequestServices.GetRequiredService<WebSocketDeviceConnectionHandler>();
                await handler.HandleAsync(context, context.RequestAborted);
            }
            else
            {
                await next();
            }
        });

        return app;
    }
}
