namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP Server 安全中间件 - API Key 验证、速率限制、审计日志
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

    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpServerSecurityMiddleware> _logger;

    // 滑动窗口速率限制: clientKey -> 线程安全计数器
    private readonly ConcurrentDictionary<string, SlidingWindowCounter> _rateLimits = new(StringComparer.OrdinalIgnoreCase);
    private long _cleanupCounter;
    private const int CleanupInterval = 100;

    public McpServerSecurityMiddleware(
        IOptionsMonitor<McpServerOptions> options,
        ILogger<McpServerSecurityMiddleware> logger,
        IServiceProvider serviceProvider)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _serviceProvider = Check.NotNull(serviceProvider);
    }

    /// <summary>
    /// 验证调用方：静态 API Key，或（若已注册）<b>运行范围</b>凭据。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 运行范围凭据是给<b>外部 CLI agent 回写</b>用的：由 <c>Tnzi.AI.Cli</c> 为每次运行签发，
    /// 随运行结束失效。校验走核心契约 <see cref="IRunScopedCredentialValidator"/>，
    /// 因此本模块<b>不引用</b> <c>Tnzi.AI.Cli</c> —— 未加载它时这条路径根本不存在，
    /// server 只认自己配置的静态 key，这正是应有的默认。
    /// </para>
    /// <para>
    /// 顺序上先试静态 key：那是纯内存的常数时间比较，而运行范围凭据要查一次库。
    /// </para>
    /// </remarks>
    public async Task<bool> ValidateCallerAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        if (ValidateApiKey(apiKey))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        using var scope = _serviceProvider.CreateScope();
        var validator = scope.ServiceProvider.GetService<IRunScopedCredentialValidator>();
        if (validator is null)
        {
            return false;
        }

        var credential = await validator.ValidateAsync(apiKey, cancellationToken);
        if (credential is null)
        {
            return false;
        }

        _logger.LogDebug(
            "MCP Server accepted a run-scoped credential for run {RunId} (agent {AgentId})",
            credential.RunId, credential.AgentId);

        return true;
    }

    /// <summary>
    /// 验证静态 API Key
    /// </summary>
    /// <returns>验证通过返回 true</returns>
    public bool ValidateApiKey(string? apiKey)
    {
        var config = _options.CurrentValue;
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

        // 固定时间比较：逐个候选 key 做等长字节比较，避免按字符提前返回的时序侧信道
        // 泄漏 API Key 前缀。语义与原 StringComparer.Ordinal 一致（区分大小写的精确匹配）。
        var candidateBytes = Encoding.UTF8.GetBytes(apiKey);
        var isValid = false;
        foreach (var allowed in config.AllowedApiKeys)
        {
            if (string.IsNullOrEmpty(allowed)) continue;
            if (CryptographicOperations.FixedTimeEquals(candidateBytes, Encoding.UTF8.GetBytes(allowed)))
            {
                isValid = true;
                break;
            }
        }

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
        if (_options.CurrentValue.AllowApiKeyInQuery)
        {
            if (request.Query.TryGetValue("apiKey", out var apiKeyQueryValues))
            {
                var apiKey = apiKeyQueryValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogWarning(
                        "MCP Server accepted API key from query string (AllowApiKeyInQuery=true). " +
                        "This is INSECURE: credentials leak to logs/proxies. Migrate client '{RemoteIp}' to the X-Api-Key header.",
                        request.GetClientIp());
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
                        request.GetClientIp());
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
    /// buckets (see <see cref="BuildClientKey"/>) and as an analytics dimension - it MUST
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

        var tenantSegment = _options.CurrentValue.RateLimitPerTenant
            ? ExtractTenantId(context.Request) ?? "public"
            : "shared";

        // 有 API key 就按 key 分区；没有才退到来源地址。
        // 走 GetClientIp 使其受 AspNetCoreOptions.CollectClientIpAddress 约束：
        // 声明不采集地址的部署，匿名调用方会一起落到 "anonymous" 这个全局桶上——
        // 总量仍有上限，但单个调用方能占满它。需要真正分区的部署应当要求 API key。
        var callerSegment = !string.IsNullOrWhiteSpace(apiKey)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)))[..16]
            : context.Request.GetClientIp() ?? "anonymous";

        return $"{tenantSegment}:{callerSegment}";
    }

    /// <summary>
    /// 检查速率限制（滑动窗口）
    /// </summary>
    /// <returns>未超限返回 true</returns>
    public bool CheckRateLimit(string clientKey)
    {
        var config = _options.CurrentValue;
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
        var config = _options.CurrentValue;
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
