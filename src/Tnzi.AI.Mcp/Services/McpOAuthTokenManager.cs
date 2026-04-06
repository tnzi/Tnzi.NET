using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tnzi.AI.Mcp.Options;
using Tnzi.AI.Mcp.Services.Interfaces;
using Tnzi.Utilities;

namespace Tnzi.AI.Mcp.Services;

/// <summary>
/// OAuth Token 管理器 — per-server SemaphoreSlim + 双重检查锁定 + proactive refresh + metadata discovery。
/// </summary>
public class McpOAuthTokenManager : IMcpOAuthTokenManager
{
    private const string HttpClientName = "Tnzi.AI.MCP.OAuth";
    private const string WellKnownSuffix = "/.well-known/openid-configuration";

    private readonly ILogger<McpOAuthTokenManager> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly McpOAuthOptions _options;

    // Per-server 缓存和锁
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DiscoveredMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly KeyedAsyncLock _serverLock = new();
    private readonly KeyedAsyncLock _metadataLock = new();

    private record CachedToken(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);
    private record DiscoveredMetadata(string? TokenEndpoint, string? RevocationEndpoint);

    public McpOAuthTokenManager(
        ILogger<McpOAuthTokenManager> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<McpOAuthOptions> options)
    {
        _logger = Check.NotNull(logger);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _options = Check.NotNull(options).Value;
    }

    public async Task<string?> GetAuthorizationHeaderAsync(string serverName, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(serverName);

        if (!_options.Servers.TryGetValue(serverName, out var config))
            return null;

        // 快速路径：缓存 token 未过期
        if (_tokenCache.TryGetValue(serverName, out var cached) && !IsExpiredOrNearExpiry(cached, config.RefreshSkewSeconds))
        {
            return $"{cached.TokenType} {cached.AccessToken}";
        }

        // 慢速路径：per-key 锁（KeyedAsyncLock 自动引用计数释放）
        await using (await _serverLock.LockAsync(serverName, ct))
        {
            // 双重检查
            if (_tokenCache.TryGetValue(serverName, out cached) && !IsExpiredOrNearExpiry(cached, config.RefreshSkewSeconds))
            {
                return $"{cached.TokenType} {cached.AccessToken}";
            }

            var token = await FetchTokenAsync(serverName, config, ct);
            if (token == null) return null;

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            var cachedToken = new CachedToken(token.AccessToken, token.TokenType ?? "Bearer", expiresAt);
            _tokenCache[serverName] = cachedToken;

            _logger.LogDebug("Fetched new OAuth token for MCP server {ServerName}, expires at {ExpiresAt}", serverName, expiresAt);
            return $"{cachedToken.TokenType} {cachedToken.AccessToken}";
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeAsync(string serverName, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(serverName);

        // 无缓存 token，直接返回成功
        if (!_tokenCache.TryRemove(serverName, out var cached))
        {
            return true;
        }

        if (!_options.Servers.TryGetValue(serverName, out var config))
        {
            return true;
        }

        // 解析 revocation endpoint：显式配置 > metadata discovery
        var revocationEndpoint = config.RevocationEndpoint;
        if (string.IsNullOrWhiteSpace(revocationEndpoint))
        {
            var metadata = await ResolveMetadataAsync(serverName, config, ct);
            revocationEndpoint = metadata?.RevocationEndpoint;
        }

        // 无 revocation endpoint，本地缓存已清除，返回成功
        if (string.IsNullOrWhiteSpace(revocationEndpoint))
        {
            _logger.LogDebug("No revocation endpoint configured for MCP server {ServerName}, local token cleared", serverName);
            return true;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var parameters = new List<KeyValuePair<string, string>>
            {
                new("token", cached.AccessToken),
                new("client_id", config.ClientId),
                new("client_secret", config.ClientSecret)
            };

            using var content = new FormUrlEncodedContent(parameters);
            using var response = await client.PostAsync(revocationEndpoint, content, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully revoked OAuth token for MCP server {ServerName}", serverName);
                return true;
            }

            _logger.LogWarning("OAuth token revocation returned HTTP {StatusCode} for MCP server {ServerName}",
                (int)response.StatusCode, serverName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke OAuth token for MCP server {ServerName}", serverName);
            return false;
        }
    }

    private static bool IsExpiredOrNearExpiry(CachedToken token, int skewSeconds)
    {
        return DateTimeOffset.UtcNow >= token.ExpiresAt.AddSeconds(-skewSeconds);
    }

    private async Task<OAuthTokenResponse?> FetchTokenAsync(string serverName, McpOAuthServerConfig config, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            // 解析 token endpoint：显式配置 > metadata discovery
            var tokenEndpoint = config.TokenEndpoint;
            if (string.IsNullOrWhiteSpace(tokenEndpoint))
            {
                var metadata = await ResolveMetadataAsync(serverName, config, ct);
                tokenEndpoint = metadata?.TokenEndpoint;
            }

            if (string.IsNullOrWhiteSpace(tokenEndpoint))
            {
                _logger.LogError("No token endpoint available for MCP server {ServerName}", serverName);
                return null;
            }

            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = config.GrantType,
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret
            };

            if (!string.IsNullOrEmpty(config.Scope))
                parameters["scope"] = config.Scope;

            if (string.Equals(config.GrantType, "refresh_token", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(config.RefreshToken))
            {
                parameters["refresh_token"] = config.RefreshToken;
            }

            var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(parameters), ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<OAuthTokenResponse>(json, OAuthJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch OAuth token for MCP server {ServerName}", serverName);
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // OAuth Metadata Discovery (RFC 8414)
    // -------------------------------------------------------------------------

    private async Task<DiscoveredMetadata?> ResolveMetadataAsync(
        string serverName, McpOAuthServerConfig config, CancellationToken ct)
    {
        if (!config.EnableMetadataDiscovery)
            return null;

        // 快速路径：缓存命中
        if (_metadataCache.TryGetValue(serverName, out var cached))
            return cached;

        await using (await _metadataLock.LockAsync(serverName, ct))
        {
            // 双重检查
            if (_metadataCache.TryGetValue(serverName, out cached))
                return cached;

            var metadataUrl = BuildMetadataUrl(config);
            if (string.IsNullOrWhiteSpace(metadataUrl))
                return null;

            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.GetAsync(metadataUrl, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OAuth metadata discovery returned HTTP {StatusCode} for MCP server {ServerName}",
                        (int)response.StatusCode, serverName);
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var tokenEndpoint = root.TryGetProperty("token_endpoint", out var tep) ? tep.GetString() : null;
                var revocationEndpoint = root.TryGetProperty("revocation_endpoint", out var rep) ? rep.GetString() : null;

                var metadata = new DiscoveredMetadata(tokenEndpoint, revocationEndpoint);
                _metadataCache[serverName] = metadata;

                _logger.LogDebug("Discovered OAuth metadata for MCP server {ServerName}: token_endpoint={TokenEndpoint}, revocation_endpoint={RevocationEndpoint}",
                    serverName, tokenEndpoint, revocationEndpoint);

                return metadata;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OAuth metadata discovery failed for MCP server {ServerName}", serverName);
                return null;
            }
        }
    }

    private static string? BuildMetadataUrl(McpOAuthServerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.MetadataUrl))
            return config.MetadataUrl;

        if (!string.IsNullOrWhiteSpace(config.AuthorizationServer))
            return config.AuthorizationServer.TrimEnd('/') + WellKnownSuffix;

        return null;
    }

    private static readonly JsonSerializerOptions OAuthJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}

/// <summary>
/// OAuth token endpoint response (standard RFC 6749)
/// </summary>
public class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}
