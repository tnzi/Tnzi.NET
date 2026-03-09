namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 工厂接口
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// 异步创建 AgentExecutor（支持 MCP 工具拉取与合并；当 AI:Mcp:Enabled 时无论 toolGroups 是否为空都会合并 MCP 工具）
    /// </summary>
    /// <param name="providerName">提供商名称（为空则使用默认提供商）</param>
    /// <param name="model">模型名称（为空则使用提供商默认模型）</param>
    /// <param name="instructions">系统指令</param>
    /// <param name="name">Agent 名称</param>
    /// <param name="toolGroups">工具组列表（为空时若启用 MCP 则 Agent 仅带 MCP 工具）</param>
    /// <param name="temperature">温度参数</param>
    /// <param name="maxTokens">最大 Token 数</param>
    /// <param name="options">自定义 AgentExecutorOptions（可选，用于注入 HistoryReducer/ContextProvider）；不会原地修改，内部使用副本合并参数</param>
    /// <param name="userPermissions">用户权限列表（为空时不过滤工具权限）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>AgentExecutor 实例</returns>
    Task<AgentExecutor> CreateAgentAsync(
        string? providerName = null,
        string? model = null,
        string? instructions = null,
        string? name = null,
        IEnumerable<string>? toolGroups = null,
        double? temperature = null,
        int? maxTokens = null,
        AgentExecutorOptions? options = null,
        IEnumerable<string>? userPermissions = null,
        CancellationToken ct = default);
}
