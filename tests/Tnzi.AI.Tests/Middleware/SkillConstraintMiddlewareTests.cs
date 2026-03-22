using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// SkillConstraintMiddleware 单元测试
/// </summary>
public class SkillConstraintMiddlewareTests
{
    #region Helpers

    private static SkillConstraintMiddleware CreateMiddleware(
        ISkillConstraintEnforcer? enforcer = null,
        IToolRegistry? toolRegistry = null)
    {
        enforcer ??= new SkillConstraintEnforcer();

        if (toolRegistry == null)
        {
            var mockRegistry = new Mock<IToolRegistry>();
            mockRegistry.Setup(r => r.GetAllTools()).Returns([]);
            toolRegistry = mockRegistry.Object;
        }

        return new(enforcer, toolRegistry, Mock.Of<ILogger<SkillConstraintMiddleware>>());
    }

    private static AiMiddlewareContext CreateContext(
        string? model = null,
        string? provider = null,
        List<AITool>? additionalTools = null)
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
                agent: null!,
                provider: provider ?? "OpenAI",
                model: model ?? "gpt-4o",
                agentId: null),
            ServiceProvider = new Mock<IServiceProvider>().Object,
            AdditionalTools = additionalTools ?? []
        };
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
        var middleware = CreateMiddleware();
        var context = CreateContext();
        // No "ActiveSkills" key in Properties
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
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.Properties["ActiveSkills"] = new List<SkillDefinition>();
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

        var middleware = CreateMiddleware(toolRegistry: toolRegistry);

        var gitTool = CreateAiTool("git_diff");
        var gitCommitTool = CreateAiTool("git_commit");
        var bashTool = CreateAiTool("bash");

        var context = CreateContext(
            additionalTools: [gitTool, gitCommitTool, bashTool]);

        var skill = CreateSkill(allowedToolGroups: ["git"]);
        context.Properties["ActiveSkills"] = new List<SkillDefinition> { skill };

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
        var middleware = CreateMiddleware();

        var context = CreateContext(model: "gpt-4o");

        var skill = CreateSkill(requiredModel: "claude-opus");
        context.Properties["ActiveSkills"] = new List<SkillDefinition> { skill };

        await middleware.InvokeAsync(context, NextDelegate);

        context.EffectiveModel.ShouldBe("claude-opus");
    }

    #endregion

    #region Test 4: RequiredProvider

    [Fact]
    public async Task InvokeAsync_WithRequiredProvider_SetsEffectiveProvider()
    {
        var middleware = CreateMiddleware();

        var context = CreateContext(provider: "OpenAI");

        var skill = CreateSkill(requiredProvider: "Anthropic");
        context.Properties["ActiveSkills"] = new List<SkillDefinition> { skill };

        await middleware.InvokeAsync(context, NextDelegate);

        context.EffectiveProvider.ShouldBe("Anthropic");
    }

    #endregion

    #region Test 5: Multiple skills — intersection of allowed groups

    [Fact]
    public async Task InvokeAsync_MultipleSkills_TakesIntersection()
    {
        var toolRegistry = CreateToolRegistry(
            ("git_diff", "git"),
            ("bash", "shell"),
            ("web_search", "web"));

        var middleware = CreateMiddleware(toolRegistry: toolRegistry);

        var gitTool = CreateAiTool("git_diff");
        var bashTool = CreateAiTool("bash");
        var webTool = CreateAiTool("web_search");

        var context = CreateContext(
            additionalTools: [gitTool, bashTool, webTool]);

        // Skill A: allows git + shell
        var skillA = CreateSkill(allowedToolGroups: ["git", "shell"], priority: 0);
        skillA = new SkillDefinition
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

        context.Properties["ActiveSkills"] = new List<SkillDefinition> { skillA, skillB };

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
        var middleware = CreateMiddleware(toolRegistry: toolRegistry);

        var gitTool = CreateAiTool("git_diff");
        // "mcp_tool" is not in the registry at all (OpenAPI/MCP dynamic tool)
        var mcpTool = CreateAiTool("mcp_tool");

        var context = CreateContext(
            additionalTools: [gitTool, mcpTool]);

        // Only git allowed → git_diff filtered out (it's in registry as group "git" which is NOT "shell")
        // but mcp_tool is NOT in registry → should pass through
        var skill = CreateSkill(allowedToolGroups: ["shell"]);
        context.Properties["ActiveSkills"] = new List<SkillDefinition> { skill };

        await middleware.InvokeAsync(context, NextDelegate);

        // git_diff is in registry (group "git"), not in allowed → removed
        context.AdditionalTools.ShouldNotContain(t => t.Name == "git_diff");
        // mcp_tool is NOT in registry → passes through
        context.AdditionalTools.ShouldContain(t => t.Name == "mcp_tool");
    }

    #endregion

    #region Streaming path

    [Fact]
    public async Task InvokeStreamingAsync_NoActiveSkills_PassesThrough()
    {
        var middleware = CreateMiddleware();
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
        var middleware = CreateMiddleware();

        var context = CreateContext(model: "gpt-4o");

        var skill = CreateSkill(requiredModel: "claude-opus");
        context.Properties["ActiveSkills"] = new List<SkillDefinition> { skill };

        await foreach (var _ in middleware.InvokeStreamingAsync(context, (ctx, ct) =>
            AsyncEnumerable.Empty<AgentStreamChunk>()))
        {
        }

        context.EffectiveModel.ShouldBe("claude-opus");
    }

    #endregion
}
