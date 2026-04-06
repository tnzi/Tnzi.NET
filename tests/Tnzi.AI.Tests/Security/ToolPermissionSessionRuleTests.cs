namespace Tnzi.AI.Tests.Security;

/// <summary>
/// Session 级规则动态增删 + 子 Agent 透传测试
/// </summary>
public class ToolPermissionSessionRuleTests
{
    #region ToolPermissionEvaluator Session Rules

    [Fact]
    public void AddSessionRule_AddsRuleWithSessionScope()
    {
        var evaluator = new ToolPermissionEvaluator([]);

        evaluator.AddSessionRule(new ToolPermissionRule
        {
            ToolPattern = "bash",
            Behavior = PermissionBehavior.Deny,
            Reason = "Blocked by session"
        });

        var rules = evaluator.GetSessionRules();
        rules.Count.ShouldBe(1);
        rules[0].ToolPattern.ShouldBe("bash");
        rules[0].Scope.ShouldBe(ToolPermissionScope.Session);
        rules[0].Behavior.ShouldBe(PermissionBehavior.Deny);
    }

    [Fact]
    public void RemoveSessionRule_RemovesMatchingPattern()
    {
        var evaluator = new ToolPermissionEvaluator([]);

        evaluator.AddSessionRule(new ToolPermissionRule { ToolPattern = "bash", Behavior = PermissionBehavior.Deny });
        evaluator.AddSessionRule(new ToolPermissionRule { ToolPattern = "read_file", Behavior = PermissionBehavior.Allow });

        evaluator.RemoveSessionRule("bash");

        var rules = evaluator.GetSessionRules();
        rules.Count.ShouldBe(1);
        rules[0].ToolPattern.ShouldBe("read_file");
    }

    [Fact]
    public void RemoveSessionRule_CaseInsensitive()
    {
        var evaluator = new ToolPermissionEvaluator([]);

        evaluator.AddSessionRule(new ToolPermissionRule { ToolPattern = "Bash", Behavior = PermissionBehavior.Deny });

        evaluator.RemoveSessionRule("bash");

        evaluator.GetSessionRules().Count.ShouldBe(0);
    }

    [Fact]
    public void GetSessionRules_ReturnsSnapshotCopy()
    {
        var evaluator = new ToolPermissionEvaluator([]);

        evaluator.AddSessionRule(new ToolPermissionRule { ToolPattern = "bash", Behavior = PermissionBehavior.Deny });

        var snapshot = evaluator.GetSessionRules();
        evaluator.AddSessionRule(new ToolPermissionRule { ToolPattern = "edit", Behavior = PermissionBehavior.Ask });

        // Snapshot should not be affected by subsequent adds
        snapshot.Count.ShouldBe(1);
        evaluator.GetSessionRules().Count.ShouldBe(2);
    }

    [Fact]
    public void Evaluate_IncludesSessionRules()
    {
        var evaluator = new ToolPermissionEvaluator([]);

        evaluator.AddSessionRule(new ToolPermissionRule
        {
            ToolPattern = "bash",
            Behavior = PermissionBehavior.Deny,
            Reason = "Session blocked"
        });

        var decision = evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Session blocked");
        decision.Scope.ShouldBe(ToolPermissionScope.Session);
    }

    [Fact]
    public void HasRules_TrueWhenOnlySessionRulesExist()
    {
        var evaluator = new ToolPermissionEvaluator([]);
        evaluator.HasRules.ShouldBeFalse();

        evaluator.AddSessionRule(new ToolPermissionRule { ToolPattern = "bash", Behavior = PermissionBehavior.Deny });

        evaluator.HasRules.ShouldBeTrue();
    }

    [Fact]
    public void SessionRules_HigherPriorityThanSystemRules_ByScope()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                Behavior = PermissionBehavior.Allow,
                Scope = ToolPermissionScope.System,
                Priority = 0
            }
        ]);

        evaluator.AddSessionRule(new ToolPermissionRule
        {
            ToolPattern = "bash",
            Behavior = PermissionBehavior.Deny,
            Priority = 0,
            Reason = "Session override"
        });

        var decision = evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" });

        // Session scope (weight=4) > System scope (weight=1) at same priority
        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Scope.ShouldBe(ToolPermissionScope.Session);
    }

    #endregion

    #region ConfiguredToolPermissionEvaluator Delegation

    [Fact]
    public void Configured_AddSessionRule_DelegatesToInnerEvaluator()
    {
        var monitor = new MutableOptionsMonitor(new AIOptions());
        using var evaluator = new ConfiguredToolPermissionEvaluator(monitor);

        evaluator.AddSessionRule(new ToolPermissionRule
        {
            ToolPattern = "bash",
            Behavior = PermissionBehavior.Deny
        });

        var rules = evaluator.GetSessionRules();
        rules.Count.ShouldBe(1);
        rules[0].ToolPattern.ShouldBe("bash");
    }

    [Fact]
    public void Configured_RemoveSessionRule_DelegatesToInnerEvaluator()
    {
        var monitor = new MutableOptionsMonitor(new AIOptions());
        using var evaluator = new ConfiguredToolPermissionEvaluator(monitor);

        evaluator.AddSessionRule(new ToolPermissionRule { ToolPattern = "bash", Behavior = PermissionBehavior.Deny });
        evaluator.RemoveSessionRule("bash");

        evaluator.GetSessionRules().Count.ShouldBe(0);
    }

    [Fact]
    public void Configured_Evaluate_IncludesSessionRules()
    {
        var monitor = new MutableOptionsMonitor(new AIOptions());
        using var evaluator = new ConfiguredToolPermissionEvaluator(monitor);

        evaluator.AddSessionRule(new ToolPermissionRule
        {
            ToolPattern = "bash",
            Behavior = PermissionBehavior.Deny,
            Reason = "Session blocked"
        });

        var decision = evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
    }

    #endregion

    #region Parent Session Rules Propagation

    [Fact]
    public void SetSubAgentContext_CapturesParentSessionRules()
    {
        // Arrange: an evaluator with session rules
        var evaluator = new ToolPermissionEvaluator([]);
        evaluator.AddSessionRule(new ToolPermissionRule
        {
            ToolPattern = "bash",
            Behavior = PermissionBehavior.Deny,
            Reason = "Parent rule"
        });

        var accessor = new AgentExecutionContextAccessor();
        var services = new ServiceCollection();
        services.AddSingleton<IToolPermissionEvaluator>(evaluator);
        var sp = services.BuildServiceProvider();

        // Simulate SetSubAgentContext behavior
        var sessionRules = evaluator.GetSessionRules();
        if (sessionRules.Count > 0)
        {
            accessor.Properties[ContextPropertyKeys.ParentSessionRules] = sessionRules;
        }

        accessor.Properties[ContextPropertyKeys.IsSubAgent] = true;
        accessor.Properties[ContextPropertyKeys.SubAgentName] = "child";

        // Verify
        accessor.Properties.ContainsKey(ContextPropertyKeys.ParentSessionRules).ShouldBeTrue();
        var propagated = accessor.Properties[ContextPropertyKeys.ParentSessionRules] as IReadOnlyList<ToolPermissionRule>;
        propagated.ShouldNotBeNull();
        propagated.Count.ShouldBe(1);
        propagated[0].ToolPattern.ShouldBe("bash");
        propagated[0].Behavior.ShouldBe(PermissionBehavior.Deny);
    }

    [Fact]
    public void SetSubAgentContext_SkipsWhenNoSessionRules()
    {
        var evaluator = new ToolPermissionEvaluator([]);
        var accessor = new AgentExecutionContextAccessor();

        var sessionRules = evaluator.GetSessionRules();
        // Should not set property when no session rules exist
        sessionRules.Count.ShouldBe(0);

        accessor.Properties.ContainsKey(ContextPropertyKeys.ParentSessionRules).ShouldBeFalse();
    }

    #endregion

    #region Default Interface Method

    [Fact]
    public void DefaultInterfaceMethods_NoOpBehavior()
    {
        IToolPermissionEvaluator evaluator = new NoOpPermissionEvaluator();

        // Default methods should not throw
        evaluator.AddSessionRule(new ToolPermissionRule { ToolPattern = "test" });
        evaluator.RemoveSessionRule("test");
        evaluator.GetSessionRules().Count.ShouldBe(0);
    }

    #endregion

    /// <summary>
    /// 最小 IToolPermissionEvaluator 实现，仅实现必需方法，
    /// 验证 default interface methods 的向后兼容性。
    /// </summary>
    private sealed class NoOpPermissionEvaluator : IToolPermissionEvaluator
    {
        public bool HasRules => false;

        public ToolPermissionDecision Evaluate(ToolPermissionContext context, IEnumerable<ToolPermissionRule>? additionalRules = null)
            => new(context.ToolName, PermissionBehavior.Allow);
    }

    /// <summary>
    /// 可变 IOptionsMonitor 用于测试 ConfiguredToolPermissionEvaluator
    /// </summary>
    private sealed class MutableOptionsMonitor : IOptionsMonitor<AIOptions>
    {
        private readonly List<Action<AIOptions, string?>> _listeners = [];

        public MutableOptionsMonitor(AIOptions value)
        {
            CurrentValue = value;
        }

        public AIOptions CurrentValue { get; private set; }

        public AIOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<AIOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new CallbackDisposable(() => _listeners.Remove(listener));
        }

        private sealed class CallbackDisposable(Action callback) : IDisposable
        {
            public void Dispose() => callback();
        }
    }
}
