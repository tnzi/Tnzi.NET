using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// Per-skill AllowedTools semantics on <see cref="SkillConstraintMiddleware"/>: a whitelist only ever
/// NARROWS the tools the agent already has. It never pulls tools out of the global registry into the
/// agent - that path bypassed ToolDefinition.RequiredPermissions and approval wrapping, and any user
/// able to author a User-scope skill could have handed an agent admin-only tools by naming them.
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

    private static AITool MakeAiTool(string name)
    {
        var mock = new Mock<AITool>();
        mock.Setup(t => t.Name).Returns(name);
        return mock.Object;
    }

    private static IAgentExecutor MakeAgent(params string[] toolNames)
    {
        var mock = new Mock<IAgentExecutor>();
        mock.Setup(a => a.Name).Returns("agent");
        mock.Setup(a => a.Tools).Returns(toolNames.Select(MakeAiTool).ToList());
        return mock.Object;
    }

    private static AiMiddlewareContext CreateContext(
        List<AITool>? existingTools = null,
        IAgentExecutor? agent = null,
        IServiceProvider? serviceProvider = null)
    {
        var sp = serviceProvider ?? CreateServiceProvider();
        var context = new AiMiddlewareContext
        {
            Request = new AgentRunRequest { UserMessage = "test" },
            Agent = new AgentResolution { ExecutionMode = AgentExecutionMode.Single, Agent = agent },
            ServiceProvider = sp
        };

        if (existingTools != null)
            context.AdditionalTools.AddRange(existingTools);

        return context;
    }

    private static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        // The provider IS resolvable: if the middleware still injected by name it would succeed here.
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
        IEnumerable<SkillDefinition> activeSkills,
        ISkillConstraintEnforcer? enforcer = null)
    {
        var tracker = new SkillActivationTracker();
        foreach (var skill in activeSkills)
            tracker.Activate(skill);

        return new SkillConstraintMiddleware(
            enforcer ?? new SkillConstraintEnforcer(),
            toolRegistry.Object,
            tracker,
            Mock.Of<ILogger<SkillConstraintMiddleware>>());
    }

    private static Task<AgentRunResult> Next(AiMiddlewareContext ctx, CancellationToken ct)
        => Task.FromResult(new AgentRunResult { Response = "done" });

    #endregion

    [Fact]
    public async Task AllowedTools_DoesNotInjectToolsOutsideAgentToolSet()
    {
        // Arrange: the skill names a registry tool the agent was never configured with.
        // The registry knows it and its provider resolves from DI, so the old injection path would add it.
        var skill = new SkillDefinition { Slug = "my-skill", Name = "My Skill", Priority = 1, AllowedTools = ["execute_command"] };
        var registry = CreateToolRegistry(MakeToolDef("execute_command", "shell"));
        var middleware = CreateMiddleware(registry, [skill]);
        var context = CreateContext(agent: MakeAgent("read_file"));

        // Act
        await middleware.InvokeAsync(context, Next);

        // Assert: nothing was pulled in from the registry
        context.AdditionalTools.ShouldBeEmpty();
        context.ExcludedToolNames.ShouldNotContain("execute_command");
    }

    [Fact]
    public async Task AllowedTools_KeepsWhitelistedTool_RemovesOtherGroupedTools()
    {
        var skill = new SkillDefinition { Slug = "s", Name = "S", AllowedTools = ["test-tool"] };
        var registry = CreateToolRegistry(MakeToolDef("test-tool", "shell"), MakeToolDef("other-tool", "shell"));
        var middleware = CreateMiddleware(registry, [skill]);

        var context = CreateContext(
            existingTools: [MakeAiTool("test-tool"), MakeAiTool("other-tool")],
            agent: MakeAgent("test-tool", "other-tool"));

        await middleware.InvokeAsync(context, Next);

        context.AdditionalTools.Select(t => t.Name).ShouldBe(["test-tool"]);
        context.ExcludedToolNames.ShouldBe(["other-tool"]);
    }

    [Fact]
    public async Task AllowedTools_UngroupedToolsAreNotSubjectToTheWhitelist()
    {
        // MCP / OpenAPI / dynamic tools are not in the registry: a whitelist cannot speak about them.
        var skill = new SkillDefinition { Slug = "s", Name = "S", AllowedTools = ["test-tool"] };
        var registry = CreateToolRegistry(MakeToolDef("test-tool", "shell"));
        var middleware = CreateMiddleware(registry, [skill]);

        var context = CreateContext(existingTools: [MakeAiTool("mcp_dynamic")], agent: MakeAgent("mcp_dynamic"));

        await middleware.InvokeAsync(context, Next);

        context.AdditionalTools.Select(t => t.Name).ShouldBe(["mcp_dynamic"]);
        context.ExcludedToolNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task SkillWithNoAllowedTools_DoesNotRestrictAnything()
    {
        var skill = new SkillDefinition { Slug = "s", Name = "S", AllowedTools = null };
        var registry = CreateToolRegistry(MakeToolDef("test-tool", "shell"));
        var middleware = CreateMiddleware(registry, [skill]);

        var context = CreateContext(existingTools: [MakeAiTool("test-tool")], agent: MakeAgent("test-tool"));

        await middleware.InvokeAsync(context, Next);

        context.AdditionalTools.Count.ShouldBe(1);
        context.ExcludedToolNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task MultipleSkillsWithDisjointAllowedTools_IntersectionIsEmpty_RemovesEveryGroupedTool()
    {
        // AllowedTools semantics are INTERSECTION (most-restrictive-wins): two disjoint whitelists
        // permit no common tool, so every grouped tool the agent has is withheld.
        var skill1 = new SkillDefinition { Slug = "skill-1", Name = "Skill 1", Priority = 2, AllowedTools = ["tool-a"] };
        var skill2 = new SkillDefinition { Slug = "skill-2", Name = "Skill 2", Priority = 1, AllowedTools = ["tool-b"] };
        var registry = CreateToolRegistry(MakeToolDef("tool-a", "group1"), MakeToolDef("tool-b", "group2"));
        var middleware = CreateMiddleware(registry, [skill1, skill2]);

        var context = CreateContext(agent: MakeAgent("tool-a", "tool-b"));

        await middleware.InvokeAsync(context, Next);

        context.ExcludedToolNames.ShouldBe(["tool-a", "tool-b"], ignoreOrder: true);
        context.AdditionalTools.ShouldBeEmpty();
    }

    [Fact]
    public async Task MultipleSkillsWithOverlappingAllowedTools_KeepsOnlyTheCommonTool()
    {
        var skill1 = new SkillDefinition { Slug = "skill-1", Name = "Skill 1", Priority = 2, AllowedTools = ["tool-a", "tool-b"] };
        var skill2 = new SkillDefinition { Slug = "skill-2", Name = "Skill 2", Priority = 1, AllowedTools = ["tool-a"] };
        var registry = CreateToolRegistry(MakeToolDef("tool-a", "group1"), MakeToolDef("tool-b", "group2"));
        var middleware = CreateMiddleware(registry, [skill1, skill2]);

        var context = CreateContext(agent: MakeAgent("tool-a", "tool-b"));

        await middleware.InvokeAsync(context, Next);

        context.ExcludedToolNames.ShouldBe(["tool-b"]);
    }

    [Fact]
    public async Task OneSkillWhitelistsOneSkillDoesNot_DoesNotCollapseIntersection()
    {
        // A skill with no whitelist imposes no individual-tool restriction and must not
        // collapse the intersection to empty.
        var whitelisting = new SkillDefinition { Slug = "skill-1", Name = "Skill 1", Priority = 2, AllowedTools = ["tool-a"] };
        var neutral = new SkillDefinition { Slug = "skill-2", Name = "Skill 2", Priority = 1, AllowedTools = null };
        var registry = CreateToolRegistry(MakeToolDef("tool-a", "group1"), MakeToolDef("tool-b", "group2"));
        var middleware = CreateMiddleware(registry, [whitelisting, neutral]);

        var context = CreateContext(agent: MakeAgent("tool-a", "tool-b"));

        await middleware.InvokeAsync(context, Next);

        context.ExcludedToolNames.ShouldBe(["tool-b"]);
    }

    [Fact]
    public async Task AllowedToolGroups_PlusAllowedTools_KeepsGroupMembersAndTheSupplementaryTool()
    {
        // Documented example: allowed-tool-groups: Git + allowed-tools: custom_lint → all git tools
        // plus custom_lint survive; everything else grouped is withheld.
        var skill = new SkillDefinition
        {
            Slug = "audit", Name = "Audit",
            AllowedToolGroups = ["git"],
            AllowedTools = ["custom_lint"],
            DeniedTools = ["git_push"]
        };
        var registry = CreateToolRegistry(
            MakeToolDef("git_diff", "git"), MakeToolDef("git_push", "git"),
            MakeToolDef("custom_lint", "lint"), MakeToolDef("bash", "shell"));
        var middleware = CreateMiddleware(registry, [skill]);

        var context = CreateContext(agent: MakeAgent("git_diff", "git_push", "custom_lint", "bash"));

        await middleware.InvokeAsync(context, Next);

        context.ExcludedToolNames.ShouldBe(["git_push", "bash"], ignoreOrder: true);
    }
}
