namespace Tnzi.AI.Tests.Security;

public class ConfiguredToolPermissionEvaluatorTests
{
    [Fact]
    public void HasRules_ReflectsInitialConfiguration()
    {
        var monitor = new MutableOptionsMonitor<AIOptions>(new AIOptions
        {
            Permissions = new ToolPermissionOptions
            {
                Enabled = true,
                SystemRules =
                [
                    new ToolPermissionRuleOptions
                    {
                        ToolPattern = "bash",
                        Behavior = PermissionBehavior.Deny
                    }
                ]
            }
        });

        using var evaluator = new ConfiguredToolPermissionEvaluator(monitor);

        evaluator.HasRules.ShouldBeTrue();
    }

    [Fact]
    public void Evaluate_UsesUpdatedRulesAfterConfigurationChange()
    {
        var monitor = new MutableOptionsMonitor<AIOptions>(new AIOptions
        {
            Permissions = new ToolPermissionOptions
            {
                Enabled = false
            }
        });

        using var evaluator = new ConfiguredToolPermissionEvaluator(monitor);

        evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" }).Behavior.ShouldBe(PermissionBehavior.Allow);

        monitor.Set(new AIOptions
        {
            Permissions = new ToolPermissionOptions
            {
                Enabled = true,
                SessionRules =
                [
                    new ToolPermissionRuleOptions
                    {
                        ToolPattern = "bash",
                        Behavior = PermissionBehavior.Deny,
                        Reason = "Blocked after reload"
                    }
                ]
            }
        });

        var decision = evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Blocked after reload");
        decision.Scope.ShouldBe(ToolPermissionScope.Session);
    }
}

file sealed class MutableOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly List<Action<T, string?>> _listeners = [];

    public MutableOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; private set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new CallbackDisposable(() => _listeners.Remove(listener));
    }

    public void Set(T value, string? name = null)
    {
        CurrentValue = value;
        foreach (var listener in _listeners.ToArray())
        {
            listener(value, name);
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }
}
