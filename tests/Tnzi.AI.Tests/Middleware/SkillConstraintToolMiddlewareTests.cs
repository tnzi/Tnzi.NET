using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// <see cref="SkillConstraintToolMiddleware"/>: the execution-time gate that makes a skill's
/// tool constraints hold in the very run that activated the skill.
/// </summary>
public class SkillConstraintToolMiddlewareTests
{
    private static (SkillConstraintToolMiddleware middleware, SkillActivationTracker tracker) Create(params (string name, string group)[] registryTools)
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetAllTools()).Returns(registryTools.Select(t => new ToolDefinition { Name = t.name, GroupName = t.group }).ToList());
        var tracker = new SkillActivationTracker();
        return (new SkillConstraintToolMiddleware(new SkillConstraintEnforcer(), registry.Object, tracker, NullLogger<SkillConstraintToolMiddleware>.Instance), tracker);
    }

    private static async Task<(object? result, bool executed, ToolExecutionContext ctx)> Call(SkillConstraintToolMiddleware middleware, string tool, string? group = null)
    {
        var executed = false;
        var ctx = new ToolExecutionContext { ToolName = tool, ToolGroup = group };
        var result = await middleware.InvokeAsync(ctx, () => { executed = true; return Task.FromResult<object?>("ran"); });
        return (result, executed, ctx);
    }

    [Fact]
    public async Task NoActiveSkills_PassesThrough()
    {
        var (mw, _) = Create(("bash", "shell"));

        var (_, executed, ctx) = await Call(mw, "bash");

        executed.ShouldBeTrue();
        ctx.FailureReason.ShouldBeNull();
    }

    [Fact]
    public async Task DeniedTool_IsShortCircuited_WithFailureReason()
    {
        var (mw, tracker) = Create(("bash", "shell"));
        tracker.Activate(new SkillDefinition { Slug = "ro", Name = "ro", Content = "x", DeniedTools = ["bash"] });

        var (result, executed, ctx) = await Call(mw, "bash");

        executed.ShouldBeFalse();
        ctx.FailureReason.ShouldNotBeNull();
        result!.ToString()!.ShouldContain("ro");
    }

    [Fact]
    public async Task DeniedTool_MatchesUngroupedDynamicToolsByName()
    {
        var (mw, tracker) = Create();
        tracker.Activate(new SkillDefinition { Slug = "s", Name = "s", Content = "x", DeniedTools = ["mcp_delete_everything"] });

        (await Call(mw, "mcp_delete_everything")).executed.ShouldBeFalse();
        (await Call(mw, "mcp_read")).executed.ShouldBeTrue();
    }

    [Fact]
    public async Task AllowedToolGroups_GroupResolvedFromRegistryWhenContextHasNone()
    {
        var (mw, tracker) = Create(("git_diff", "git"), ("bash", "shell"));
        tracker.Activate(new SkillDefinition { Slug = "s", Name = "s", Content = "x", AllowedToolGroups = ["git"] });

        (await Call(mw, "git_diff")).executed.ShouldBeTrue();
        (await Call(mw, "bash")).executed.ShouldBeFalse("group comes from the registry when ToolExecutionContext.ToolGroup is null");
        (await Call(mw, "bash", group: "shell")).executed.ShouldBeFalse();
        (await Call(mw, "mcp_dynamic")).executed.ShouldBeTrue("ungrouped tools are outside group constraints");
    }

    [Fact]
    public async Task SkillManagementTools_AreNeverBlocked()
    {
        var (mw, tracker) = Create();
        tracker.Activate(new SkillDefinition
        {
            Slug = "lockdown", Name = "l", Content = "x",
            AllowedToolGroups = ["nothing"],
            DeniedTools = ["skill_deactivate", "skill_get"]
        });

        foreach (var tool in new[] { "skill_search", "skill_get", "skill_activate", "skill_deactivate", "skill_get_resource" })
            (await Call(mw, tool)).executed.ShouldBeTrue($"{tool} must stay callable so the agent can leave the skill");
    }

    [Fact]
    public async Task ModelOnlySkill_DoesNotTouchToolCalls()
    {
        var (mw, tracker) = Create(("bash", "shell"));
        tracker.Activate(new SkillDefinition { Slug = "s", Name = "s", Content = "x", RequiredModel = "claude-opus" });

        (await Call(mw, "bash")).executed.ShouldBeTrue();
    }
}
