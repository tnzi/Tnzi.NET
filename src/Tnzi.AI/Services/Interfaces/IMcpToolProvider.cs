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
    /// 为指定用户取工具：<see cref="GetToolsAsync(CancellationToken)"/> 的全部，加上消费方经
    /// <see cref="IUserMcpServerSource"/> 为该用户贡献的远程 MCP 服务器的工具。这些工具只在这个用户的调用里出现。
    /// <paramref name="userId"/> 为 null 时等同无参重载（不含任何用户专属服务器）。
    /// 无参重载取当前运行请求里的用户；没有运行上下文的调用方（例如经 MCP 回写通道转发的外部 CLI）用这个重载显式给出用户。
    /// 默认实现忽略用户，供不支持用户专属服务器的自定义实现沿用。
    /// </summary>
    Task<IReadOnlyList<AITool>> GetToolsAsync(Guid? userId, CancellationToken ct = default) => GetToolsAsync(ct);

    /// <summary>
    /// 只取该用户的专属服务器的工具（不含部署配置与注册表里的服务器）。给把调用转发到用户账号的路径用
    /// （外部 CLI 经 MCP 回写通道转发）：那条路径不该顺带暴露平台自己的 MCP 客户端工具。
    /// 默认实现返回空，供不支持用户专属服务器的自定义实现沿用。
    /// </summary>
    Task<IReadOnlyList<AITool>> GetUserToolsAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AITool>>(Array.Empty<AITool>());

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
