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

    /// <summary>显式指定的 Agent ID（优先级最高，短路全部规则）</summary>
    public string? ExplicitAgentId { get; init; }

    /// <summary>
    /// 兜底 Agent ID（渠道默认值）：仅在没有任何规则命中时生效，优先于 <c>GatewayOptions.DefaultAgentId</c>。
    /// 解析优先级：<see cref="ExplicitAgentId"/> &gt; 绑定规则 &gt; <see cref="FallbackAgentId"/> &gt; Gateway 默认。
    /// </summary>
    public Guid? FallbackAgentId { get; init; }

    /// <summary>
    /// Owning tenant resolved from channel config in multi-tenant deployments;
    /// null = single-tenant / global default.
    /// </summary>
    public Guid? TenantId { get; init; }
}
