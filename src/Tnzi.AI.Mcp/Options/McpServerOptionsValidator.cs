namespace Tnzi.AI.Mcp.Options;

/// <summary>
/// MCP Server 配置选项验证器
/// </summary>
public class McpServerOptionsValidator : OptionsValidatorBase<McpServerOptions>
{
    protected override void ValidateOptions(McpServerOptions options, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(options.Endpoint)
            && !options.Endpoint.StartsWith("/", StringComparison.Ordinal))
        {
            AddError(errors, nameof(options.Endpoint),
                "Endpoint must start with '/'.");
        }

        // Required-checks are gated on Enabled — a disabled server's incomplete
        // configuration must not block application startup. Format checks above
        // still apply whenever a value is provided.
        if (options.Enabled && string.IsNullOrWhiteSpace(options.Endpoint))
        {
            AddError(errors, nameof(options.Endpoint),
                "Endpoint is required.");
        }

        if (options.RateLimitPerMinute < 0)
        {
            AddError(errors, nameof(options.RateLimitPerMinute),
                "Rate limit must be >= 0.");
        }

        // Only enforce API keys when the server is actually enabled — a disabled
        // server must not block application startup over missing credentials.
        if (options.Enabled && options.RequireAuthentication && options.AllowedApiKeys.Count == 0)
        {
            AddError(errors, nameof(options.AllowedApiKeys),
                "RequireAuthentication is enabled but no AllowedApiKeys are configured.");
        }

        // The rate-limit tracking table needs at least one slot, otherwise every
        // request hits the "table full" rejection path and the limiter degenerates
        // into a deny-all. Only enforced when rate limiting is actually in effect
        // (server enabled + a positive per-minute limit).
        if (options.Enabled && options.RateLimitPerMinute > 0 && options.RateLimitTrackingMaxEntries < 1)
        {
            AddError(errors, nameof(options.RateLimitTrackingMaxEntries),
                "RateLimitTrackingMaxEntries must be >= 1 when rate limiting is enabled.");
        }
    }

    protected override void CollectWarnings(McpServerOptions options, List<string> warnings)
    {
        if (options.Enabled && !options.RequireAuthentication)
        {
            AddWarning(warnings, nameof(options.RequireAuthentication),
                "MCP server is enabled without authentication; all requests will be accepted " +
                "without an API key. Ensure the endpoint is not reachable from untrusted networks, " +
                "or set RequireAuthentication=true with AllowedApiKeys.");
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
