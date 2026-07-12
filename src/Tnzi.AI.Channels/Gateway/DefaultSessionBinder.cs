namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// 默认会话绑定解析器 — 优先级匹配规则，无匹配时使用 GatewayOptions 默认值。
/// 规则来源：配置（GatewayOptions.BindingRules，启动时冻结）+ 数据库（启用的 SessionBindingRule 行，
/// 带缓存按 TTL 刷新；绝不在每次 Resolve 时查库）。同优先级下配置规则胜出。
/// </summary>
/// <remarks>
/// 数据库规则（<see cref="SessionBindingRule"/>）是 <c>IMultiTenant</c> 实体，按 <c>context.TenantId</c> 分区：
/// 一条带 TenantId 的 DB 规则只匹配同租户的上下文；TenantId=null 的 DB 规则（单租户部署）与
/// 配置规则（<c>GatewayOptions.BindingRules</c>）均为部署级全局规则，匹配任意上下文。
/// 后台缓存在无当前租户的全新作用域里加载全部 DB 规则（临时禁用多租户过滤器），
/// 隔离改在匹配时强制——缓存为服务器内部数据，不对外暴露。
/// </remarks>
public class DefaultSessionBinder : ISessionBinder
{
    private readonly IReadOnlyList<SessionBindingRule> _configRules;
    private readonly IOptionsMonitor<GatewayOptions> _options;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly TimeSpan _cacheTtl;

    // 缓存：合并后的（配置 + 数据库）规则，按优先级降序、来源（配置优先）排序。
    private readonly object _cacheLock = new();
    private List<SessionBindingRule>? _mergedRulesCache;
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    public DefaultSessionBinder(
        IReadOnlyList<SessionBindingRule> rules,
        IOptionsMonitor<GatewayOptions> options,
        IServiceScopeFactory? scopeFactory = null,
        TimeSpan? cacheTtl = null)
    {
        Check.NotNull(rules);
        Check.NotNull(options);

        _configRules = rules;
        _options = options;
        _scopeFactory = scopeFactory;
        // 默认 5 分钟 TTL — 避免每次 Resolve 查库，同时让 admin 写入后较快生效。
        _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc />
    public SessionBinding Resolve(SessionBindingContext context)
    {
        Check.NotNull(context);

        // 默认作用域/默认 Agent 为 KEEP-STATIC（不进配置中心热设置），但仍经 IOptionsMonitor
        // 读取以避免运行时热设置消费审计误报，并在 appsettings 重载时保持一致。
        var options = _options.CurrentValue;

        // 显式指定 AgentId 时直接使用
        if (!string.IsNullOrEmpty(context.ExplicitAgentId) && Guid.TryParse(context.ExplicitAgentId, out var explicitId))
        {
            return BuildBinding(explicitId, options.DefaultScope, context);
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
        var defaultAgentId = options.DefaultAgentId ?? Guid.Empty;
        return BuildBinding(defaultAgentId, options.DefaultScope, context);
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

            // SessionBindingRule 是 IMultiTenant：这个全新作用域没有当前租户，
            // 多租户全局过滤器会变成 e.TenantId == null，从而隐藏所有带租户的规则。
            // 在此临时禁用多租户过滤器，把所有租户的规则一并加载进缓存；
            // 真正的隔离由 MatchesRule 在匹配时按 context.TenantId 强制（缓存是服务器内部数据）。
            // 同步阻塞：Resolve 是同步签名，且此查询每 TTL 周期最多一次。
            var filterManager = scope.ServiceProvider.GetService<IDataFilterManager>();
            if (filterManager != null)
            {
                using (filterManager.Disable<IMultiTenantFilter>())
                {
                    return repository.ToListAsync(r => r.IsEnabled).GetAwaiter().GetResult();
                }
            }

            // 没有过滤器管理器（极少见）→ 直接查询；多租户开启时只会拿到 null 租户规则。
            return repository.ToListAsync(r => r.IsEnabled).GetAwaiter().GetResult();
        }
        catch
        {
            // 数据库不可用/仓储未注册 — 降级为仅配置规则。
            return [];
        }
    }

    private static bool MatchesRule(SessionBindingRule rule, SessionBindingContext context)
    {
        // 租户分区：带 TenantId 的 DB 规则只命中同租户的上下文；
        // TenantId=null 的规则（配置规则 + 单租户部署的 DB 规则）为部署级全局规则，匹配任意上下文。
        if (rule.TenantId.HasValue && rule.TenantId != context.TenantId)
        {
            return false;
        }

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
