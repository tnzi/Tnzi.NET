// 与 ModelContextProtocol.Protocol.Result 同名
using Result = Tnzi.Results.Result;

namespace Tnzi.AI.Mcp.Server;



/// <summary>
/// MCP Server Host - 将 Tnzi.AI 的 Agent 暴露为 MCP Server。
/// 外部 MCP Client（如 Claude Desktop、VS Code）可以调用这些 Agent 作为工具。
/// </summary>
public interface IMcpServerHost
{
    /// <summary>注册一个 Agent 为 MCP 工具</summary>
    void ExposeAgent(Guid agentId, McpToolExposureOptions? options = null);

    /// <summary>
    /// 先确认调用方看得见这个 Agent 且已启用、指定的工具名合规，再注册它（管理端用这个入口）。
    /// </summary>
    /// <remarks>
    /// 找不到答 404，已停用或工具名不合规答 400，都不写入注册表。暴露只存在于本进程：
    /// 重启后只有 <c>AI:McpServer:ExposedAgentIds</c> 里列出的会恢复，多实例之间也不共享。
    /// 默认实现只做注册，供不需要校验的自定义实现沿用。
    /// </remarks>
    Task<Result> ExposeAgentAsync(Guid agentId, McpToolExposureOptions? options = null, CancellationToken cancellationToken = default)
    {
        ExposeAgent(agentId, options);
        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// 注册自定义 MCP 工具。对外宣告的输入是一个字符串参数 <c>input</c>，handler 收到 <c>tools/call</c> 的参数对象
    /// <c>{"input": "..."}</c>，返回的字符串作为一个文本块发出；要别的参数形状、调用方信息、取消令牌或别的结果，
    /// 用 <see cref="ExposeTool(McpToolDefinition)"/>。
    /// </summary>
    void ExposeTool(string name, string description, Func<JsonElement, Task<string>> handler);

    /// <summary>
    /// 注册自定义 MCP 工具并声明它的输入 schema（JSON Schema，顶层 <c>type</c> 必须是 <c>object</c>）。
    /// <c>tools/list</c> 原样宣告这份 schema，<c>tools/call</c> 的参数对象原样交给 handler，返回的字符串作为一个文本块发出。
    /// </summary>
    void ExposeTool(string name, string description, JsonElement inputSchema, Func<JsonElement, Task<string>> handler)
        => throw new NotSupportedException($"{GetType().Name} does not support declaring an input schema for custom MCP tools.");

    /// <summary>
    /// 注册自定义 MCP 工具（完整形态）：声明的 schema 原样宣告；handler 拿到原样的参数对象、调用方、
    /// 本次调用的 DI 作用域与取消令牌，返回的内容块与 <c>IsError</c> 原样发给客户端。见 <see cref="McpToolDefinition"/>。
    /// </summary>
    void ExposeTool(McpToolDefinition tool)
        => throw new NotSupportedException($"{GetType().Name} does not support custom MCP tool definitions.");

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

    /// <summary>
    /// 这个 Agent 是否列在 <c>AI:McpServer:ExposedAgentIds</c> 里：是的话，移除只维持到进程重启。
    /// </summary>
    bool IsConfiguredAgent(Guid agentId) => false;

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
