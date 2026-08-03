namespace Tnzi.AI.Services;

/// <summary>
/// Agent 解析器 - 根据 agentId / provider / model / toolGroups 解析 AgentExecutor，
/// 以及从请求内容构建 ChatMessage
/// </summary>
public interface IAgentResolver
{
    /// <summary>
    /// 解析 Agent：根据 agentId / provider / model / toolGroups / toolNames 创建 AgentExecutor。
    /// <paramref name="toolNames"/> 为 per-request 单工具覆盖（无 AgentId 的 ad-hoc 路径），与 toolGroups 对称叠加；
    /// DB Agent 路径的 per-tool 授权来自 grant 投影，不需要此参数。
    /// </summary>
    Task<AgentResolution> ResolveAgentAsync(Guid? agentId, string? provider, string? model, List<string>? toolGroups, CancellationToken ct, List<string>? toolNames = null);

    /// <summary>
    /// 构建用户消息：支持纯文本和多模态内容（图片、文件）
    /// </summary>
    /// <param name="message">纯文本消息（与 content 二选一）</param>
    /// <param name="content">多模态内容部分列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含正确 AIContent 类型的 ChatMessage（文本返回 TextContent，图片返回 DataContent）</returns>
    Task<ChatMessage> BuildChatMessageAsync(string? message, List<ContentPartDto>? content, CancellationToken ct);
}
