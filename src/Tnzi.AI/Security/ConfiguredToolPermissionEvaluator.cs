namespace Tnzi.AI.Security;

using Tnzi.AI.Options;

/// <summary>
/// 基于 AIOptions 动态配置的工具权限评估器。
/// </summary>
public sealed class ConfiguredToolPermissionEvaluator : IToolPermissionEvaluator, IDisposable
{
    private readonly IDisposable? _changeSubscription;
    private ToolPermissionEvaluator _currentEvaluator;

    public ConfiguredToolPermissionEvaluator(IOptionsMonitor<AIOptions> optionsMonitor)
    {
        Check.NotNull(optionsMonitor);

        _currentEvaluator = CreateEvaluator(optionsMonitor.CurrentValue);
        _changeSubscription = optionsMonitor.OnChange(options =>
        {
            Volatile.Write(ref _currentEvaluator, CreateEvaluator(options));
        });
    }

    /// <inheritdoc />
    public bool HasRules => Volatile.Read(ref _currentEvaluator).HasRules;

    /// <inheritdoc />
    public ToolPermissionDecision Evaluate(
        ToolPermissionContext context,
        IEnumerable<ToolPermissionRule>? additionalRules = null)
    {
        return Volatile.Read(ref _currentEvaluator).Evaluate(context, additionalRules);
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
