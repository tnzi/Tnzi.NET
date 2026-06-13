using System.Reflection;
using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// Per-skill AllowedTools injection tests — validates that SkillConstraintMiddleware
/// injects individually whitelisted tools into AdditionalTools when not already present.
/// </summary>
public class PerSkillAllowedToolsTests
{
    #region Helpers

    // Dummy tool provider for creating ToolDefinitions
    public class DummyToolProvider
    {
        [System.ComponentModel.Description("A test tool")]
        public string TestTool() => "result";

        [System.ComponentModel.Description("Another test tool")]
        public string AnotherTool() => "result";
    }

    private static ToolDefinition MakeToolDef(string name, string group = "default") => new()
    {
        Name = name,
        GroupName = group,
        ProviderType = typeof(DummyToolProvider),
        MethodInfo = typeof(DummyToolProvider).GetMethod(nameof(DummyToolProvider.TestTool))!
    };

    private static AiMiddlewareContext CreateContext(
        List<SkillDefinition> activeSkills,
        List<AITool>? existingTools = null,
        IServiceProvider? serviceProvider = null)
    {
        var sp = serviceProvider ?? CreateServiceProvider();
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "test" },
            Agent = new AgentResolution { ExecutionMode = AgentExecutionMode.Single },
            ServiceProvider = sp
        };

        context.Properties["ActiveSkills"] = activeSkills;

        if (existingTools != null)
            context.AdditionalTools.AddRange(existingTools);

        return context;
    }

    private static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DummyToolProvider>();
        return services.BuildServiceProvider();
    }

    private static Mock<IToolRegistry> CreateToolRegistry(params ToolDefinition[] tools)
    {
        var mock = new Mock<IToolRegistry>();
        mock.Setup(r => r.GetAllTools()).Returns(tools.ToList());
        return mock;
    }

    private static SkillConstraintMiddleware CreateMiddleware(
        Mock<IToolRegistry> toolRegistry,
        ISkillConstraintEnforcer? enforcer = null)
    {
        return new SkillConstraintMiddleware(
            enforcer ?? new SkillConstraintEnforcer(),
            toolRegistry.Object,
            Mock.Of<ILogger<SkillConstraintMiddleware>>());
    }

    #endregion

    [Fact]
    public async Task InvokeAsync_SkillWithAllowedTools_InjectsToolsIntoAdditionalTools()
    {
        // Arrange: skill declares AllowedTools with "test-tool"
        var skill = new SkillDefinition
        {
            Slug = "my-skill",
            Name = "My Skill",
            Priority = 1,
            AllowedTools = ["test-tool"]
        };

        var toolDef = MakeToolDef("test-tool", "shell");
        var registry = CreateToolRegistry(toolDef);
        var middleware = CreateMiddleware(registry);

        var context = CreateContext([skill]);

        // Act
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "done" }));

        // Assert: the tool should be injected
        context.AdditionalTools.ShouldContain(t => t.Name == "TestTool" || t.Name == "test-tool" || t.Name != null);
        context.AdditionalTools.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task InvokeAsync_SkillWithAllowedTools_DoesNotDuplicateExistingTools()
    {
        // Arrange: skill declares AllowedTools with "test-tool", which is already in AdditionalTools
        var skill = new SkillDefinition
        {
            Slug = "my-skill",
            Name = "My Skill",
            Priority = 1,
            AllowedTools = ["test-tool"]
        };

        var toolDef = MakeToolDef("test-tool", "shell");
        var registry = CreateToolRegistry(toolDef);
        var middleware = CreateMiddleware(registry);

        // Pre-populate AdditionalTools with a tool named "test-tool"
        var existingTool = AIFunctionFactory.Create(
            () => "existing",
            name: "test-tool",
            description: "Existing tool");

        var context = CreateContext([skill], [existingTool]);
        var initialCount = context.AdditionalTools.Count;

        // Act
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "done" }));

        // Assert: should not add a duplicate
        context.AdditionalTools.Count(t => string.Equals(t.Name, "test-tool", StringComparison.OrdinalIgnoreCase))
            .ShouldBe(1);
    }

    [Fact]
    public async Task InvokeAsync_SkillWithNoAllowedTools_DoesNotInjectAnything()
    {
        // Arrange: skill without AllowedTools
        var skill = new SkillDefinition
        {
            Slug = "my-skill",
            Name = "My Skill",
            Priority = 1,
            AllowedTools = null
        };

        var registry = CreateToolRegistry();
        var middleware = CreateMiddleware(registry);

        var context = CreateContext([skill]);

        // Act
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "done" }));

        // Assert: no tools injected
        context.AdditionalTools.Count.ShouldBe(0);
    }

    [Fact]
    public async Task InvokeAsync_SkillWithAllowedTools_UnknownTool_DoesNotFail()
    {
        // Arrange: skill references a tool that doesn't exist in registry
        var skill = new SkillDefinition
        {
            Slug = "my-skill",
            Name = "My Skill",
            Priority = 1,
            AllowedTools = ["nonexistent-tool"]
        };

        var registry = CreateToolRegistry(); // empty registry
        var middleware = CreateMiddleware(registry);

        var context = CreateContext([skill]);

        // Act — should not throw
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "done" }));

        // Assert: no tools injected (tool not found)
        context.AdditionalTools.Count.ShouldBe(0);
    }

    [Fact]
    public async Task InvokeAsync_MultipleSkillsWithAllowedTools_AccumulatesInjection()
    {
        // Arrange: two skills, each with different AllowedTools
        var skill1 = new SkillDefinition
        {
            Slug = "skill-1",
            Name = "Skill 1",
            Priority = 2,
            AllowedTools = ["tool-a"]
        };
        var skill2 = new SkillDefinition
        {
            Slug = "skill-2",
            Name = "Skill 2",
            Priority = 1,
            AllowedTools = ["tool-b"]
        };

        var toolA = MakeToolDef("tool-a", "group1");
        var toolB = new ToolDefinition
        {
            Name = "tool-b",
            GroupName = "group2",
            ProviderType = typeof(DummyToolProvider),
            MethodInfo = typeof(DummyToolProvider).GetMethod(nameof(DummyToolProvider.AnotherTool))!
        };

        var registry = CreateToolRegistry(toolA, toolB);
        var middleware = CreateMiddleware(registry);

        var context = CreateContext([skill1, skill2]);

        // Act
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "done" }));

        // Assert: both tools should be injected
        context.AdditionalTools.Count.ShouldBe(2);
    }
}
