
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 客户端工厂实现 - 按 McpServerConfig 创建 Stdio/HTTP 连接，缓存以 Server Name 为 key。
/// 配置变更（Endpoint、Command、Arguments）生效需重启应用，不在运行时自动重连。
/// 实现 IAsyncDisposable，应用关闭时由 Host 调用以释放所有缓存的连接（Stdio 子进程、Http 连接等）。
/// </summary>
public class McpClientFactory : IMcpClientFactory, IAsyncDisposable
{
    // MCP 标准 Header 常量（与 McpServerSecurityMiddleware 共享语义）
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string TenantHeaderName = "X-Tenant-Id";

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpClientFactory> _logger;
    private readonly ConcurrentDictionary<string, IMcpClientAdapter> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private volatile bool _disposed;

    public McpClientFactory(
        ILoggerFactory loggerFactory,
        ILogger<McpClientFactory> logger)
    {
        _loggerFactory = Check.NotNull(loggerFactory);
        _logger = Check.NotNull(logger);
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

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
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

            if (_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var adapter = await CreateAndConnectAsync(config, ct).ConfigureAwait(false);
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

    private async Task<IMcpClientAdapter> CreateAndConnectAsync(McpServerConfig config, CancellationToken ct)
    {
        IClientTransport transport = config.ConnectionType switch
        {
            McpConnectionType.Stdio => CreateStdioTransport(config),
            McpConnectionType.Http => CreateHttpTransport(config),
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

    private HttpClientTransport CreateHttpTransport(McpServerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint) || !Uri.TryCreate(config.Endpoint.TrimEnd('/'), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"MCP server '{config.Name}': Http requires a valid HTTP(S) Endpoint.");
        }
        var options = new HttpClientTransportOptions { Endpoint = uri };
        if (config.Headers is { Count: > 0 })
        {
            if (!TryApplyHttpHeaders(options, config.Headers))
            {
                if (TryApplyHttpQueryFallback(options, config.Headers))
                {
                    _logger.LogWarning(
                        "MCP server '{ServerName}' configured Headers but HttpClientTransportOptions does not support custom headers in this SDK version. Falling back to query-based auth/tenant propagation.",
                        config.Name);
                }
                else
                {
                    _logger.LogWarning(
                        "MCP server '{ServerName}' configured Headers but HttpClientTransportOptions does not support custom headers in this SDK version.",
                        config.Name);
                }
            }
        }

        return new HttpClientTransport(options, loggerFactory: _loggerFactory);
    }

    private static bool TryApplyHttpHeaders(HttpClientTransportOptions options, Dictionary<string, string> headers)
    {
        var optionsType = options.GetType();
        var httpClientProp = optionsType.GetProperty("HttpClient", BindingFlags.Instance | BindingFlags.Public);
        if (httpClientProp != null && httpClientProp.PropertyType == typeof(HttpClient) && httpClientProp.CanWrite)
        {
            var client = new HttpClient();
            foreach (var (key, value) in headers)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                }
            }
            httpClientProp.SetValue(options, client);
            return true;
        }

        var headersProp = optionsType.GetProperty("DefaultHeaders", BindingFlags.Instance | BindingFlags.Public)
                         ?? optionsType.GetProperty("Headers", BindingFlags.Instance | BindingFlags.Public);
        if (headersProp != null && headersProp.CanWrite)
        {
            headersProp.SetValue(options, headers);
            return true;
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
