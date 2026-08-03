namespace Tnzi.AI.Services;

/// <summary>
/// MCP 工具提供者 - 从已配置的 MCP 服务器拉取工具列表，按 AllowedTools 过滤并按 McpServerConfig 做审批包装后返回。
/// 审批仅在此处按每服务器配置完成，AgentFactory 不再对 MCP 工具做全局 ToolApproval 包装。
/// </summary>
public interface IMcpToolProvider
{
    /// <summary>
    /// 获取所有已配置 MCP 服务器的工具（已过滤、已按每服务器审批配置包装），合并去重后返回。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>AITool 列表，可直接与 C# 工具合并传入 AgentExecutor</returns>
    Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default);

    /// <summary>
    /// 失效指定服务器或全部服务器的工具缓存，下次 GetToolsAsync 调用将重新从 MCP 服务器拉取。
    /// </summary>
    /// <param name="serverName">服务器名称；null 表示失效所有缓存</param>
    void InvalidateCache(string? serverName) { }

    /// <summary>
    /// 获取指定服务器缓存的工具名称列表。
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <returns>工具名称列表（只读）；服务器不存在或尚未缓存时返回空列表</returns>
    IReadOnlyList<string> GetServerToolNames(string serverName) => Array.Empty<string>();
}
