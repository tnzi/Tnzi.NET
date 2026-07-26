namespace Tnzi.AI.Security;

/// <summary>
/// 基于 AIOptions 动态配置的工具权限评估器。
/// 支持配置文件规则 + 数据库持久化规则（通过 IServiceScopeFactory 异步加载）。
/// </summary>
public sealed class ConfiguredToolPermissionEvaluator : IToolPermissionEvaluator, IDisposable
{
    private readonly IDisposable? _changeSubscription;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<ConfiguredToolPermissionEvaluator> _logger;
    private ToolPermissionEvaluator _currentEvaluator;
    private IReadOnlyList<ToolPermissionRule> _cachedDbRules = [];

    public ConfiguredToolPermissionEvaluator(
        IOptionsMonitor<AIOptions> optionsMonitor,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<ConfiguredToolPermissionEvaluator>? logger = null)
    {
        Check.NotNull(optionsMonitor);

        _scopeFactory = scopeFactory;
        _logger = logger ?? NullLogger<ConfiguredToolPermissionEvaluator>.Instance;
        _currentEvaluator = CreateEvaluator(optionsMonitor.CurrentValue);
        _changeSubscription = optionsMonitor.OnChange(options =>
        {
            var replacement = CreateEvaluator(options);
            // 配置热更新不得丢弃会话级规则：Deny 类会话规则一旦丢失就是 fail-open
            //（被显式拒绝过的工具在下一次 appsettings 变更后重新可用）。
            foreach (var sessionRule in Volatile.Read(ref _currentEvaluator).GetSessionRules())
            {
                replacement.AddSessionRule(sessionRule);
            }
            Volatile.Write(ref _currentEvaluator, replacement);
        });

        // 异步初始加载 DB 规则（fire-and-forget）
        if (_scopeFactory != null)
        {
            _ = RefreshDbRulesAsync();
        }
    }

    /// <inheritdoc />
    public bool HasRules => Volatile.Read(ref _currentEvaluator).HasRules || Volatile.Read(ref _cachedDbRules).Count > 0;

    /// <inheritdoc />
    public ToolPermissionDecision Evaluate(
        ToolPermissionContext context,
        IEnumerable<ToolPermissionRule>? additionalRules = null)
    {
        var dbRules = Volatile.Read(ref _cachedDbRules);
        var merged = dbRules.Count > 0
            ? (additionalRules != null ? dbRules.Concat(additionalRules) : dbRules)
            : additionalRules;

        return Volatile.Read(ref _currentEvaluator).Evaluate(context, merged);
    }

    /// <inheritdoc />
    public void AddSessionRule(ToolPermissionRule rule)
    {
        Volatile.Read(ref _currentEvaluator).AddSessionRule(rule);
    }

    /// <inheritdoc />
    public void RemoveSessionRule(string toolPattern)
    {
        Volatile.Read(ref _currentEvaluator).RemoveSessionRule(toolPattern);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPermissionRule> GetSessionRules()
    {
        return Volatile.Read(ref _currentEvaluator).GetSessionRules();
    }

    /// <inheritdoc />
    public Task RefreshRulesAsync() => RefreshDbRulesAsync();

    /// <summary>
    /// 刷新数据库缓存的权限规则
    /// </summary>
    public async Task RefreshDbRulesAsync()
    {
        if (_scopeFactory == null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetService<IToolPermissionRuleStore>();
            if (store == null) return;

            var rules = await store.GetRulesAsync();
            Volatile.Write(ref _cachedDbRules, rules);
            _logger.LogDebug("Refreshed {Count} tool permission rules from database.", rules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh tool permission rules from database. Using cached rules.");
        }
    }

    public void Dispose()
    {
        _changeSubscription?.Dispose();
    }

    private static ToolPermissionEvaluator CreateEvaluator(AIOptions options)
    {
        var rules = ToolPermissionOptionsRuleAdapter.ToRules(options.Permissions);
        return new ToolPermissionEvaluator(rules);
    }
}
