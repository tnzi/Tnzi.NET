namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// 会话绑定输入上下文 - 提供给 DefaultSessionBinder 的请求信息
/// </summary>
public class SessionBindingContext
{
    /// <summary>频道名称</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>聊天/群组 ID</summary>
    public string ChatId { get; init; } = string.Empty;

    /// <summary>用户 ID</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Peer 类型</summary>
    public string? PeerKind { get; init; }

    /// <summary>话题 ID</summary>
    public string? TopicId { get; init; }

    /// <summary>显式指定的 Agent ID（优先级最高）</summary>
    public string? ExplicitAgentId { get; init; }

    /// <summary>
    /// Owning tenant resolved from channel config in multi-tenant deployments;
    /// null = single-tenant / global default.
    /// </summary>
    public Guid? TenantId { get; init; }
}
