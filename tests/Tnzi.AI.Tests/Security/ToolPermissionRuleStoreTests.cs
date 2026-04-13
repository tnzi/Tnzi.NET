namespace Tnzi.AI.Tests.Security;

public class ToolPermissionRuleStoreTests
{
    [Fact]
    public async Task GetRulesAsync_ReturnsEnabledRulesWithCorrectEnumMapping()
    {
        // Arrange
        var entities = new List<ToolPermissionRuleEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = "bash",
                ToolGroup = "shell",
                Behavior = (int)PermissionBehavior.Deny,
                Scope = (int)ToolPermissionScope.System,
                Priority = 100,
                IsDestructiveOnly = true,
                IsSubAgentOnly = false,
                Reason = "Deny bash for safety",
                IsEnabled = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = "file_read",
                Behavior = (int)PermissionBehavior.Allow,
                Scope = (int)ToolPermissionScope.User,
                Priority = 50,
                IsEnabled = true
            }
        };

        var repository = CreateMockRepository(entities);
        var store = new DatabaseToolPermissionRuleStore(repository.Object);

        // Act
        var rules = await store.GetRulesAsync();

        // Assert
        rules.Count.ShouldBe(2);

        var bashRule = rules.First(r => r.ToolPattern == "bash");
        bashRule.ToolGroup.ShouldBe("shell");
        bashRule.Behavior.ShouldBe(PermissionBehavior.Deny);
        bashRule.Scope.ShouldBe(ToolPermissionScope.System);
        bashRule.Priority.ShouldBe(100);
        bashRule.IsDestructiveOnly.ShouldBeTrue();
        bashRule.IsSubAgentOnly.ShouldBeFalse();
        bashRule.Reason.ShouldBe("Deny bash for safety");

        var fileRule = rules.First(r => r.ToolPattern == "file_read");
        fileRule.Behavior.ShouldBe(PermissionBehavior.Allow);
        fileRule.Scope.ShouldBe(ToolPermissionScope.User);
    }

    [Fact]
    public async Task GetRulesAsync_SkipsDisabledRules()
    {
        // Arrange
        var entities = new List<ToolPermissionRuleEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = "enabled_tool",
                Behavior = (int)PermissionBehavior.Allow,
                Scope = (int)ToolPermissionScope.System,
                IsEnabled = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = "disabled_tool",
                Behavior = (int)PermissionBehavior.Deny,
                Scope = (int)ToolPermissionScope.System,
                IsEnabled = false
            }
        };

        var repository = CreateMockRepository(entities);
        var store = new DatabaseToolPermissionRuleStore(repository.Object);

        // Act
        var rules = await store.GetRulesAsync();

        // Assert
        rules.Count.ShouldBe(1);
        rules[0].ToolPattern.ShouldBe("enabled_tool");
    }

    [Fact]
    public async Task GetRulesByScopeAsync_FiltersByScope()
    {
        // Arrange
        var entities = new List<ToolPermissionRuleEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = "system_rule",
                Behavior = (int)PermissionBehavior.Deny,
                Scope = (int)ToolPermissionScope.System,
                IsEnabled = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = "user_rule",
                Behavior = (int)PermissionBehavior.Allow,
                Scope = (int)ToolPermissionScope.User,
                IsEnabled = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = "another_system_rule",
                Behavior = (int)PermissionBehavior.Ask,
                Scope = (int)ToolPermissionScope.System,
                IsEnabled = true
            }
        };

        var repository = CreateMockRepository(entities);
        var store = new DatabaseToolPermissionRuleStore(repository.Object);

        // Act
        var systemRules = await store.GetRulesByScopeAsync(ToolPermissionScope.System);

        // Assert
        systemRules.Count.ShouldBe(2);
        systemRules.ShouldAllBe(r => r.Scope == ToolPermissionScope.System);
    }

    [Fact]
    public async Task GetRulesAsync_ReturnsEmptyWhenNoEnabledRules()
    {
        // Arrange
        var entities = new List<ToolPermissionRuleEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = "disabled",
                Behavior = (int)PermissionBehavior.Deny,
                Scope = (int)ToolPermissionScope.System,
                IsEnabled = false
            }
        };

        var repository = CreateMockRepository(entities);
        var store = new DatabaseToolPermissionRuleStore(repository.Object);

        // Act
        var rules = await store.GetRulesAsync();

        // Assert
        rules.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetRulesAsync_NullToolPatternDefaultsToWildcard()
    {
        // Arrange
        var entities = new List<ToolPermissionRuleEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = null,
                Behavior = (int)PermissionBehavior.Ask,
                Scope = (int)ToolPermissionScope.System,
                IsEnabled = true
            }
        };

        var repository = CreateMockRepository(entities);
        var store = new DatabaseToolPermissionRuleStore(repository.Object);

        // Act
        var rules = await store.GetRulesAsync();

        // Assert
        rules.Count.ShouldBe(1);
        rules[0].ToolPattern.ShouldBe("*");
    }

    [Fact]
    public void ConfiguredEvaluator_MergesDbRulesIntoEvaluation()
    {
        // Arrange: 配置文件无规则
        var monitor = new TestOptionsMonitor(new AIOptions
        {
            Permissions = new ToolPermissionOptions { Enabled = false }
        });

        // 创建 evaluator 并手动注入 DB 规则
        var evaluator = new ConfiguredToolPermissionEvaluator(monitor);

        // 没有 DB 规则时，应该允许
        var decision1 = evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" });
        decision1.Behavior.ShouldBe(PermissionBehavior.Allow);

        // 通过 additionalRules 模拟 DB 规则效果
        var dbRules = new List<ToolPermissionRule>
        {
            new()
            {
                ToolPattern = "bash",
                Behavior = PermissionBehavior.Deny,
                Scope = ToolPermissionScope.System,
                Priority = 100,
                Reason = "DB rule: bash denied"
            }
        };

        var decision2 = evaluator.Evaluate(
            new ToolPermissionContext { ToolName = "bash" },
            dbRules);

        decision2.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision2.Reason.ShouldBe("DB rule: bash denied");

        evaluator.Dispose();
    }

    [Fact]
    public void HasRules_ReflectsDbRulesPresence()
    {
        // Arrange: 配置文件无规则
        var monitor = new TestOptionsMonitor(new AIOptions
        {
            Permissions = new ToolPermissionOptions { Enabled = false }
        });

        var evaluator = new ConfiguredToolPermissionEvaluator(monitor);

        // 初始无规则
        evaluator.HasRules.ShouldBeFalse();

        evaluator.Dispose();
    }

    private static Mock<IRepository<ToolPermissionRuleEntity, Guid>> CreateMockRepository(
        List<ToolPermissionRuleEntity> entities)
    {
        var mock = new Mock<IRepository<ToolPermissionRuleEntity, Guid>>();
        mock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(entities.BuildMock());
        return mock;
    }
}

file sealed class TestOptionsMonitor : IOptionsMonitor<AIOptions>, IDisposable
{
    public TestOptionsMonitor(AIOptions value)
    {
        CurrentValue = value;
    }

    public AIOptions CurrentValue { get; }

    public AIOptions Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<AIOptions, string?> listener) => this;

    public void Dispose() { }
}
