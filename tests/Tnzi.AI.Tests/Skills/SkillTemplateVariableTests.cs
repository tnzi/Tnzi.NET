using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// Tests for built-in template variable support ({{SESSION_ID}}, {{AGENT_NAME}}).
/// These variables are injected from SkillRenderContext, not from user-defined parameters.
/// </summary>
public class SkillTemplateVariableTests
{
    private readonly SkillTemplateEngine _engine = new();

    private static SkillDefinition MakeSkill(string content, params SkillParameter[] parameters) => new()
    {
        Slug = "test-skill",
        Name = "Test Skill",
        Content = content,
        Parameters = [.. parameters]
    };

    [Fact]
    public void Render_WithSessionId_ReplacesBuiltInVariable()
    {
        var skill = MakeSkill("Session: {{SESSION_ID}}");
        var context = new SkillRenderContext { SessionId = "abc-123" };

        var result = _engine.Render(skill, null, context);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Session: abc-123");
    }

    [Fact]
    public void Render_WithAgentName_ReplacesBuiltInVariable()
    {
        var skill = MakeSkill("Agent: {{AGENT_NAME}}");
        var context = new SkillRenderContext { AgentName = "code-reviewer" };

        var result = _engine.Render(skill, null, context);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Agent: code-reviewer");
    }

    [Fact]
    public void Render_WithBothBuiltInVariables_ReplacesBoth()
    {
        var skill = MakeSkill("Agent {{AGENT_NAME}} in session {{SESSION_ID}}");
        var context = new SkillRenderContext
        {
            SessionId = "sess-456",
            AgentName = "assistant"
        };

        var result = _engine.Render(skill, null, context);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Agent assistant in session sess-456");
    }

    [Fact]
    public void Render_BuiltInVariablesWithUserParams_BothResolved()
    {
        var skill = MakeSkill(
            "Agent {{AGENT_NAME}} reviewing {{scope}} in {{SESSION_ID}}",
            new SkillParameter { Name = "scope", Required = true });

        var parameters = new Dictionary<string, string> { ["scope"] = "security" };
        var context = new SkillRenderContext
        {
            SessionId = "sess-789",
            AgentName = "reviewer"
        };

        var result = _engine.Render(skill, parameters, context);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Agent reviewer reviewing security in sess-789");
    }

    [Fact]
    public void Render_BuiltInVariableWithoutContext_KeepsPlaceholder()
    {
        // No context provided - built-in variables remain as-is (no error, since they're recognized)
        var skill = MakeSkill("Session: {{SESSION_ID}}");

        var result = _engine.Render(skill, null, null);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Session: {{SESSION_ID}}");
    }

    [Fact]
    public void Render_BuiltInVariableDoesNotCauseUnresolvedError()
    {
        // Built-in variables should NOT trigger "unresolved placeholder" error
        // even when no parameter definition exists for them
        var skill = MakeSkill("Use {{SESSION_ID}} and {{AGENT_NAME}} here.");

        var result = _engine.Render(skill, null, null);

        result.Success.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Render_UserParamOverridesBuiltIn_UserParamWins()
    {
        // If a user explicitly provides a parameter named SESSION_ID, it should override the built-in
        var skill = MakeSkill(
            "Session: {{SESSION_ID}}",
            new SkillParameter { Name = "SESSION_ID" });

        var parameters = new Dictionary<string, string> { ["SESSION_ID"] = "user-override" };
        var context = new SkillRenderContext { SessionId = "built-in-value" };

        var result = _engine.Render(skill, parameters, context);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Session: user-override");
    }

    [Fact]
    public void Render_NullContextValues_KeepsPlaceholder()
    {
        // Context provided but values are null - should keep placeholder
        var skill = MakeSkill("Agent: {{AGENT_NAME}}");
        var context = new SkillRenderContext { AgentName = null };

        var result = _engine.Render(skill, null, context);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Agent: {{AGENT_NAME}}");
    }

    [Fact]
    public void Render_BuiltInAndUnknown_UnknownStillErrors()
    {
        // Built-in variables are fine, but truly unknown placeholders should still error
        var skill = MakeSkill("{{SESSION_ID}} and {{UNKNOWN_THING}}");

        var result = _engine.Render(skill, null, null);

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("UNKNOWN_THING"));
        result.Errors.ShouldNotContain(e => e.Contains("SESSION_ID"));
    }

    [Fact]
    public void BuiltInVariables_ContainsExpectedNames()
    {
        SkillTemplateEngine.BuiltInVariables.ShouldContain("SESSION_ID");
        SkillTemplateEngine.BuiltInVariables.ShouldContain("AGENT_NAME");
    }

    [Fact]
    public void Render_BuiltInVariables_CaseInsensitive()
    {
        // Built-in variable names should be case-insensitive
        var skill = MakeSkill("Agent: {{agent_name}}");
        var context = new SkillRenderContext { AgentName = "my-agent" };

        var result = _engine.Render(skill, null, context);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Agent: my-agent");
    }

    [Fact]
    public void Render_NoParamsNoContext_SimpleContent_Succeeds()
    {
        // Backward compatibility: no context, no built-in variables in content
        var skill = MakeSkill("You are a helpful assistant.");

        var result = _engine.Render(skill, null, null);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("You are a helpful assistant.");
    }
}
