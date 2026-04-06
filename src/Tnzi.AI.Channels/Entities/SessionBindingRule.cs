namespace Tnzi.AI.Channels.Entities;

/// <summary>
/// 会话绑定规则 — 按优先级匹配入站请求到指定 Agent 和 Scope
/// </summary>
public class SessionBindingRule : AuditedEntity<Guid>
{
    /// <summary>频道名称（null = 匹配所有频道）</summary>
    public string? Channel { get; set; }

    /// <summary>Peer 类型："user"/"group"（null = 匹配所有类型）</summary>
    public string? PeerKind { get; set; }

    /// <summary>Peer ID（null = 匹配所有 Peer）</summary>
    public string? PeerId { get; set; }

    /// <summary>绑定的 Agent ID</summary>
    public Guid AgentId { get; set; }

    /// <summary>会话作用域</summary>
    public SessionScope Scope { get; set; }

    /// <summary>优先级（越大越优先）</summary>
    public int Priority { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}
