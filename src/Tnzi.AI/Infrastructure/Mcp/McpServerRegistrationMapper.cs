namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// McpServerRegistration（DB 注册条目）→ McpServerConfig（运行时配置）映射。
/// McpServerCatalog（批量物化）与 McpServerRegistryService.TestConnectionAsync（单条探测）共用，
/// 保证两条路径生成的运行时配置一致。
/// </summary>
internal static class McpServerRegistrationMapper
{
    // MCP 标准 API Key Header（与 McpClientFactory / McpServerSecurityMiddleware 共享语义）
    private const string ApiKeyHeaderName = "X-Api-Key";

    /// <summary>注册表允许的 transport 值（HTTP 系；stdio 仅允许出现在部署配置中）</summary>
    internal static readonly string[] AllowedTransports = ["sse", "streamable-http", "http"];

    /// <summary>transport 是否为注册表允许的 HTTP 系取值</summary>
    internal static bool IsAllowedTransport(string? transport) =>
        AllowedTransports.Contains(transport?.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 将注册条目映射为运行时 McpServerConfig。
    /// </summary>
    /// <param name="entity">注册条目（调用方负责保证 transport 合法）</param>
    /// <param name="decryptAuthToken">凭证解密委托（密文 → 明文）；密文为空时不调用</param>
    /// <exception cref="NotSupportedException">transport 不是 HTTP 系取值（含 legacy stdio 行）</exception>
    internal static McpServerConfig ToServerConfig(
        McpServerRegistration entity,
        Func<string, string> decryptAuthToken)
    {
        Check.NotNull(entity);
        Check.NotNull(decryptAuthToken);

        if (!IsAllowedTransport(entity.Transport))
        {
            throw new NotSupportedException(
                $"MCP server registration '{entity.Name}' has unsupported transport '{entity.Transport}'. " +
                "Only sse / streamable-http / http are allowed in the runtime registry; " +
                "stdio servers must be configured via deployment configuration (AI:Mcp options).");
        }

        var config = new McpServerConfig
        {
            Name = entity.Name,
            Description = entity.Description,
            Endpoint = entity.ServerUrl,
            ConnectionType = McpConnectionType.Http
        };

        if (!string.IsNullOrEmpty(entity.AuthTokenEncrypted))
        {
            var token = decryptAuthToken(entity.AuthTokenEncrypted);
            switch (entity.AuthType?.Trim().ToLowerInvariant())
            {
                case "api-key":
                    config.Headers[ApiKeyHeaderName] = token;
                    break;
                default:
                    // bearer / oauth（静态 token）/ 未指定 → Authorization: Bearer。
                    // 动态 OAuth 流（client_credentials）仅支持部署配置（McpServerConfig.OAuth）。
                    config.Headers["Authorization"] = $"Bearer {token}";
                    break;
            }
        }

        return config;
    }
}
