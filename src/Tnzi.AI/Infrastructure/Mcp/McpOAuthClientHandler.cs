namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 客户端 OAuth 认证处理器 — 通过 client_credentials 流获取访问令牌，
/// 支持令牌缓存、自动刷新和过期重新获取。
/// </summary>
public class McpOAuthClientHandler
{
    private const string HttpClientName = "Tnzi.AI.OAuth";
    private const int ExpiryBufferSeconds = 60;
    private const string OpenIdConfigurationSuffix = "/.well-known/openid-configuration";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpOAuthClientHandler> _logger;
    private readonly ConcurrentDictionary<string, OAuthToken> _tokenCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tokenLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, OAuthMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _metadataLocks = new(StringComparer.OrdinalIgnoreCase);

    public McpOAuthClientHandler(
        IHttpClientFactory httpClientFactory,
        ILogger<McpOAuthClientHandler> logger)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 获取指定 MCP 服务器的 OAuth 访问令牌。优先使用缓存 → 刷新 → 重新获取。
    /// </summary>
    public virtual async Task<string> GetAccessTokenAsync(string serverName, McpOAuthConfig config, CancellationToken ct)
    {
        Check.NotNullOrWhiteSpace(serverName);
        Check.NotNull(config);
        ValidateConfig(config);

        // 快速路径：缓存命中且未过期（无锁）
        if (_tokenCache.TryGetValue(serverName, out var cached) && !cached.IsExpired)
        {
            return cached.AccessToken;
        }

        // 获取 per-server 锁，防止并发 token grant
        var tokenLock = _tokenLocks.GetOrAdd(serverName, _ => new SemaphoreSlim(1, 1));
        await tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 双重检查：在锁内再次验证缓存（可能已被其他线程刷新）
            if (_tokenCache.TryGetValue(serverName, out cached) && !cached.IsExpired)
            {
                return cached.AccessToken;
            }

            // 过期 + 有 refresh_token → 刷新
            if (cached is { IsExpired: true, RefreshToken: not null })
            {
                _logger.LogDebug("OAuth token for MCP server '{ServerName}' expired, attempting refresh", serverName);
                try
                {
                    var refreshed = await RequestTokenAsync(serverName, config, cached.RefreshToken, ct).ConfigureAwait(false);
                    _tokenCache[serverName] = refreshed;
                    return refreshed.AccessToken;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OAuth token refresh failed for MCP server '{ServerName}', falling back to client_credentials", serverName);
                    // 刷新失败，降级到 client_credentials
                }
            }

            // client_credentials 流
            _logger.LogDebug("Acquiring OAuth token for MCP server '{ServerName}' via client_credentials", serverName);
            var token = await RequestTokenAsync(serverName, config, refreshToken: null, ct).ConfigureAwait(false);
            _tokenCache[serverName] = token;
            return token.AccessToken;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    public virtual bool InvalidateToken(string serverName)
    {
        Check.NotNullOrWhiteSpace(serverName);
        return _tokenCache.TryRemove(serverName, out _);
    }

    public virtual async Task<bool> RevokeTokenAsync(string serverName, McpOAuthConfig config, CancellationToken ct)
    {
        Check.NotNullOrWhiteSpace(serverName);
        Check.NotNull(config);
        ValidateConfig(config);

        if (!_tokenCache.TryGetValue(serverName, out var cached) || string.IsNullOrWhiteSpace(cached.AccessToken))
        {
            return false;
        }

        var metadata = await ResolveMetadataAsync(serverName, config, ct).ConfigureAwait(false);
        var revocationEndpoint = !string.IsNullOrWhiteSpace(config.RevocationEndpoint)
            ? config.RevocationEndpoint
            : metadata.RevocationEndpoint;

        if (string.IsNullOrWhiteSpace(revocationEndpoint))
        {
            return false;
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("token", cached.AccessToken),
            new("client_id", config.ClientId)
        };
        if (!string.IsNullOrWhiteSpace(config.ClientSecret))
        {
            parameters.Add(new("client_secret", config.ClientSecret));
        }

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await httpClient.PostAsync(revocationEndpoint, content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        _tokenCache.TryRemove(serverName, out _);
        return true;
    }

    private async Task<OAuthToken> RequestTokenAsync(
        string serverName,
        McpOAuthConfig config,
        string? refreshToken,
        CancellationToken ct)
    {
        // IHttpClientFactory 管理 HttpClient 生命周期，不需要 using/dispose
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var metadata = await ResolveMetadataAsync(serverName, config, ct).ConfigureAwait(false);
        var tokenEndpoint = !string.IsNullOrWhiteSpace(config.TokenEndpoint)
            ? config.TokenEndpoint
            : metadata.TokenEndpoint;

        var parameters = new List<KeyValuePair<string, string>>();
        if (refreshToken != null)
        {
            parameters.Add(new("grant_type", "refresh_token"));
            parameters.Add(new("refresh_token", refreshToken));
            parameters.Add(new("client_id", config.ClientId));
            if (!string.IsNullOrEmpty(config.ClientSecret))
            {
                parameters.Add(new("client_secret", config.ClientSecret));
            }
        }
        else
        {
            parameters.Add(new("grant_type", "client_credentials"));
            parameters.Add(new("client_id", config.ClientId));
            if (!string.IsNullOrEmpty(config.ClientSecret))
            {
                parameters.Add(new("client_secret", config.ClientSecret));
            }
            if (config.Scopes is { Length: > 0 })
            {
                parameters.Add(new("scope", string.Join(" ", config.Scopes)));
            }
        }

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await httpClient.PostAsync(tokenEndpoint, content, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"OAuth token request failed for MCP server '{serverName}': HTTP {(int)response.StatusCode}. {errorBody}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (!root.TryGetProperty("access_token", out var accessTokenProp) ||
            accessTokenProp.GetString() is not { Length: > 0 } accessToken)
        {
            throw new InvalidOperationException(
                $"OAuth token response for MCP server '{serverName}' is missing 'access_token' field.");
        }

        var expiresIn = root.TryGetProperty("expires_in", out var expiresProp) ? expiresProp.GetInt32() : 3600;
        var newRefreshToken = root.TryGetProperty("refresh_token", out var refreshProp) ? refreshProp.GetString() : null;

        return new OAuthToken(
            accessToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - ExpiryBufferSeconds, 0)),
            newRefreshToken);
    }

    private static void ValidateConfig(McpOAuthConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ClientId))
        {
            throw new ArgumentException("OAuth ClientId is required.", nameof(config));
        }

        var hasTokenEndpoint = !string.IsNullOrWhiteSpace(config.TokenEndpoint);
        var hasDiscoverySource = !string.IsNullOrWhiteSpace(config.MetadataUrl) || !string.IsNullOrWhiteSpace(config.AuthorizationServer);
        if (!hasTokenEndpoint && !(config.EnableMetadataDiscovery && hasDiscoverySource))
        {
            throw new ArgumentException("OAuth TokenEndpoint is required unless metadata discovery is enabled with MetadataUrl or AuthorizationServer.", nameof(config));
        }
    }

    private async Task<OAuthMetadata> ResolveMetadataAsync(string serverName, McpOAuthConfig config, CancellationToken ct)
    {
        if (!config.EnableMetadataDiscovery)
        {
            return new OAuthMetadata(config.TokenEndpoint, config.RevocationEndpoint);
        }

        if (_metadataCache.TryGetValue(serverName, out var cached))
        {
            return cached;
        }

        var metadataLock = _metadataLocks.GetOrAdd(serverName, _ => new SemaphoreSlim(1, 1));
        await metadataLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_metadataCache.TryGetValue(serverName, out cached))
            {
                return cached;
            }

            var metadataUrl = BuildMetadataUrl(config);
            if (string.IsNullOrWhiteSpace(metadataUrl))
            {
                return new OAuthMetadata(config.TokenEndpoint, config.RevocationEndpoint);
            }

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.GetAsync(metadataUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"OAuth metadata discovery failed for MCP server '{serverName}': HTTP {(int)response.StatusCode}.");
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var tokenEndpoint = root.TryGetProperty("token_endpoint", out var tokenEndpointProp)
                ? tokenEndpointProp.GetString()
                : config.TokenEndpoint;
            if (string.IsNullOrWhiteSpace(tokenEndpoint))
            {
                throw new InvalidOperationException(
                    $"OAuth metadata for MCP server '{serverName}' is missing 'token_endpoint'.");
            }

            var revocationEndpoint = root.TryGetProperty("revocation_endpoint", out var revocationProp)
                ? revocationProp.GetString()
                : config.RevocationEndpoint;

            var metadata = new OAuthMetadata(tokenEndpoint, revocationEndpoint);
            _metadataCache[serverName] = metadata;
            return metadata;
        }
        finally
        {
            metadataLock.Release();
        }
    }

    private static string? BuildMetadataUrl(McpOAuthConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.MetadataUrl))
        {
            return config.MetadataUrl;
        }

        if (string.IsNullOrWhiteSpace(config.AuthorizationServer))
        {
            return null;
        }

        return config.AuthorizationServer.TrimEnd('/') + OpenIdConfigurationSuffix;
    }

    /// <summary>
    /// 缓存的 OAuth 令牌（内部使用）
    /// </summary>
    internal sealed record OAuthToken(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    internal sealed record OAuthMetadata(string TokenEndpoint, string? RevocationEndpoint);
}
