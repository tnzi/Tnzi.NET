namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 资源发现与读取 — 从已配置的 MCP 服务器获取上下文资源。
/// 单个服务器不可用时记录警告并跳过，不阻塞整体。
/// </summary>
public interface IMcpResourceProvider
{
    /// <summary>
    /// 列出指定 MCP 服务器提供的资源。
    /// </summary>
    /// <param name="serverName">服务器名称（对应 McpServerConfig.Name）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>资源信息列表；服务器不存在或不可用时返回空列表</returns>
    Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(string serverName, CancellationToken ct = default);

    /// <summary>
    /// 读取指定 MCP 服务器上的资源内容。
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <param name="uri">资源 URI</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>资源内容；服务器不存在、不可用或资源不存在时返回 null</returns>
    Task<McpResourceContent?> ReadResourceAsync(string serverName, string uri, CancellationToken ct = default);

    /// <summary>
    /// 列出所有已配置 MCP 服务器的全部资源。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>所有服务器的资源信息合并列表</returns>
    Task<IReadOnlyList<McpResourceInfo>> ListAllResourcesAsync(CancellationToken ct = default);
}

/// <summary>
/// MCP 资源信息（来自 resources/list 响应）
/// </summary>
/// <param name="Uri">资源唯一标识 URI</param>
/// <param name="Name">资源显示名称</param>
/// <param name="MimeType">MIME 类型（可选）</param>
/// <param name="Description">资源描述（可选）</param>
public record McpResourceInfo(string Uri, string Name, string? MimeType, string? Description);

/// <summary>
/// MCP 资源内容（来自 resources/read 响应）
/// </summary>
/// <param name="Text">资源文本内容</param>
/// <param name="MimeType">MIME 类型（可选）</param>
public record McpResourceContent(string Text, string? MimeType);
