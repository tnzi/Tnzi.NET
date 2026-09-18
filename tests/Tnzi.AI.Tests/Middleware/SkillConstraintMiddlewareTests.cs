using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// SkillConstraintMiddleware 单元测试
/// </summary>
public class SkillConstraintMiddlewareTests
{
    #region Helpers

    /// <summary>
    /// The activation tracker is the single source of truth the middleware reads; tests seed it
    /// directly the way skill_activate would (Properties["ActiveSkills"] is no longer consulted).
    /// </summary>
    private static (SkillConstraintMiddleware middleware, SkillActivationTracker tracker) CreateMiddleware(
        ISkillConstraintEnforcer? enforcer = null,
        IToolRegistry? toolRegistry = null,
        params SkillDefinition[] activeSkills)
    {
        enforcer ??= new SkillConstraintEnforcer();

        if (toolRegistry == null)
        {
            var mockRegistry = new Mock<IToolRegistry>();
            mockRegistry.Setup(r => r.GetAllTools()).Returns([]);
            toolRegistry = mockRegistry.Object;
        }

        var tracker = new SkillActivationTracker();
        foreach (var skill in activeSkills)
            tracker.Activate(skill);

        return (new(enforcer, toolRegistry, tracker, Mock.Of<ILogger<SkillConstraintMiddleware>>()), tracker);
    }

    private static AiMiddlewareContext CreateContext(
        string? model = null,
        string? provider = null,
        List<AITool>? additionalTools = null,
        IAgentExecutor? agent = null)
    {
        return new AiMiddlewareContext
        {
            Request = new AgentRunRequest
            {
                UserMessage = "Hello",
                Model = model,
                Provider = provider
            },
            Agent = AgentResolution.Success(
                agent: agent!,
                provider: provider ?? "OpenAI",
                model: model ?? "gpt-4o",
                agentId: null),
            ServiceProvider = new Mock<IServiceProvider>().Object,
            AdditionalTools = additionalTools ?? []
        };
    }

    /// <summary>An agent executor that only exposes a tool list (what the middleware inspects).</summary>
    private static IAgentExecutor CreateAgentWithTools(params string[] toolNames)
    {
        var mock = new Mock<IAgentExecutor>();
        mock.Setup(a => a.Name).Returns("agent");
        mock.Setup(a => a.Tools).Returns(toolNames.Select(CreateAiTool).ToList());
        return mock.Object;
    }

    private static AITool CreateAiTool(string name)
    {
        // AITool is created by registering with AIFunctionFactory or similar;
        // for tests we need a mock-like object. We'll use a minimal approach.
        var mockTool = new Mock<AITool>();
        mockTool.Setup(t => t.Name).Returns(name);
        return mockTool.Object;
    }

    private static IToolRegistry CreateToolRegistry(params (string name, string group)[] tools)
    {
        var defs = tools.Select(t => new ToolDefinition { Name = t.name, GroupName = t.group }).ToList();
        var mockRegistry = new Mock<IToolRegistry>();
        mockRegistry.Setup(r => r.GetAllTools()).Returns(defs);
        return mockRegistry.Object;
    }

    private static SkillDefinition CreateSkill(
        List<string>? allowedToolGroups = null,
        string? requiredModel = null,
        string? requiredProvider = null,
        int priority = 0)
    {
        return new SkillDefinition
        {
            Slug = "test-skill",
            Name = "Test Skill",
            Content = "Test skill content",
            AllowedToolGroups = allowedToolGroups,
            RequiredModel = requiredModel,
            RequiredProvider = requiredProvider,
            Priority = priority
        };
    }

    private static Task<AgentRunResult> NextDelegate(AiMiddlewareContext ctx, CancellationToken ct)
        => Task.FromResult(new AgentRunResult { Response = "ok" });

    #endregion

    #region Test 1: NoActiveSkills → pass through

    [Fact]
    public async Task InvokeAsync_NoActiveSkills_PassesThrough()
    {
        var (middleware, _) = CreateMiddleware();
        var context = CreateContext();
        // Nothing activated in the tracker
        var nextCalled = false;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        nextCalled.ShouldBeTrue();
        context.EffectiveModel.ShouldBeNull();
        context.EffectiveProvider.ShouldBeNull();
    }

    [Fact]
    public async Task InvokeAsync_EmptyActiveSkillsList_PassesThrough()
    {
        var (middleware, tracker) = CreateMiddleware();
        var context = CreateContext();
        tracker.ActivatedSkills.ShouldBeEmpty();
        var nextCalled = false;

        await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        nextCalled.ShouldBeTrue();
        context.EffectiveModel.ShouldBeNull();
    }

    #endregion

    #region Test 2: AllowedToolGroups filtering

    [Fact]
    public async Task InvokeAsync_WithAllowedToolGroups_FiltersTools()
    {
        var toolRegistry = CreateToolRegistry(
            ("git_diff", "git"),
            ("git_commit", "git"),
            ("bash", "shell"));

        var skill = CreateSkill(allowedToolGroups: ["git"]);
        var (middleware, _) = CreateMiddleware(toolRegistry: toolRegistry, activeSkills: skill);

        var gitTool = CreateAiTool("git_diff");
        var gitCommitTool = CreateAiTool("git_commit");
        var bashTool = CreateAiTool("bash");

        var context = CreateContext(
            additionalTools: [gitTool, gitCommitTool, bashTool]);

        await middleware.InvokeAsync(context, NextDelegate);

        // Only git tools should remain; bash (group "shell") should be removed
        context.AdditionalTools.ShouldContain(t => t.Name == "git_diff");
        context.AdditionalTools.ShouldContain(t => t.Name == "git_commit");
        context.AdditionalTools.ShouldNotContain(t => t.Name == "bash");
    }

    #endregion

    #region Test 3: RequiredModel

    [Fact]
    public async Task InvokeAsync_WithRequiredModel_SetsEffectiveModel()
    {
        var skill = CreateSkill(requiredModel: "claude-opus");
        var (middleware, _) = CreateMiddleware(activeSkills: skill);

        var context = CreateContext(model: "gpt-4o");

        await middleware.InvokeAsync(context, NextDelegate);

        context.EffectiveModel.ShouldBe("claude-opus");
    }

    #endregion

    #region Test 4: RequiredProvider

    [Fact]
    public async Task InvokeAsync_WithRequiredProvider_SetsEffectiveProvider()
    {
        var skill = CreateSkill(requiredProvider: "Anthropic");
        var (middleware, _) = CreateMiddleware(activeSkills: skill);

        var context = CreateContext(provider: "OpenAI");

        await middleware.InvokeAsync(context, NextDelegate);

        context.EffectiveProvider.ShouldBe("Anthropic");
    }

    #endregion

    #region Test 5: Multiple skills - intersection of allowed groups

    [Fact]
    public async Task InvokeAsync_MultipleSkills_TakesIntersection()
    {
        var toolRegistry = CreateToolRegistry(
            ("git_diff", "git"),
            ("bash", "shell"),
            ("web_search", "web"));

        // Skill A: allows git + shell
        var skillA = new SkillDefinition
        {
            Slug = "skill-a",
            Name = "Skill A",
            Content = "A",
            AllowedToolGroups = ["git", "shell"],
            Priority = 0
        };

        // Skill B: allows git + web
        var skillB = new SkillDefinition
        {
            Slug = "skill-b",
            Name = "Skill B",
            Content = "B",
            AllowedToolGroups = ["git", "web"],
            Priority = 1
        };

        var (middleware, _) = CreateMiddleware(toolRegistry: toolRegistry, activeSkills: [skillA, skillB]);

        var gitTool = CreateAiTool("git_diff");
        var bashTool = CreateAiTool("bash");
        var webTool = CreateAiTool("web_search");

        var context = CreateContext(
            additionalTools: [gitTool, bashTool, webTool]);

        await middleware.InvokeAsync(context, NextDelegate);

        // Intersection: git only → git_diff survives, bash (shell) and web_search (web) removed
        context.AdditionalTools.ShouldContain(t => t.Name == "git_diff");
        context.AdditionalTools.ShouldNotContain(t => t.Name == "bash");
        context.AdditionalTools.ShouldNotContain(t => t.Name == "web_search");
    }

    #endregion

    #region Test 6: Ungrouped tools pass through

    [Fact]
    public async Task InvokeAsync_UngroupedTools_PassThrough()
    {
        // Registry only knows about "git_diff"
        var toolRegistry = CreateToolRegistry(("git_diff", "git"));
        // Only shell allowed → git_diff filtered out (it's in registry as group "git" which is NOT "shell")
        // but mcp_tool is NOT in registry → should pass through
        var skill = CreateSkill(allowedToolGroups: ["shell"]);
        var (middleware, _) = CreateMiddleware(toolRegistry: toolRegistry, activeSkills: skill);

        var gitTool = CreateAiTool("git_diff");
        // "mcp_tool" is not in the registry at all (OpenAPI/MCP dynamic tool)
        var mcpTool = CreateAiTool("mcp_tool");

        var context = CreateContext(
            additionalTools: [gitTool, mcpTool]);

        await middleware.InvokeAsync(context, NextDelegate);

        // git_diff is in registry (group "git"), not in allowed → removed
        context.AdditionalTools.ShouldNotContain(t => t.Name == "git_diff");
        // mcp_tool is NOT in registry → passes through
        context.AdditionalTools.ShouldContain(t => t.Name == "mcp_tool");
    }

    #endregion

    #region Test 7: Deny wins over allow (B2)

    [Fact]
    public async Task InvokeAsync_ToolAllowedByOneSkillDeniedByAnother_DenyWins()
    {
        // "tool-x" is in the registry (so it can be injected by the allow-list path)
        var toolRegistry = CreateToolRegistry(("tool-x", "group-a"));

        // Skill A explicitly allows "tool-x"
        var skillA = new SkillDefinition
        {
            Slug = "skill-allow",
            Name = "Skill Allow",
            Content = "A",
            AllowedTools = ["tool-x"],
            Priority = 1
        };

        // Skill B explicitly denies "tool-x"
        var skillB = new SkillDefinition
        {
            Slug = "skill-deny",
            Name = "Skill Deny",
            Content = "B",
            DeniedTools = ["tool-x"],
            Priority = 0
        };

        var (middleware, _) = CreateMiddleware(toolRegistry: toolRegistry, activeSkills: [skillA, skillB]);

        // The agent already has tool-x (from its tool groups) and it is also injected.
        var context = CreateContext(additionalTools: [CreateAiTool("tool-x")], agent: CreateAgentWithTools("tool-x"));

        await middleware.InvokeAsync(context, NextDelegate);

        // Deny wins: tool-x must be withheld from both lists
        context.AdditionalTools.ShouldNotContain(t => t.Name != null &&
            string.Equals(t.Name, "tool-x", StringComparison.OrdinalIgnoreCase));
        context.ExcludedToolNames.ShouldContain("tool-x");
    }

    #endregion

    #region Agent's own tools (not only AdditionalTools) are withheld from the model

    [Fact]
    public async Task InvokeAsync_DeniedTool_OnAgentItself_IsAddedToExcludedToolNames()
    {
        var toolRegistry = CreateToolRegistry(("bash", "shell"), ("read_file", "fs"));
        var skill = new SkillDefinition { Slug = "ro", Name = "Read Only", Content = "x", DeniedTools = ["bash"] };
        var (middleware, _) = CreateMiddleware(toolRegistry: toolRegistry, activeSkills: skill);

        // bash and read_file come from the agent's configured tool groups, not from a context provider.
        var context = CreateContext(agent: CreateAgentWithTools("bash", "read_file"));

        await middleware.InvokeAsync(context, NextDelegate);

        context.ExcludedToolNames.ShouldBe(["bash"], ignoreOrder: true);
    }

    [Fact]
    public async Task InvokeAsync_AllowedToolGroups_OnAgentItself_ExcludesOtherGroups_KeepsWhitelistedAndUngrouped()
    {
        var toolRegistry = CreateToolRegistry(("git_diff", "git"), ("bash", "shell"), ("custom_lint", "lint"));
        var skill = new SkillDefinition
        {
            Slug = "audit", Name = "Audit", Content = "x",
            AllowedToolGroups = ["git"],
            AllowedTools = ["custom_lint"] // supplementary whitelist: survives the group filter
        };
        var (middleware, _) = CreateMiddleware(toolRegistry: toolRegistry, activeSkills: skill);

        var context = CreateContext(agent: CreateAgentWithTools("git_diff", "bash", "custom_lint", "mcp_dynamic"));

        await middleware.InvokeAsync(context, NextDelegate);

        context.ExcludedToolNames.ShouldBe(["bash"], ignoreOrder: true);
    }

    [Fact]
    public async Task InvokeAsync_NoToolRestrictions_LeavesAgentToolsAlone()
    {
        var toolRegistry = CreateToolRegistry(("bash", "shell"));
        var skill = CreateSkill(requiredModel: "claude-opus"); // model override only
        var (middleware, _) = CreateMiddleware(toolRegistry: toolRegistry, activeSkills: skill);

        var context = CreateContext(agent: CreateAgentWithTools("bash"));

        await middleware.InvokeAsync(context, NextDelegate);

        context.ExcludedToolNames.ShouldBeEmpty();
        context.EffectiveModel.ShouldBe("claude-opus");
    }

    #endregion

    #region Streaming path

    [Fact]
    public async Task InvokeStreamingAsync_NoActiveSkills_PassesThrough()
    {
        var (middleware, _) = CreateMiddleware();
        var context = CreateContext();
        var nextCalled = false;

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return AsyncEnumerable.Empty<AgentStreamChunk>();
        }))
        {
            chunks.Add(chunk);
        }

        nextCalled.ShouldBeTrue();
        context.EffectiveModel.ShouldBeNull();
    }

    [Fact]
    public async Task InvokeStreamingAsync_WithRequiredModel_SetsEffectiveModel()
    {
        var skill = CreateSkill(requiredModel: "claude-opus");
        var (middleware, _) = CreateMiddleware(activeSkills: skill);

        var context = CreateContext(model: "gpt-4o");

        await foreach (var _ in middleware.InvokeStreamingAsync(context, (ctx, ct) =>
            AsyncEnumerable.Empty<AgentStreamChunk>()))
        {
        }

        context.EffectiveModel.ShouldBe("claude-opus");
    }

    #endregion
}
