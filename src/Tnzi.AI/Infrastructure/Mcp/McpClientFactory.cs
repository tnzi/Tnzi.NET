
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 客户端工厂实现 - 按 McpServerConfig 创建 Stdio/HTTP 连接，缓存以 <see cref="McpCacheKey"/>
/// （租户键 + Server Name）为 key。
/// 支持连接健康检查与断线自动重连（指数退避，最多 3 次重试）。
/// 实现 IAsyncDisposable，应用关闭时由 Host 调用以释放所有缓存的连接（Stdio 子进程、Http 连接等）。
/// </summary>
/// <remarks>
/// <para>
/// 服务器配置来源由 <see cref="IMcpServerCatalog"/> 物化（部署配置 + DB 注册表合并），
/// 本工厂只负责按给定 McpServerConfig 建连。信任边界：Stdio（本机子进程）配置只可能来自
/// 部署配置（AI:Mcp options）- DB 注册表在 catalog/registry 两层均拒绝 stdio。
/// </para>
/// <para>
/// 缓存分桶必须带租户维度：两个租户可以注册同名 server（唯一索引是 (TenantId, Name)），
/// 只按名字缓存会让后来的租户命中前一个租户用其凭据建立的连接。见 <see cref="McpCacheKey"/>。
/// </para>
/// <para>
/// 一个连不上的服务器不能拖住所有运行（工具清单在每次 agent 解析时拉取）：
/// 建连按分区键各自加锁，不同服务器、不同租户互不排队；整轮重试失败后该分区键进入
/// <see cref="FailureCooldown"/>，窗口内的请求立即以 <see cref="McpServerUnavailableException"/> 失败，
/// 不再各自重走一轮；HTTP 服务器的初始化等待以 <see cref="HttpInitializationTimeout"/> 为上限。
/// <see cref="InvalidateClientAsync(string, CancellationToken)"/>（注册表改动 / 401 恢复）同时清掉冷却。
/// </para>
/// </remarks>
public class McpClientFactory : IMcpClientFactory, IAsyncDisposable
{
    // 重连参数
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 500;
    private const int BackoffMultiplier = 2;

    /// <summary>整轮重试失败后，同一分区键不再尝试建连的时长。</summary>
    public static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// HTTP 服务器的初始化超时（SDK 默认 60 秒）。正常的远端初始化在一两秒内完成；
    /// 黑洞地址会让每次尝试都等满它。Stdio 子进程沿用 SDK 默认：首次启动可能要先下载包。
    /// </summary>
    public static readonly TimeSpan HttpInitializationTimeout = TimeSpan.FromSeconds(15);

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpClientFactory> _logger;
    private readonly McpOAuthClientHandler? _oauthHandler;
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, IMcpClientAdapter> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastHealthCheckTime = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConnectFailure> _recentFailures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(30);
    private readonly KeyedAsyncLock _connectLocks = new();

    // 下一次为该分区键建连时要向消费方要「刷新过的」凭据（上一次的被服务器拒绝了）。只用于用户专属服务器。
    private readonly ConcurrentDictionary<string, byte> _refreshCredentialKeys = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    public McpClientFactory(
        ILoggerFactory loggerFactory,
        ILogger<McpClientFactory> logger,
        McpOAuthClientHandler? oauthHandler = null,
        IOptionsMonitor<AIOptions>? aiOptions = null,
        TimeProvider? timeProvider = null)
    {
        _loggerFactory = Check.NotNull(loggerFactory);
        _logger = Check.NotNull(logger);
        _oauthHandler = oauthHandler;
        _aiOptions = aiOptions;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<IMcpClientAdapter> GetOrCreateClientAsync(McpServerConfig config, CancellationToken ct = default)
    {
        Check.NotNull(config);
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new InvalidOperationException("MCP server config Name is required.");
        }

        // 分区键带租户维度：同名 server 属于不同租户时端点与凭据都不同，必须各自建连
        var key = McpCacheKey.For(config);

        // 快速路径：缓存命中 + 健康检查节流（30s 内不重复检查）
        if (_cache.TryGetValue(key, out var cached))
        {
            if (_lastHealthCheckTime.TryGetValue(key, out var lastCheck)
                && DateTime.UtcNow - lastCheck < HealthCheckInterval)
            {
                return cached;
            }

            if (await IsClientHealthyAsync(cached, config.Name, ct).ConfigureAwait(false))
            {
                _lastHealthCheckTime[key] = DateTime.UtcNow;
                return cached;
            }

            // 连接已断开，清除健康检查时间戳，进入锁区域处理重连
            _lastHealthCheckTime.TryRemove(key, out _);
            _logger.LogWarning("MCP client for server '{ServerName}' is disconnected, attempting reconnection", config.Name);
        }

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(McpClientFactory));
        }

        ThrowIfCoolingDown(key, config.Name);

        // 按分区键加锁：同一台服务器的并发请求只建一次连接，别的服务器 / 租户不受影响
        await using (await _connectLocks.LockAsync(key, ct).ConfigureAwait(false))
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(McpClientFactory));
            }

            // 双重检查：在锁内再次验证缓存（可能已被其他线程重连）
            if (_cache.TryGetValue(key, out cached))
            {
                if (await IsClientHealthyAsync(cached, config.Name, ct).ConfigureAwait(false))
                {
                    _lastHealthCheckTime[key] = DateTime.UtcNow;
                    return cached;
                }

                // 移除并释放断开的连接
                await InvalidateCachedClientAsync(key).ConfigureAwait(false);
            }

            // 排在前面的请求刚失败过：直接失败，不再各自重走一轮
            ThrowIfCoolingDown(key, config.Name);

            // 使用指数退避重试创建新连接
            IMcpClientAdapter adapter;
            try
            {
                adapter = await CreateWithRetryAsync(config, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _recentFailures[key] = new ConnectFailure(_timeProvider.GetUtcNow() + FailureCooldown, ex);
                throw;
            }

            _recentFailures.TryRemove(key, out _);
            if (_disposed)
            {
                await adapter.DisposeAsync().ConfigureAwait(false);
                throw new ObjectDisposedException(nameof(McpClientFactory));
            }

            _cache[key] = adapter;
            return adapter;
        }
    }

    /// <summary>该分区键在冷却窗口内时立即失败（异常带上一次失败作为内部异常）。</summary>
    private void ThrowIfCoolingDown(string key, string serverName)
    {
        if (!_recentFailures.TryGetValue(key, out var failure))
        {
            return;
        }

        if (_timeProvider.GetUtcNow() >= failure.RetryAfter)
        {
            _recentFailures.TryRemove(new KeyValuePair<string, ConnectFailure>(key, failure));
            return;
        }

        throw new McpServerUnavailableException(serverName, failure.RetryAfter, failure.Error);
    }

    /// <inheritdoc />
    public async Task InvalidateClientAsync(string serverName, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(serverName);

        // 调用方（注册表 CRUD / 401 恢复）只带得出名字，本工厂是 Singleton 没有租户上下文，
        // 因此失效全部租户桶里叫这个名字的连接。多失效一个只是重连一次；
        // 漏失效一个是继续拿着已被改掉或作废的凭据往外打，且毫无症状。
        // 冷却也一并清掉：注册表改了地址或凭据之后，下一次请求应当立刻按新配置重试。
        foreach (var key in _recentFailures.Keys.Where(k => McpCacheKey.MatchesServerName(k, serverName)).ToList())
        {
            _recentFailures.TryRemove(key, out _);
        }

        foreach (var key in _cache.Keys.Where(k => McpCacheKey.MatchesServerName(k, serverName)).ToList())
        {
            await using (await _connectLocks.LockAsync(key, ct).ConfigureAwait(false))
            {
                await InvalidateCachedClientAsync(key).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task InvalidateClientAsync(McpServerConfig config, bool refreshCredential, CancellationToken ct = default)
    {
        Check.NotNull(config);
        if (config is not UserMcpServerConfig user)
        {
            await InvalidateClientAsync(config.Name, ct).ConfigureAwait(false);
            return;
        }

        // 用户专属服务器只失效这一个用户的这一条连接：按名字会连带掉线别的用户
        var key = McpCacheKey.For(user);
        if (refreshCredential)
        {
            _refreshCredentialKeys[key] = 0;
        }

        await InvalidateKeyAsync(key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task InvalidateUserClientsAsync(Guid userId, string? serverName, CancellationToken ct = default)
    {
        var keys = _cache.Keys
            .Concat(_recentFailures.Keys)
            .Concat(_refreshCredentialKeys.Keys)
            .Where(k => McpCacheKey.BelongsToUser(k, userId, serverName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var key in keys)
        {
            await InvalidateKeyAsync(key, ct).ConfigureAwait(false);
            _refreshCredentialKeys.TryRemove(key, out _);
        }
    }

    private async Task InvalidateKeyAsync(string key, CancellationToken ct)
    {
        _recentFailures.TryRemove(key, out _);
        await using (await _connectLocks.LockAsync(key, ct).ConfigureAwait(false))
        {
            await InvalidateCachedClientAsync(key).ConfigureAwait(false);
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
    /// 从缓存中移除并释放客户端（不持有锁，调用方必须已持有该分区键的锁）。
    /// </summary>
    /// <param name="cacheKey">分区键（<see cref="McpCacheKey"/>），不是裸服务器名</param>
    private async Task InvalidateCachedClientAsync(string cacheKey)
    {
        _lastHealthCheckTime.TryRemove(cacheKey, out _);
        if (_cache.TryRemove(cacheKey, out var adapter))
        {
            var serverName = McpCacheKey.ServerName(cacheKey);
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
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // 调用方取消才算取消；SDK 自己的初始化超时也可能以取消的形式出现，那是一次失败的尝试
                lastException = ex;
                if (config is UserMcpServerConfig && McpAuthRecoveryToolWrapper.IsAuthFailure(ex))
                {
                    // 凭据被拒：下一次尝试向消费方要刷新过的，而不是再用同一个失败的值撞两次
                    _refreshCredentialKeys[McpCacheKey.For(config)] = 0;
                }

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
        // 凭据只写进本次建连的局部请求头副本：config 实例来自 IMcpServerCatalog 的 30s 快照缓存（或消费方的清单缓存），
        // 回写 config.Headers 会把 bearer 令牌留在共享对象里（跨请求、跨用户可见）。
        var effectiveHeaders = config.Headers;
        if (config.OAuth != null && _oauthHandler != null)
        {
            var accessToken = await _oauthHandler.GetAccessTokenAsync(config.Name, config.OAuth, ct).ConfigureAwait(false);
            effectiveHeaders = new Dictionary<string, string>(config.Headers) { ["Authorization"] = $"Bearer {accessToken}" };
            _logger.LogDebug("OAuth token injected for MCP server '{ServerName}'", config.Name);
        }
        else if (config is UserMcpServerConfig userConfig)
        {
            effectiveHeaders = await BuildUserHeadersAsync(userConfig, ct).ConfigureAwait(false);
        }

        return await ConnectTransportAsync(config, effectiveHeaders, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 向消费方要这个用户此刻的凭据并合成请求头。上一次尝试被服务器拒绝过时带 <c>Refresh = true</c>。
    /// 凭据只存在于返回的局部字典里。
    /// </summary>
    private async Task<Dictionary<string, string>> BuildUserHeadersAsync(UserMcpServerConfig config, CancellationToken ct)
    {
        var refresh = _refreshCredentialKeys.TryRemove(McpCacheKey.For(config), out _);
        McpCredential? credential;
        try
        {
            credential = await config.CredentialProvider(new McpCredentialRequest(refresh), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // 回调的异常消息可能带着令牌或账号信息：只记类型，不记消息
            _logger.LogWarning("The credential provider for user-scoped MCP server '{ServerName}' failed ({ExceptionType})", config.Name, ex.GetType().Name);
            throw new InvalidOperationException($"The credential provider for MCP server '{config.Name}' failed.");
        }

        if (credential is null)
        {
            throw new InvalidOperationException($"No credential is available for MCP server '{config.Name}'.");
        }

        if (string.IsNullOrWhiteSpace(credential.AccessToken) || credential.AccessToken.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(credential.Scheme) || credential.Scheme.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            throw new InvalidOperationException($"The credential for MCP server '{config.Name}' is not a valid header value.");
        }

        var headers = new Dictionary<string, string>(config.Headers, StringComparer.OrdinalIgnoreCase);
        if (credential.AdditionalHeaders is not null)
        {
            foreach (var (name, value) in credential.AdditionalHeaders)
            {
                if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"The credential for MCP server '{config.Name}' must not carry an Authorization header of its own.");
                }

                headers[name] = value;
            }
        }

        headers["Authorization"] = $"{credential.Scheme} {credential.AccessToken}";
        _logger.LogDebug("Credential obtained for user-scoped MCP server '{ServerName}' (refresh: {Refresh})", config.Name, refresh);
        return headers;
    }

    /// <summary>按已定好的请求头建传输并连接。拆出来是为了让测试观察实际发出的请求头而不必起真实服务器。</summary>
    protected internal virtual async Task<IMcpClientAdapter> ConnectTransportAsync(
        McpServerConfig config, Dictionary<string, string> effectiveHeaders, CancellationToken ct)
    {
        IClientTransport transport = config.ConnectionType switch
        {
            McpConnectionType.Stdio => CreateStdioTransport(config),
            McpConnectionType.Http => CreateHttpTransport(config, effectiveHeaders),
            _ => throw new NotSupportedException($"MCP connection type '{config.ConnectionType}' is not supported. Use Stdio or Http.")
        };

        try
        {
            var client = await McpClient.CreateAsync(transport, CreateClientOptions(config), cancellationToken: ct).ConfigureAwait(false);
            return new McpClientAdapter(client, config.Name, _logger, config.PrefixToolNameWithServer);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to create MCP client for server '{ServerName}'", config.Name);
            throw new InvalidOperationException($"Failed to connect to MCP server '{config.Name}': {ex.Message}", ex);
        }
    }

    /// <summary>建连选项：HTTP 服务器的初始化等待以 <see cref="HttpInitializationTimeout"/> 为上限，Stdio 沿用 SDK 默认。</summary>
    protected internal virtual McpClientOptions? CreateClientOptions(McpServerConfig config)
        => config.ConnectionType == McpConnectionType.Http
            ? new McpClientOptions { InitializationTimeout = HttpInitializationTimeout }
            : null;

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

        var options = new HttpClientTransportOptions { Endpoint = uri, Name = config.Name, ConnectionTimeout = HttpInitializationTimeout };
        if (headers is { Count: > 0 })
        {
            // 凭据与租户头只走请求头：查询串里的凭据会进访问日志、代理日志与错误信息
            options.AdditionalHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        }

        return new HttpClientTransport(options, CreateHttpClient(config), _loggerFactory, ownsHttpClient: true);
    }

    private HttpClient CreateHttpClient(McpServerConfig config)
        => new(CreateHttpHandler(config, _aiOptions?.CurrentValue.Mcp.AllowPrivateEndpoints == true));

    /// <summary>
    /// HTTP transport 专用 HttpClient 的主处理器：从不自动跟随重定向；端点来自注册表（<see cref="McpServerConfig.EnforceEgressGuard"/>）
    /// 且未开 <c>AI:Mcp:AllowPrivateEndpoints</c> 时，用 <see cref="EgressGuardedConnector.CreateHandler"/>：每一条 TCP 连接都先过
    /// egress 检查，且不走代理（经代理时连接的是代理，目标主机由代理解析，连接期检查看不到它）。
    /// </summary>
    /// <remarks>
    /// 307 / 308 保留方法与请求体：跟随重定向就是把初始化 POST（连同凭据头）原样投到 Location 指的地方，
    /// 注册时检查过的地址因此不再是实际连接的地址。部署配置声明的端点（与开了私网放行的部署）是宿主信任的，
    /// 照常跟随系统代理设置。
    /// </remarks>
    internal static SocketsHttpHandler CreateHttpHandler(McpServerConfig config, bool allowPrivateEndpoints)
        => config.EnforceEgressGuard && !allowPrivateEndpoints
            ? EgressGuardedConnector.CreateHandler()
            : new SocketsHttpHandler { AllowAutoRedirect = false };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        // 置位之后才拿到锁的建连会看到 _disposed 并自行释放刚建好的连接；这里逐键加锁，等在途的建连收尾
        foreach (var key in _cache.Keys.ToList())
        {
            await using (await _connectLocks.LockAsync(key, CancellationToken.None).ConfigureAwait(false))
            {
                if (!_cache.TryRemove(key, out var adapter))
                {
                    continue;
                }

                try
                {
                    await adapter.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing MCP client adapter for server '{ServerName}'", McpCacheKey.ServerName(key));
                }
            }
        }

        _recentFailures.Clear();
    }

    private sealed record ConnectFailure(DateTimeOffset RetryAfter, Exception Error);
}
