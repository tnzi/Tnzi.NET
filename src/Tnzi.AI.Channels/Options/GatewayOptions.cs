namespace Tnzi.AI.Channels.Options;

/// <summary>
/// Gateway 配置选项
/// </summary>
[ConfigSection("AI:Channels:Gateway")]
[RuntimeSettingGroup(Key = "ai-channels", Module = "AI", DisplayName = "Channels",
    I18nKey = "admin.modules.system.settings.groups.aiChannels",
    Icon = "mdi:forum-outline", Order = 158)]
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
    [RuntimeSetting(Label = "Max Connections per User", I18n = "admin.modules.system.settings.fields.gatewayMaxConnectionsPerUser",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Gateway",
        Description = "Maximum concurrent WebSocket connections per user (anonymous connections share one UserId==null bucket bound by the same cap). Applied when a new connection is accepted.")]
    public int MaxConnectionsPerUser { get; set; } = 5;

    /// <summary>
    /// 是否要求 WebSocket 连接已认证。默认 false（兼容现有匿名开发客户端）。
    /// 设为 true 时，未携带已认证主体的连接在 AcceptWebSocketAsync 前被拒绝（401/关闭）。
    /// 生产环境强烈建议设为 true（AI:Channels:Gateway:RequireAuthentication=true）。
    /// </summary>
    public bool RequireAuthentication { get; set; }

    /// <summary>心跳间隔（秒）</summary>
    [RuntimeSetting(Label = "Heartbeat Interval (seconds)", I18n = "admin.modules.system.settings.fields.gatewayHeartbeatIntervalSeconds",
        Type = SettingFieldType.Int, Min = 5, Subsection = "Gateway",
        Description = "Interval between WebSocket heartbeat pings, in seconds. Applied to connections accepted after the change.")]
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>会话空闲驱逐时间（小时），超过此时间未活跃的会话将被自动清理</summary>
    [RuntimeSetting(Label = "Session Eviction (hours)", I18n = "admin.modules.system.settings.fields.gatewaySessionEvictionHours",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Gateway",
        Description = "Idle sessions inactive longer than this many hours are pruned on the next request.")]
    public int SessionEvictionHours { get; set; } = 24;

    /// <summary>流式更新最小间隔（毫秒），合并快速到达的 token 增量，避免刷爆 IM 平台的编辑/限流阈值</summary>
    [RuntimeSetting(Label = "Streaming Throttle (ms)", I18n = "admin.modules.system.settings.fields.gatewayStreamingThrottleMs",
        Type = SettingFieldType.Int, Min = 100, Subsection = "Gateway",
        Description = "Minimum interval between streamed reply updates pushed to IM platforms, in milliseconds. Rapid token deltas are coalesced to avoid flooding platform edit/rate limits. The first token and the final message are always delivered immediately. Takes effect on the next streamed reply.")]
    public int StreamingThrottleMs { get; set; } = 350;

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
