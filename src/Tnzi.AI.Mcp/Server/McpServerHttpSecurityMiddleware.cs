namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP HTTP/SSE 端点安全中间件。
/// </summary>
public class McpServerHttpSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly McpServerSecurityMiddleware _security;

    public McpServerHttpSecurityMiddleware(
        RequestDelegate next,
        IOptionsMonitor<McpServerOptions> options,
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

        if (_options.CurrentValue.RateLimitPerTenant)
        {
            context.Items[McpServerSecurityMiddleware.TenantHeaderName] =
                _security.ExtractTenantId(context.Request);
        }

        // Store the hashed caller segment so downstream audit logging can record
        // which (hashed) key made the call - enables UniqueCallers statistics.
        // The hash is already embedded in clientKey as "{tenant}:{hash16}"; extract it.
        var colonIndex = clientKey.IndexOf(':');
        if (colonIndex >= 0 && colonIndex < clientKey.Length - 1)
        {
            context.Items[McpServerSecurityMiddleware.CallerHashItemKey] =
                clientKey[(colonIndex + 1)..];
        }

        await _next(context);
    }
}
