
namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// SkillTemplateEngine 单元测试
/// </summary>
public class SkillTemplateEngineTests
{
    private readonly SkillTemplateEngine _engine = new();

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static SkillDefinition MakeSkill(string content, params SkillParameter[] parameters) => new()
    {
        Slug = "test-skill",
        Name = "Test Skill",
        Content = content,
        Parameters = [.. parameters]
    };

    private static SkillParameter Param(string name, bool required = false, string? defaultValue = null, List<string>? allowedValues = null) => new()
    {
        Name = name,
        Required = required,
        DefaultValue = defaultValue,
        AllowedValues = allowedValues
    };

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_NoParams_ReturnsContentUnchanged()
    {
        var skill = MakeSkill("You are a helpful assistant.");

        var result = _engine.Render(skill);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("You are a helpful assistant.");
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Render_WithParams_ReplacesPlaceholders()
    {
        var skill = MakeSkill("Focus on {{scope}} vulnerabilities.", Param("scope"));
        var parameters = new Dictionary<string, string> { ["scope"] = "security" };

        var result = _engine.Render(skill, parameters);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Focus on security vulnerabilities.");
    }

    [Fact]
    public void Render_MissingRequired_ReturnsError()
    {
        var skill = MakeSkill("Focus on {{scope}} issues.", Param("scope", required: true));

        var result = _engine.Render(skill, parameters: null);

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("scope"));
    }

    [Fact]
    public void Render_InvalidAllowedValue_ReturnsError()
    {
        var skill = MakeSkill("Scope: {{scope}}.", Param("scope", allowedValues: ["security", "performance"]));
        var parameters = new Dictionary<string, string> { ["scope"] = "invalid" };

        var result = _engine.Render(skill, parameters);

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("scope"));
    }

    [Fact]
    public void Render_OptionalWithDefault_UsesDefault()
    {
        var skill = MakeSkill("Analyze {{scope}} areas.", Param("scope", required: false, defaultValue: "all"));

        var result = _engine.Render(skill, parameters: null);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("Analyze all areas.");
    }

    [Fact]
    public void Render_UnusedParam_ReportsUnused()
    {
        var skill = MakeSkill("Simple prompt.", Param("scope"));
        var parameters = new Dictionary<string, string> { ["scope"] = "security", ["extra"] = "unused" };

        var result = _engine.Render(skill, parameters);

        result.UnusedParameters.ShouldContain("extra");
    }

    [Fact]
    public void Render_UnresolvedPlaceholder_ReportsError()
    {
        // Template references {{unknown}} but no SkillParameter defines it
        var skill = MakeSkill("Check {{unknown}} now.");

        var result = _engine.Render(skill, parameters: null);

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("unknown"));
    }

    [Fact]
    public void ExtractParameterNames_ReturnsAllPlaceholders()
    {
        var names = _engine.ExtractParameterNames("Focus on {{scope}} at {{severity}} level.");

        names.ShouldContain("scope");
        names.ShouldContain("severity");
        names.Count.ShouldBe(2);
    }

    [Fact]
    public void ExtractParameterNames_EmptyTemplate_ReturnsEmpty()
    {
        var names = _engine.ExtractParameterNames(string.Empty);

        names.ShouldBeEmpty();
    }

    [Fact]
    public void Render_NullParams_WithNoPlaceholders_Succeeds()
    {
        var skill = MakeSkill("You are a code reviewer.");

        var result = _engine.Render(skill, parameters: null);

        result.Success.ShouldBeTrue();
        result.RenderedContent.ShouldBe("You are a code reviewer.");
    }
}
