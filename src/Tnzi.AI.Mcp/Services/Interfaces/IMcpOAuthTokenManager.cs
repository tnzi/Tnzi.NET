namespace Tnzi.AI.Mcp.Services.Interfaces;

/// <summary>
/// MCP Server OAuth Token 管理器 — 每个 Server 独立的 Token 生命周期管理
/// </summary>
public interface IMcpOAuthTokenManager
{
    /// <summary>
    /// 获取指定 MCP Server 的 Authorization header 值（例如 "Bearer xxx"）。
    /// 返回 null 表示该 Server 未配置 OAuth。
    /// </summary>
    Task<string?> GetAuthorizationHeaderAsync(string serverName, CancellationToken ct = default);
}
