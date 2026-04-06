namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// 默认会话绑定解析器 — 优先级匹配规则，无匹配时使用 GatewayOptions 默认值
/// </summary>
public class DefaultSessionBinder : ISessionBinder
{
    private readonly IReadOnlyList<SessionBindingRule> _rules;
    private readonly GatewayOptions _options;

    public DefaultSessionBinder(IReadOnlyList<SessionBindingRule> rules, IOptions<GatewayOptions> options)
    {
        Check.NotNull(rules);
        Check.NotNull(options);

        // 按优先级降序排列（高优先级先匹配）
        _rules = rules.OrderByDescending(r => r.Priority).ToList();
        _options = options.Value;
    }

    /// <inheritdoc />
    public SessionBinding Resolve(SessionBindingContext context)
    {
        Check.NotNull(context);

        // 显式指定 AgentId 时直接使用
        if (!string.IsNullOrEmpty(context.ExplicitAgentId) && Guid.TryParse(context.ExplicitAgentId, out var explicitId))
        {
            return BuildBinding(explicitId, _options.DefaultScope, context);
        }

        // 按优先级迭代规则，首个匹配者胜出
        foreach (var rule in _rules)
        {
            if (!rule.IsEnabled) continue;
            if (MatchesRule(rule, context))
            {
                return BuildBinding(rule.AgentId, rule.Scope, context);
            }
        }

        // 无匹配 — 使用默认配置
        var defaultAgentId = _options.DefaultAgentId ?? Guid.Empty;
        return BuildBinding(defaultAgentId, _options.DefaultScope, context);
    }

    private static bool MatchesRule(SessionBindingRule rule, SessionBindingContext context)
    {
        // Channel: null 匹配所有
        if (rule.Channel is not null &&
            !string.Equals(rule.Channel, context.Channel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // PeerKind: null 匹配所有
        if (rule.PeerKind is not null &&
            !string.Equals(rule.PeerKind, context.PeerKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // PeerId: null 匹配所有
        if (rule.PeerId is not null &&
            !string.Equals(rule.PeerId, context.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static SessionBinding BuildBinding(Guid agentId, SessionScope scope, SessionBindingContext context)
    {
        var sessionKey = scope switch
        {
            SessionScope.Global => $"agent:{agentId:N}:global",
            SessionScope.PerPeer => $"agent:{agentId:N}:peer:{context.UserId ?? context.ChatId}",
            SessionScope.PerChannelPeer => $"agent:{agentId:N}:{context.Channel}:peer:{context.UserId ?? context.ChatId}",
            SessionScope.PerThread => $"agent:{agentId:N}:{context.Channel}:{context.ChatId}:thread",
            _ => $"agent:{agentId:N}:global"
        };

        return new SessionBinding
        {
            AgentId = agentId,
            Scope = scope,
            SessionKey = sessionKey
        };
    }
}
