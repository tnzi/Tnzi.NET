namespace Tnzi.AI.Mcp.Server;



/// <summary>
/// MCP Server Host - 将 Tnzi.AI 的 Agent 暴露为 MCP Server。
/// 外部 MCP Client（如 Claude Desktop、VS Code）可以调用这些 Agent 作为工具。
/// </summary>
public interface IMcpServerHost
{
    /// <summary>注册一个 Agent 为 MCP 工具</summary>
    void ExposeAgent(Guid agentId, McpToolExposureOptions? options = null);

    /// <summary>注册自定义 MCP 工具</summary>
    void ExposeTool(string name, string description, Func<JsonElement, Task<string>> handler);

    /// <summary>列出当前暴露的 MCP 工具</summary>
    Task<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>调用指定 MCP 工具</summary>
    Task<CallToolResult> CallToolAsync(
        string name,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken = default);

    /// <summary>移除已暴露的 Agent</summary>
    bool RemoveAgent(Guid agentId) => false;

    /// <summary>获取已暴露的 Agent ID 列表</summary>
    IReadOnlyList<Guid> GetExposedAgentIds() => [];

    /// <summary>获取已注册的自定义工具名称列表</summary>
    IReadOnlyList<string> GetCustomToolNames() => [];
}

/// <summary>
/// Agent 暴露为 MCP 工具时的选项
/// </summary>
public class McpToolExposureOptions
{
    /// <summary>自定义工具名称（默认使用 Agent 名称）</summary>
    public string? ToolName { get; init; }

    /// <summary>自定义描述（默认使用 Agent 描述）</summary>
    public string? Description { get; init; }
}
