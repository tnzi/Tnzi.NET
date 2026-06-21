namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP Server 安全中间件 — API Key 验证、速率限制、审计日志
/// </summary>
public class McpServerSecurityMiddleware
{
    public const string ApiKeyHeaderName = "X-Api-Key";
    public const string TenantHeaderName = "X-Tenant-Id";

    /// <summary>
    /// HttpContext.Items key under which the hashed caller key (16-char hex) is stored by
    /// <see cref="McpServerHttpSecurityMiddleware"/>. Downstream code reads this key to
    /// associate usage records with the caller without touching the raw API key.
    /// </summary>
    public const string CallerHashItemKey = "mcp-caller-hash";

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

        // Query-string API key extraction is OFF by default because query strings
        // leak into access logs, proxy caches, browser history, and referrer headers.
        // See McpServerOptions.AllowApiKeyInQuery for opt-in semantics.
        if (_options.Value.AllowApiKeyInQuery)
        {
            if (request.Query.TryGetValue("apiKey", out var apiKeyQueryValues))
            {
                var apiKey = apiKeyQueryValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogWarning(
                        "MCP Server accepted API key from query string (AllowApiKeyInQuery=true). " +
                        "This is INSECURE — credentials leak to logs/proxies. Migrate client '{RemoteIp}' to X-Api-Key header.",
                        request.HttpContext.Connection.RemoteIpAddress);
                    return apiKey;
                }
            }

            if (request.Query.TryGetValue("apikey", out var legacyApiKeyQueryValues))
            {
                var apiKey = legacyApiKeyQueryValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogWarning(
                        "MCP Server accepted API key from legacy 'apikey' query parameter (AllowApiKeyInQuery=true). " +
                        "This is INSECURE. Migrate client '{RemoteIp}' to X-Api-Key header.",
                        request.HttpContext.Connection.RemoteIpAddress);
                    return apiKey;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 从 HTTP 请求中提取租户标识（仅读取 <c>X-Tenant-Id</c> 请求头，不读取 query string）。
    /// <para>
    /// <b>UNTRUSTED partition hint.</b> The value is self-reported by the client and is
    /// NOT validated against any tenant store. It is used ONLY to partition rate-limit
    /// buckets (see <see cref="BuildClientKey"/>) and as an analytics dimension — it MUST
    /// NEVER be used for data isolation or authorization decisions.
    /// </para>
    /// <para>
    /// Query-string extraction (<c>?tenantId=</c>/<c>?tenant=</c>) was deliberately removed:
    /// query values leak into access logs/proxies and made it trivial to spoof another
    /// tenant's rate-limit partition via a crafted URL.
    /// </para>
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

        return null;
    }

    /// <summary>
    /// 构建 HTTP 请求的限流键。
    /// </summary>
    public string BuildClientKey(HttpContext context, string? apiKey)
    {
        Check.NotNull(context);

        var tenantSegment = _options.Value.RateLimitPerTenant
            ? ExtractTenantId(context.Request) ?? "public"
            : "shared";

        var callerSegment = !string.IsNullOrWhiteSpace(apiKey)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)))[..16]
            : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return $"{tenantSegment}:{callerSegment}";
    }

    /// <summary>
    /// 检查速率限制（滑动窗口）
    /// </summary>
    /// <returns>未超限返回 true</returns>
    public bool CheckRateLimit(string clientKey)
    {
        var config = _options.Value;
        var limit = config.RateLimitPerMinute;
        if (limit <= 0)
        {
            return true;
        }

        // Defensive cap: under a key-space flood attack the dict could grow before
        // the normal eviction interval fires. Force eviction when we're at or
        // above the configured cap, BEFORE adding a new entry.
        if (_rateLimits.Count >= config.RateLimitTrackingMaxEntries && !_rateLimits.ContainsKey(clientKey))
        {
            EvictStaleEntries();
            // After eviction, if still full (all entries are fresh), reject as a
            // defensive measure rather than let memory grow unboundedly. A
            // legitimate workload should never sustain > RateLimitTrackingMaxEntries
            // distinct clients within a 2-minute window.
            if (_rateLimits.Count >= config.RateLimitTrackingMaxEntries)
            {
                _logger.LogWarning(
                    "MCP Server rate limit tracking table is full ({Count}/{Max}); rejecting request for new client '{ClientKey}'",
                    _rateLimits.Count, config.RateLimitTrackingMaxEntries, clientKey);
                return false;
            }
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
    /// <param name="toolName">MCP 工具名称</param>
    /// <param name="agentId">调用的 Agent ID（自定义工具为 null）</param>
    /// <param name="durationMs">调用耗时（毫秒）</param>
    /// <param name="isSuccess">是否成功</param>
    /// <param name="errorMessage">错误信息（成功时为 null）</param>
    /// <param name="callerApiKeyId">调用方 API Key 的哈希摘要（16 位十六进制，非原始 Key）。
    /// 由 <see cref="McpServerHttpSecurityMiddleware"/> 通过 <see cref="BuildClientKey"/> 计算后
    /// 存入 <c>HttpContext.Items[<see cref="CallerHashItemKey"/>]</c>。</param>
    /// <param name="ct">取消令牌</param>
    public async Task AuditLogAsync(
        string toolName,
        Guid? agentId,
        long durationMs,
        bool isSuccess,
        string? errorMessage = null,
        string? callerApiKeyId = null,
        CancellationToken ct = default)
    {
        var config = _options.Value;
        var auditEnabled = config.EnableAuditLog;
        var analyticsEnabled = config.EnableToolAnalytics;

        // Audit log and operational analytics are gated INDEPENDENTLY: disabling the
        // audit log (IUsageLogService) must NOT silently disable per-tool analytics
        // (IMcpToolAnalyticsService) and vice versa. Skip the scope only when both are off.
        if (!auditEnabled && !analyticsEnabled)
        {
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();

            if (auditEnabled)
            {
                var usageLogService = scope.ServiceProvider.GetService<IUsageLogService>();
                if (usageLogService != null)
                {
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
            }

            // Per-tool operational analytics (stats/popularity/errors), independent of EnableAuditLog
            if (analyticsEnabled)
            {
                var analyticsService = scope.ServiceProvider.GetService<IMcpToolAnalyticsService>();
                if (analyticsService != null)
                {
                    await analyticsService.RecordUsageAsync(toolName, durationMs, isSuccess, errorMessage, callerApiKeyId);
                }
            }
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
