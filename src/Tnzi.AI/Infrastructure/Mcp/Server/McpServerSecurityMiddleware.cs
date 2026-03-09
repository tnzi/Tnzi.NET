namespace Tnzi.AI.Infrastructure.Mcp.Server;

/// <summary>
/// MCP Server 安全中间件 — API Key 验证、速率限制、审计日志
/// </summary>
public class McpServerSecurityMiddleware
{
    public const string ApiKeyHeaderName = "X-Api-Key";
    public const string TenantHeaderName = "X-Tenant-Id";

    private readonly IOptions<McpServerOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpServerSecurityMiddleware> _logger;

    // 滑动窗口速率限制: clientKey -> 线程安全计数器
    private readonly ConcurrentDictionary<string, SlidingWindowCounter> _rateLimits = new(StringComparer.OrdinalIgnoreCase);
    private long _cleanupCounter;
    private const int CleanupInterval = 100;

    public McpServerSecurityMiddleware(
        IOptions<McpServerOptions> options,
        ILogger<McpServerSecurityMiddleware> logger,
        IServiceProvider serviceProvider)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _serviceProvider = Check.NotNull(serviceProvider);
    }

    /// <summary>
    /// 验证 API Key
    /// </summary>
    /// <returns>验证通过返回 true</returns>
    public bool ValidateApiKey(string? apiKey)
    {
        var config = _options.Value;
        if (!config.RequireAuthentication)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("MCP Server request rejected: missing API key");
            return false;
        }

        if (config.AllowedApiKeys.Count == 0)
        {
            // 未配置任何 key 时，认证总是失败
            _logger.LogWarning("MCP Server request rejected: no allowed API keys configured");
            return false;
        }

        var isValid = config.AllowedApiKeys.Contains(apiKey, StringComparer.Ordinal);
        if (!isValid)
        {
            _logger.LogWarning("MCP Server request rejected: invalid API key");
        }

        return isValid;
    }

    /// <summary>
    /// 从 HTTP 请求中提取 API Key。
    /// </summary>
    public string? ExtractApiKey(HttpRequest request)
    {
        Check.NotNull(request);

        if (request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyValues))
        {
            var apiKey = apiKeyValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                return apiKey;
            }
        }

        if (request.Headers.TryGetValue("Authorization", out var authorizationValues))
        {
            var authorization = authorizationValues.FirstOrDefault();
            const string bearerPrefix = "Bearer ";
            if (!string.IsNullOrWhiteSpace(authorization)
                && authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorization[bearerPrefix.Length..].Trim();
            }
        }

        if (request.Query.TryGetValue("apiKey", out var apiKeyQueryValues))
        {
            var apiKey = apiKeyQueryValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                return apiKey;
            }
        }

        if (request.Query.TryGetValue("apikey", out var legacyApiKeyQueryValues))
        {
            var apiKey = legacyApiKeyQueryValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                return apiKey;
            }
        }

        return null;
    }

    /// <summary>
    /// 从 HTTP 请求中提取租户标识。
    /// </summary>
    public string? ExtractTenantId(HttpRequest request)
    {
        Check.NotNull(request);

        if (request.Headers.TryGetValue(TenantHeaderName, out var tenantValues))
        {
            var tenantId = tenantValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                return tenantId;
            }
        }

        if (request.Query.TryGetValue("tenantId", out var tenantQueryValues))
        {
            var tenantId = tenantQueryValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                return tenantId;
            }
        }

        if (request.Query.TryGetValue("tenant", out var legacyTenantQueryValues))
        {
            var tenantId = legacyTenantQueryValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                return tenantId;
            }
        }

        return null;
    }

    /// <summary>
    /// 构建 HTTP 请求的限流键。
    /// </summary>
    public string BuildClientKey(HttpContext context, string? apiKey)
    {
        Check.NotNull(context);

        var tenantSegment = _options.Value.TenantIsolation
            ? ExtractTenantId(context.Request) ?? "public"
            : "shared";

        var callerSegment = !string.IsNullOrWhiteSpace(apiKey)
            ? apiKey
            : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return $"{tenantSegment}:{callerSegment}";
    }

    /// <summary>
    /// 检查速率限制（滑动窗口）
    /// </summary>
    /// <returns>未超限返回 true</returns>
    public bool CheckRateLimit(string clientKey)
    {
        var limit = _options.Value.RateLimitPerMinute;
        if (limit <= 0)
        {
            return true;
        }

        var counter = _rateLimits.GetOrAdd(clientKey, _ => new SlidingWindowCounter());
        if (!counter.TryIncrement(limit))
        {
            _logger.LogWarning("MCP Server rate limit exceeded for client '{ClientKey}': {Limit} requests/minute",
                clientKey, limit);
            return false;
        }

        if (Interlocked.Increment(ref _cleanupCounter) % CleanupInterval == 0)
        {
            EvictStaleEntries();
        }

        return true;
    }

    private void EvictStaleEntries()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-2);
        foreach (var (key, counter) in _rateLimits)
        {
            if (counter.IsStale(cutoff))
            {
                _rateLimits.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// 记录审计日志
    /// </summary>
    public async Task AuditLogAsync(
        string toolName,
        Guid? agentId,
        long durationMs,
        bool isSuccess,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        if (!_options.Value.EnableAuditLog)
        {
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var usageLogService = scope.ServiceProvider.GetService<IUsageLogService>();
            if (usageLogService == null) return;

            await usageLogService.LogUsageAsync(
                operationType: "McpToolCall",
                provider: "mcp-server",
                model: toolName,
                inputTokens: 0,
                outputTokens: 0,
                durationMs: durationMs,
                isSuccess: isSuccess,
                errorMessage: errorMessage,
                agentId: agentId,
                ct: ct);
        }
        catch (Exception ex)
        {
            // 审计日志失败不应影响主流程
            _logger.LogWarning(ex, "Failed to write MCP Server audit log for tool '{ToolName}'", toolName);
        }
    }

    /// <summary>
    /// 线程安全的滑动窗口计数器，通过内部锁消除并发竞态条件
    /// </summary>
    private sealed class SlidingWindowCounter
    {
        private readonly object _lock = new();
        private DateTimeOffset _windowStart;
        private int _count;

        public bool TryIncrement(int limit)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                if (now - _windowStart > TimeSpan.FromMinutes(1))
                {
                    _windowStart = now;
                    _count = 1;
                    return true;
                }

                _count++;
                return _count <= limit;
            }
        }

        public bool IsStale(DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                return _windowStart < cutoff;
            }
        }
    }
}
