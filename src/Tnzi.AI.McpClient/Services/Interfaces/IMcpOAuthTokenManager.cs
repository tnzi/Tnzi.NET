namespace Tnzi.AI.McpClient.Services.Interfaces;

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

    /// <summary>
    /// 撤销指定 MCP Server 的 OAuth 令牌（RFC 7009）并清除本地缓存。
    /// 如果未配置 RevocationEndpoint 且 metadata discovery 也无法获取，仅清除本地缓存。
    /// </summary>
    /// <returns>true 表示远程撤销成功或无需撤销（无缓存 token），false 表示远程撤销失败（本地缓存仍被清除）</returns>
    Task<bool> RevokeAsync(string serverName, CancellationToken ct = default)
        => Task.FromResult(true);
}
