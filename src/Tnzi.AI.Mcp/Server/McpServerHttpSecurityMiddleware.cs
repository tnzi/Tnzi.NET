namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP HTTP/SSE 端点安全中间件。
/// </summary>
public class McpServerHttpSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptions<McpServerOptions> _options;
    private readonly McpServerSecurityMiddleware _security;

    public McpServerHttpSecurityMiddleware(
        RequestDelegate next,
        IOptions<McpServerOptions> options,
        McpServerSecurityMiddleware security)
    {
        _next = Check.NotNull(next);
        _options = Check.NotNull(options);
        _security = Check.NotNull(security);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Check.NotNull(context);

        var apiKey = _security.ExtractApiKey(context.Request);
        if (!_security.ValidateApiKey(apiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized MCP request.");
            return;
        }

        var clientKey = _security.BuildClientKey(context, apiKey);
        if (!_security.CheckRateLimit(clientKey))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("MCP rate limit exceeded.");
            return;
        }

        if (_options.Value.TenantIsolation)
        {
            context.Items[McpServerSecurityMiddleware.TenantHeaderName] =
                _security.ExtractTenantId(context.Request);
        }

        await _next(context);
    }
}
