namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// 默认会话绑定解析器 — 优先级匹配规则，无匹配时使用 GatewayOptions 默认值。
/// 规则来源：配置（GatewayOptions.BindingRules，启动时冻结）+ 数据库（启用的 SessionBindingRule 行，
/// 带缓存按 TTL 刷新；绝不在每次 Resolve 时查库）。同优先级下配置规则胜出。
/// </summary>
/// <remarks>
/// SessionBindingRule 非 <c>IMultiTenant</c>，因此数据库规则被视为全局规则。
/// </remarks>
public class DefaultSessionBinder : ISessionBinder
{
    private readonly IReadOnlyList<SessionBindingRule> _configRules;
    private readonly GatewayOptions _options;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly TimeSpan _cacheTtl;

    // 缓存：合并后的（配置 + 数据库）规则，按优先级降序、来源（配置优先）排序。
    private readonly object _cacheLock = new();
    private List<SessionBindingRule>? _mergedRulesCache;
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    public DefaultSessionBinder(
        IReadOnlyList<SessionBindingRule> rules,
        IOptions<GatewayOptions> options,
        IServiceScopeFactory? scopeFactory = null,
        TimeSpan? cacheTtl = null)
    {
        Check.NotNull(rules);
        Check.NotNull(options);

        _configRules = rules;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        // 默认 5 分钟 TTL — 避免每次 Resolve 查库，同时让 admin 写入后较快生效。
        _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(5);
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

        // 按优先级迭代规则（配置 + 缓存的数据库规则），首个匹配者胜出
        foreach (var rule in GetMergedRules())
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

    /// <summary>
    /// 获取合并并排序后的规则列表（配置 + 数据库），带 TTL 缓存。
    /// 数据库查询每个 TTL 周期最多一次，绝不在每次 Resolve 时进行。
    /// </summary>
    private IReadOnlyList<SessionBindingRule> GetMergedRules()
    {
        var now = DateTimeOffset.UtcNow;

        lock (_cacheLock)
        {
            if (_mergedRulesCache != null && now < _cacheExpiresAt)
            {
                return _mergedRulesCache;
            }

            var dbRules = LoadDbRules();

            // 合并：配置规则在前（同优先级胜出），数据库规则在后；统一按优先级降序稳定排序。
            // OrderByDescending 是稳定排序 → 同 Priority 下保持"配置先于数据库"的相对顺序。
            var merged = _configRules
                .Concat(dbRules)
                .OrderByDescending(r => r.Priority)
                .ToList();

            _mergedRulesCache = merged;
            _cacheExpiresAt = now.Add(_cacheTtl);
            return merged;
        }
    }

    /// <summary>
    /// 从数据库加载启用的绑定规则（通过新作用域解析 scoped 仓储，因为绑定器是 Singleton）。
    /// 任何失败都降级为"无数据库规则"，绝不让绑定不可用。
    /// </summary>
    private List<SessionBindingRule> LoadDbRules()
    {
        if (_scopeFactory == null)
        {
            return [];
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetService<IRepository<SessionBindingRule, Guid>>();
            if (repository == null)
            {
                return [];
            }

            // 仅加载启用的规则；非 IMultiTenant → 视为全局规则。
            // 同步阻塞：Resolve 是同步签名，且此查询每 TTL 周期最多一次。
            var rules = repository.ToListAsync(r => r.IsEnabled).GetAwaiter().GetResult();
            return rules;
        }
        catch
        {
            // 数据库不可用/仓储未注册 — 降级为仅配置规则。
            return [];
        }
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
