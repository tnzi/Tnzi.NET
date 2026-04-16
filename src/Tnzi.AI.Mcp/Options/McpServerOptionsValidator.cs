namespace Tnzi.AI.Mcp.Options;

/// <summary>
/// MCP Server 配置选项验证器
/// </summary>
public class McpServerOptionsValidator : OptionsValidatorBase<McpServerOptions>
{
    protected override void ValidateOptions(McpServerOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.Transport))
        {
            AddError(errors, nameof(options.Transport), "Transport is required. Must be 'stdio' or 'sse'.");
        }
        else if (options.Transport is not ("stdio" or "sse"))
        {
            AddError(errors, nameof(options.Transport),
                $"Unsupported transport: '{options.Transport}'. Must be 'stdio' or 'sse'.");
        }

        if (!string.IsNullOrWhiteSpace(options.Endpoint)
            && !options.Endpoint.StartsWith("/", StringComparison.Ordinal))
        {
            AddError(errors, nameof(options.Endpoint),
                "Endpoint must start with '/'.");
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            AddError(errors, nameof(options.Endpoint),
                "Endpoint is required for MCP HTTP/SSE transport.");
        }

        if (options.RateLimitPerMinute < 0)
        {
            AddError(errors, nameof(options.RateLimitPerMinute),
                "Rate limit must be >= 0.");
        }

        if (options.RequireAuthentication && options.AllowedApiKeys.Count == 0 && options.Transport == "sse")
        {
            AddError(errors, nameof(options.AllowedApiKeys),
                "RequireAuthentication is enabled with SSE transport but no AllowedApiKeys are configured.");
        }

    }

    protected override void CollectWarnings(McpServerOptions options, List<string> warnings)
    {
        if (options.RequireAuthentication && options.Transport == "stdio")
        {
            AddWarning(warnings, nameof(options.RequireAuthentication),
                "RequireAuthentication has limited effect with stdio transport. " +
                "Authentication is enforced at the process boundary. Use SSE transport for HTTP-level API key authentication.");
        }

        if (options.AllowApiKeyInQuery)
        {
            AddWarning(warnings, nameof(options.AllowApiKeyInQuery),
                "AllowApiKeyInQuery is enabled. Query-string API keys leak into access logs, " +
                "proxy caches, browser history, and referrer headers. This flag is intended " +
                "ONLY for transitional compatibility — migrate clients to X-Api-Key header.");
        }
    }
}
