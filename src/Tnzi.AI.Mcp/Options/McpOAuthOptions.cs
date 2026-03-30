namespace Tnzi.AI.Mcp.Options;

/// <summary>
/// MCP Server OAuth 配置
/// </summary>
public class McpOAuthOptions
{
    /// <summary>每个 MCP Server 的 OAuth 配置</summary>
    public Dictionary<string, McpOAuthServerConfig> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 单个 MCP Server 的 OAuth 配置
/// </summary>
public class McpOAuthServerConfig
{
    /// <summary>Token 端点 URL</summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>Client ID</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client Secret</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Grant Type（client_credentials 或 refresh_token）</summary>
    public string GrantType { get; set; } = "client_credentials";

    /// <summary>Refresh Token（GrantType=refresh_token 时使用）</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Scope（可选）</summary>
    public string? Scope { get; set; }

    /// <summary>提前刷新偏移量（秒），默认 60s</summary>
    public int RefreshSkewSeconds { get; set; } = 60;
}
