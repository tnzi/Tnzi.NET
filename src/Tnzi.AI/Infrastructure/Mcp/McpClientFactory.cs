
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 客户端工厂实现 - 按 McpServerConfig 创建 Stdio/HTTP 连接，缓存以 Server Name 为 key。
/// 支持连接健康检查与断线自动重连（指数退避，最多 3 次重试）。
/// 实现 IAsyncDisposable，应用关闭时由 Host 调用以释放所有缓存的连接（Stdio 子进程、Http 连接等）。
/// </summary>
/// <remarks>
/// 服务器配置来源由 <see cref="IMcpServerCatalog"/> 物化（部署配置 + DB 注册表合并），
/// 本工厂只负责按给定 McpServerConfig 建连。信任边界：Stdio（本机子进程）配置只可能来自
/// 部署配置（AI:Mcp options）- DB 注册表在 catalog/registry 两层均拒绝 stdio。
/// </remarks>
public class McpClientFactory : IMcpClientFactory, IAsyncDisposable
{
    // MCP 标准 Header 常量（与 McpServerSecurityMiddleware 共享语义）
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string TenantHeaderName = "X-Tenant-Id";

    // 重连参数
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 500;
    private const int BackoffMultiplier = 2;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpClientFactory> _logger;
    private readonly McpOAuthClientHandler? _oauthHandler;
    private readonly ConcurrentDictionary<string, IMcpClientAdapter> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastHealthCheckTime = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private volatile bool _disposed;

    public McpClientFactory(
        ILoggerFactory loggerFactory,
        ILogger<McpClientFactory> logger,
        McpOAuthClientHandler? oauthHandler = null)
    {
        _loggerFactory = Check.NotNull(loggerFactory);
        _logger = Check.NotNull(logger);
        _oauthHandler = oauthHandler;
    }

    /// <inheritdoc />
    public async Task<IMcpClientAdapter> GetOrCreateClientAsync(McpServerConfig config, CancellationToken ct = default)
    {
        Check.NotNull(config);
        var key = config.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("MCP server config Name is required.");
        }

        // 快速路径：缓存命中 + 健康检查节流（30s 内不重复检查）
        if (_cache.TryGetValue(key, out var cached))
        {
            if (_lastHealthCheckTime.TryGetValue(key, out var lastCheck)
                && DateTime.UtcNow - lastCheck < HealthCheckInterval)
            {
                return cached;
            }

            if (await IsClientHealthyAsync(cached, key, ct).ConfigureAwait(false))
            {
                _lastHealthCheckTime[key] = DateTime.UtcNow;
                return cached;
            }

            // 连接已断开，清除健康检查时间戳，进入锁区域处理重连
            _lastHealthCheckTime.TryRemove(key, out _);
            _logger.LogWarning("MCP client for server '{ServerName}' is disconnected, attempting reconnection", key);
        }

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(McpClientFactory));
        }

        await _createLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(McpClientFactory));
            }

            // 双重检查：在锁内再次验证缓存（可能已被其他线程重连）
            if (_cache.TryGetValue(key, out cached))
            {
                if (await IsClientHealthyAsync(cached, key, ct).ConfigureAwait(false))
                {
                    _lastHealthCheckTime[key] = DateTime.UtcNow;
                    return cached;
                }

                // 移除并释放断开的连接
                await InvalidateCachedClientAsync(key).ConfigureAwait(false);
            }

            // 使用指数退避重试创建新连接
            var adapter = await CreateWithRetryAsync(config, ct).ConfigureAwait(false);
            if (!_cache.TryAdd(key, adapter))
            {
                await adapter.DisposeAsync().ConfigureAwait(false);
                return _cache[key];
            }

            return adapter;
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task InvalidateClientAsync(string serverName, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(serverName);

        await _createLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await InvalidateCachedClientAsync(serverName).ConfigureAwait(false);
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>
    /// 检查缓存中的客户端是否仍然健康（连接有效）。
    /// </summary>
    private async Task<bool> IsClientHealthyAsync(IMcpClientAdapter adapter, string serverName, CancellationToken ct)
    {
        try
        {
            return await adapter.IsConnectedAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed for MCP server '{ServerName}', treating as disconnected", serverName);
            return false;
        }
    }

    /// <summary>
    /// 从缓存中移除并释放客户端（不持有锁，调用方必须已持有 _createLock）。
    /// </summary>
    private async Task InvalidateCachedClientAsync(string serverName)
    {
        _lastHealthCheckTime.TryRemove(serverName, out _);
        if (_cache.TryRemove(serverName, out var adapter))
        {
            try
            {
                await adapter.DisposeAsync().ConfigureAwait(false);
                _logger.LogInformation("Invalidated and disposed MCP client for server '{ServerName}'", serverName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MCP client during invalidation for server '{ServerName}'", serverName);
            }
        }
    }

    /// <summary>
    /// 使用指数退避重试创建新连接（最多 MaxRetries 次）。
    /// </summary>
    private async Task<IMcpClientAdapter> CreateWithRetryAsync(McpServerConfig config, CancellationToken ct)
    {
        var delayMs = BaseDelayMs;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var adapter = await CreateAndConnectInternalAsync(config, ct).ConfigureAwait(false);
                if (attempt > 1)
                {
                    _logger.LogInformation(
                        "MCP client reconnected to server '{ServerName}' on attempt {Attempt}",
                        config.Name, attempt);
                }
                return adapter;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                if (config.OAuth != null && _oauthHandler != null)
                {
                    var invalidated = _oauthHandler.InvalidateToken(config.Name);
                    if (invalidated)
                    {
                        _logger.LogDebug("Invalidated OAuth token cache for MCP server '{ServerName}' after failed connection attempt", config.Name);
                    }
                }
                _logger.LogWarning(
                    ex, "MCP client connection attempt {Attempt}/{MaxRetries} failed for server '{ServerName}'",
                    attempt, MaxRetries, config.Name);

                if (attempt < MaxRetries)
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    delayMs *= BackoffMultiplier;
                }
            }
        }

        throw new InvalidOperationException(
            $"Failed to connect to MCP server '{config.Name}' after {MaxRetries} attempts.",
            lastException);
    }

    /// <summary>
    /// 创建并连接新的 MCP 客户端。子类可覆写以便测试注入。
    /// </summary>
    protected internal virtual async Task<IMcpClientAdapter> CreateAndConnectInternalAsync(McpServerConfig config, CancellationToken ct)
    {
        // OAuth: 获取令牌并注入 Authorization 头。
        // 只在本次建连的局部副本上加头：config 实例来自 IMcpServerCatalog 的 30s 快照缓存，
        // 回写 config.Headers 会把 bearer 令牌留在共享缓存对象里（跨请求可见）。
        var effectiveHeaders = config.Headers;
        if (config.OAuth != null && _oauthHandler != null)
        {
            var accessToken = await _oauthHandler.GetAccessTokenAsync(config.Name, config.OAuth, ct).ConfigureAwait(false);
            effectiveHeaders = new Dictionary<string, string>(config.Headers) { ["Authorization"] = $"Bearer {accessToken}" };
            _logger.LogDebug("OAuth token injected for MCP server '{ServerName}'", config.Name);
        }

        IClientTransport transport = config.ConnectionType switch
        {
            McpConnectionType.Stdio => CreateStdioTransport(config),
            McpConnectionType.Http => CreateHttpTransport(config, effectiveHeaders),
            _ => throw new NotSupportedException($"MCP connection type '{config.ConnectionType}' is not supported. Use Stdio or Http.")
        };

        try
        {
            var client = await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
            return new McpClientAdapter(client, config.Name, _logger, config.PrefixToolNameWithServer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MCP client for server '{ServerName}'", config.Name);
            throw new InvalidOperationException($"Failed to connect to MCP server '{config.Name}': {ex.Message}", ex);
        }
    }

    private StdioClientTransport CreateStdioTransport(McpServerConfig config)
    {
        string command;
        IReadOnlyList<string> arguments;
        if (!string.IsNullOrWhiteSpace(config.Command))
        {
            command = config.Command;
            arguments = config.Arguments ?? [];
        }
        else
        {
            if (config.Arguments == null || config.Arguments.Count == 0)
            {
                throw new InvalidOperationException($"MCP server '{config.Name}': Stdio requires either Command or at least one Argument.");
            }
            command = config.Arguments[0];
            arguments = config.Arguments.Count > 1 ? config.Arguments.Skip(1).ToList() : [];
        }

        var options = new StdioClientTransportOptions
        {
            Name = config.Name,
            Command = command,
            Arguments = arguments is IList<string> list ? list : arguments.ToList()
        };

        // 通过 SDK 的 EnvironmentVariables 属性传递环境变量给子进程，避免污染当前进程的全局环境
        if (config.Environment is { Count: > 0 })
        {
            options.EnvironmentVariables = new Dictionary<string, string?>();
            foreach (var (key, value) in config.Environment)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    options.EnvironmentVariables[key] = value;
                }
            }
            _logger.LogDebug(
                "Configured {Count} environment variables for MCP stdio child process '{ServerName}'",
                options.EnvironmentVariables.Count, config.Name);
        }

        return new StdioClientTransport(options, _loggerFactory);
    }

    private HttpClientTransport CreateHttpTransport(McpServerConfig config, Dictionary<string, string> headers)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint) || !Uri.TryCreate(config.Endpoint.TrimEnd('/'), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"MCP server '{config.Name}': Http requires a valid HTTP(S) Endpoint.");
        }
        var options = new HttpClientTransportOptions { Endpoint = uri };

        // 始终为 HTTP transport 配置禁止自动重定向的 HttpClient（防御 SSRF 重定向绕过），
        // 与 A2A 客户端基线一致。SDK 不支持注入 HttpClient 时降级到不带 handler 的默认行为。
        var configuredHttpClient = TryApplyHttpClientWithHeaders(options, headers);

        if (!configuredHttpClient && headers is { Count: > 0 })
        {
            // 无法注入 HttpClient（即无法应用自定义头/重定向策略）- 尝试 query 降级传递鉴权信息。
            if (TryApplyHttpQueryFallback(options, headers))
            {
                _logger.LogWarning(
                    "MCP server '{ServerName}' configured Headers but HttpClientTransportOptions does not support injecting an HttpClient in this SDK version. Falling back to query-based auth/tenant propagation (redirect protection unavailable).",
                    config.Name);
            }
            else
            {
                _logger.LogWarning(
                    "MCP server '{ServerName}' configured Headers but HttpClientTransportOptions does not support injecting an HttpClient in this SDK version.",
                    config.Name);
            }
        }

        return new HttpClientTransport(options, loggerFactory: _loggerFactory);
    }

    /// <summary>
    /// 尝试为 HTTP transport 注入一个禁止自动重定向（AllowAutoRedirect=false）的 HttpClient，
    /// 并附加自定义头（若有）。注入成功返回 true（重定向保护生效）。
    /// SDK 不支持 HttpClient 属性时返回 false，由调用方决定 query 降级。
    /// </summary>
    private static bool TryApplyHttpClientWithHeaders(HttpClientTransportOptions options, Dictionary<string, string>? headers)
    {
        var optionsType = options.GetType();
        var httpClientProp = optionsType.GetProperty("HttpClient", BindingFlags.Instance | BindingFlags.Public);
        if (httpClientProp != null && httpClientProp.PropertyType == typeof(HttpClient) && httpClientProp.CanWrite)
        {
            var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
            if (headers is { Count: > 0 })
            {
                foreach (var (key, value) in headers)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                    }
                }
            }
            httpClientProp.SetValue(options, client);
            return true;
        }

        // SDK 无 HttpClient 注入点：仅当有头时退而求其次写入 Headers/DefaultHeaders（无法控制重定向）。
        if (headers is { Count: > 0 })
        {
            var headersProp = optionsType.GetProperty("DefaultHeaders", BindingFlags.Instance | BindingFlags.Public)
                             ?? optionsType.GetProperty("Headers", BindingFlags.Instance | BindingFlags.Public);
            if (headersProp != null && headersProp.CanWrite)
            {
                headersProp.SetValue(options, headers);
            }
        }

        return false;
    }

    private static bool TryApplyHttpQueryFallback(HttpClientTransportOptions options, Dictionary<string, string> headers)
    {
        string? apiKey = null;
        if (headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader)
            && !string.IsNullOrWhiteSpace(apiKeyHeader))
        {
            apiKey = apiKeyHeader;
        }
        else if (headers.TryGetValue("Authorization", out var authorization)
            && !string.IsNullOrWhiteSpace(authorization))
        {
            const string bearerPrefix = "Bearer ";
            apiKey = authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? authorization[bearerPrefix.Length..].Trim()
                : authorization.Trim();
        }

        headers.TryGetValue(TenantHeaderName, out var tenantId);

        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(tenantId))
        {
            return false;
        }

        var endpointBuilder = new UriBuilder(options.Endpoint);
        var queryParts = new List<string>();
        var existingQuery = endpointBuilder.Query.TrimStart('?');
        if (!string.IsNullOrWhiteSpace(existingQuery))
        {
            queryParts.Add(existingQuery);
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            queryParts.Add($"apiKey={Uri.EscapeDataString(apiKey)}");
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            queryParts.Add($"tenantId={Uri.EscapeDataString(tenantId)}");
        }

        endpointBuilder.Query = string.Join("&", queryParts);
        options.Endpoint = endpointBuilder.Uri;
        return true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        await _createLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var keys = _cache.Keys.ToList();
            foreach (var key in keys)
            {
                if (_cache.TryRemove(key, out var adapter))
                {
                    try
                    {
                        await adapter.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing MCP client adapter for server '{ServerName}'", key);
                    }
                }
            }
        }
        finally
        {
            _createLock.Release();
            _createLock.Dispose();
        }
    }
}
