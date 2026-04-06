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

    /// <summary>Revocation Endpoint URL（可选，RFC 7009）</summary>
    public string? RevocationEndpoint { get; set; }

    /// <summary>
    /// 是否启用 OAuth metadata discovery。
    /// 启用后从 <see cref="MetadataUrl"/> 或 <see cref="AuthorizationServer"/> 派生的 well-known 地址
    /// 发现 token/revocation endpoint（未显式配置时）。
    /// </summary>
    public bool EnableMetadataDiscovery { get; set; }

    /// <summary>OAuth/OIDC metadata 文档地址（如 /.well-known/openid-configuration）</summary>
    public string? MetadataUrl { get; set; }

    /// <summary>授权服务器基地址。未显式提供 MetadataUrl 时，将自动拼接 well-known metadata 地址</summary>
    public string? AuthorizationServer { get; set; }
}
