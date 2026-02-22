namespace Tnzi.AI.Services.Interfaces;

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
}
