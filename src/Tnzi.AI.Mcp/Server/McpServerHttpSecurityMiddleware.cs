namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP HTTP/SSE 端点安全中间件。
/// </summary>
public class McpServerHttpSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly McpServerSecurityMiddleware _security;

    public McpServerHttpSecurityMiddleware(
        RequestDelegate next,
        McpServerSecurityMiddleware security)
    {
        _next = Check.NotNull(next);
        _security = Check.NotNull(security);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Check.NotNull(context);

        var apiKey = _security.ExtractApiKey(context.Request);
        var callerScope = await _security.ValidateCallerAsync(apiKey, context.RequestAborted);
        if (callerScope is null)
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

        // Store the caller segment so downstream audit logging can record which (hashed)
        // key made the call (UniqueCallers statistics) and tool buckets partition by it.
        // The client key IS the caller segment: no client-controlled prefix is ever part of it.
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey] = clientKey;
        // The caller scope is what makes a run-scoped credential narrower than a static key:
        // McpServerHost filters tools/list and refuses out-of-scope tools/call by it.
        context.Items[McpServerSecurityMiddleware.CallerScopeItemKey] = callerScope;

        await _next(context);
    }
}
