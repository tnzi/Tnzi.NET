namespace Tnzi.AI.Channels.Entities;

/// <summary>
/// 会话绑定规则 - 按优先级匹配入站请求到指定 Agent 和 Scope。
/// 实现 <see cref="IMultiTenant"/>：多租户部署下每条规则归属一个租户，
/// 入站流量只命中本租户的规则（TenantId=null 为部署级全局规则）。
/// </summary>
public class SessionBindingRule : AuditedEntity<Guid>, IMultiTenant
{
    /// <summary>所属租户（null = 单租户 / 部署级全局规则，匹配任意租户上下文）</summary>
    public Guid? TenantId { get; set; }

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
