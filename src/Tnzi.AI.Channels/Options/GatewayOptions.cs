namespace Tnzi.AI.Channels.Options;

/// <summary>
/// Gateway 配置选项
/// </summary>
public class GatewayOptions
{
    /// <summary>是否启用 Gateway</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>WebSocket 路径</summary>
    public string Path { get; set; } = "/ws/gateway";

    /// <summary>默认 Agent ID（无规则匹配时使用）</summary>
    public Guid? DefaultAgentId { get; set; }

    /// <summary>默认会话作用域</summary>
    public SessionScope DefaultScope { get; set; } = SessionScope.PerPeer;

    /// <summary>每用户最大连接数（匿名连接共享 UserId==null 桶，同样受此上限约束）</summary>
    public int MaxConnectionsPerUser { get; set; } = 5;

    /// <summary>
    /// 是否要求 WebSocket 连接已认证。默认 false（兼容现有匿名开发客户端）。
    /// 设为 true 时，未携带已认证主体的连接在 AcceptWebSocketAsync 前被拒绝（401/关闭）。
    /// 生产环境强烈建议设为 true（AI:Channels:Gateway:RequireAuthentication=true）。
    /// </summary>
    public bool RequireAuthentication { get; set; }

    /// <summary>心跳间隔（秒）</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>会话空闲驱逐时间（小时），超过此时间未活跃的会话将被自动清理</summary>
    public int SessionEvictionHours { get; set; } = 24;

    /// <summary>JSON 配置中的绑定规则（数据库规则的补充）</summary>
    public List<SessionBindingRuleConfig>? BindingRules { get; set; }
}

/// <summary>
/// JSON 配置格式的绑定规则
/// </summary>
public class SessionBindingRuleConfig
{
    /// <summary>频道名称（null = 匹配所有）</summary>
    public string? Channel { get; set; }

    /// <summary>Peer 类型（null = 匹配所有）</summary>
    public string? PeerKind { get; set; }

    /// <summary>Peer ID（null = 匹配所有）</summary>
    public string? PeerId { get; set; }

    /// <summary>绑定的 Agent ID</summary>
    public Guid AgentId { get; set; }

    /// <summary>会话作用域</summary>
    public SessionScope Scope { get; set; } = SessionScope.PerPeer;

    /// <summary>优先级（越大越优先）</summary>
    public int Priority { get; set; }
}
