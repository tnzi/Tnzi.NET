namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP Prompt 模板发现与获取 - 从已配置的 MCP 服务器获取 Prompt 模板。
/// 单个服务器不可用时记录警告并跳过，不阻塞整体。
/// </summary>
public interface IMcpPromptProvider
{
    /// <summary>
    /// 列出指定 MCP 服务器提供的 Prompt 模板。
    /// </summary>
    /// <param name="serverName">服务器名称（对应 McpServerConfig.Name）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Prompt 信息列表；服务器不存在或不可用时返回空列表</returns>
    Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(string serverName, CancellationToken ct = default);

    /// <summary>
    /// 获取指定 MCP 服务器上的 Prompt 模板渲染结果。
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <param name="promptName">Prompt 名称</param>
    /// <param name="arguments">Prompt 参数（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Prompt 结果；服务器不存在、不可用或 Prompt 不存在时返回 null</returns>
    Task<McpPromptResult?> GetPromptAsync(string serverName, string promptName, Dictionary<string, string>? arguments = null, CancellationToken ct = default);

    /// <summary>
    /// 列出所有已配置 MCP 服务器的全部 Prompt 模板。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>所有服务器的 Prompt 信息合并列表</returns>
    Task<IReadOnlyList<McpPromptInfo>> ListAllPromptsAsync(CancellationToken ct = default);
}

/// <summary>
/// MCP Prompt 模板信息（来自 prompts/list 响应）
/// </summary>
/// <param name="Name">Prompt 名称</param>
/// <param name="Description">Prompt 描述（可选）</param>
/// <param name="Arguments">Prompt 参数定义列表（可选）</param>
public record McpPromptInfo(string Name, string? Description, IReadOnlyList<McpPromptArgument>? Arguments);

/// <summary>
/// MCP Prompt 参数定义
/// </summary>
/// <param name="Name">参数名称</param>
/// <param name="Description">参数描述（可选）</param>
/// <param name="Required">是否必填</param>
public record McpPromptArgument(string Name, string? Description, bool Required);

/// <summary>
/// MCP Prompt 渲染结果（来自 prompts/get 响应）
/// </summary>
/// <param name="Messages">消息列表</param>
public record McpPromptResult(IReadOnlyList<McpPromptMessage> Messages);

/// <summary>
/// MCP Prompt 消息
/// </summary>
/// <param name="Role">角色（user/assistant）</param>
/// <param name="Content">消息内容</param>
public record McpPromptMessage(string Role, string Content);
