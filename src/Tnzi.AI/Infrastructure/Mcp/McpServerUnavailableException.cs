namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 服务器刚刚整轮建连失败、仍在冷却窗口内：本次请求不再尝试建连，立即失败。
/// </summary>
/// <remarks>
/// 调用方（工具 / 资源 / 提示提供器）按服务器跳过它即可；真正的失败原因在 <see cref="Exception.InnerException"/>，
/// 已在那一次建连时按警告记过日志，冷却期内的每次请求不必再记一遍。
/// </remarks>
public sealed class McpServerUnavailableException : InfrastructureException
{
    public McpServerUnavailableException(string serverName, DateTimeOffset retryAfter, Exception lastError)
        : base("Mcp", $"MCP server '{serverName}' could not be reached; the next connection attempt is allowed after {retryAfter:O}.", isRetryable: true, lastError)
    {
        ServerName = serverName;
        RetryAfter = retryAfter;
    }

    /// <summary>服务器名称</summary>
    public string ServerName { get; }

    /// <summary>冷却结束、允许再次建连的时刻（UTC）</summary>
    public DateTimeOffset RetryAfter { get; }
}
