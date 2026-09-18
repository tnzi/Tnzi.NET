namespace Tnzi.AI.Options;

/// <summary>
/// 无 AgentId 的请求自选工具的服务端允许列表（<c>AI:AdHocTools</c>）。
/// </summary>
/// <remarks>
/// <para>
/// HTTP 聊天体（<c>ChatRequestDto.ToolGroups</c>）里的工具组原先原样进解析器：任何登录用户自填
/// <c>["sandbox"]</c> 就能拿到宿主上的 bash，自填 <c>["task"]</c> 就能列出并取消租户内别人的运行。
/// <c>AgentRuntime</c> 现在把无 AgentId 且未标记 <c>AgentRunRequest.TrustedToolSelection</c> 的请求
/// 逐个对照这两张表，不在表里的一律 403（不静默丢弃）。
/// </para>
/// <para>
/// 默认两张表都为空 = 客户端自选一律拒绝。要让终端用户在聊天里自选 <c>datetime</c> / <c>text</c> 这类
/// 无害工具组，把它们写进 <see cref="AllowedGroups"/>；有 AgentId 的请求走实体授权，与本表无关。
/// </para>
/// </remarks>
public class AdHocToolsOptions
{
    /// <summary>
    /// 允许客户端自选的工具组（忽略大小写）。默认空 = 全部拒绝。
    /// </summary>
    public List<string> AllowedGroups { get; set; } = [];

    /// <summary>
    /// 允许客户端自选的单个工具名（<c>AgentRunRequest.ToolNames</c>，忽略大小写）。默认空 = 全部拒绝。
    /// </summary>
    public List<string> AllowedTools { get; set; } = [];
}
