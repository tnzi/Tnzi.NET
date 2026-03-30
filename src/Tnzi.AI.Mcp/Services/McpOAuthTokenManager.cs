using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tnzi.AI.Mcp.Options;
using Tnzi.AI.Mcp.Services.Interfaces;
using Tnzi.Utilities;

namespace Tnzi.AI.Mcp.Services;

/// <summary>
/// OAuth Token 管理器 — per-server SemaphoreSlim + 双重检查锁定 + proactive refresh。
/// </summary>
public class McpOAuthTokenManager : IMcpOAuthTokenManager
{
    private readonly ILogger<McpOAuthTokenManager> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly McpOAuthOptions _options;

    // Per-server 缓存和锁
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly KeyedAsyncLock _serverLock = new();

    private record CachedToken(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

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

    private static bool IsExpiredOrNearExpiry(CachedToken token, int skewSeconds)
    {
        return DateTimeOffset.UtcNow >= token.ExpiresAt.AddSeconds(-skewSeconds);
    }

    private async Task<OAuthTokenResponse?> FetchTokenAsync(string serverName, McpOAuthServerConfig config, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Tnzi.AI.MCP.OAuth");
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

            var response = await client.PostAsync(config.TokenEndpoint, new FormUrlEncodedContent(parameters), ct);
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
