namespace Tnzi.AI.Channels.Dtos;

/// <summary>
/// 会话绑定规则（管理端只读视图）。
/// </summary>
/// <remarks>
/// 刻意**不含** <c>TenantId</c> 与审计列：管理端看的是"规则怎么配的"，
/// 租户归属由查询时的全局过滤器保证，不需要也不应该出现在响应里。
/// </remarks>
public class SessionBindingRuleDto
{
    /// <summary>规则 ID</summary>
    public Guid Id { get; set; }

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
    public bool IsEnabled { get; set; }
}
