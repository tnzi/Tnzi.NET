using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;

namespace Tnzi.AI.Tests.Skills;

public class SkillConstraintEnforcerTests
{
    private readonly SkillConstraintEnforcer _enforcer = new();

    // 1. No constraints → all available groups pass through
    [Fact]
    public void Apply_NoConstraints_ReturnsAllGroups()
    {
        var skill = new SkillDefinition { AllowedToolGroups = null };
        var context = new SkillConstraintContext
        {
            AvailableToolGroups = ["shell", "git", "web"]
        };

        var result = _enforcer.Apply(skill, context);

        result.EffectiveToolGroups.Count.ShouldBe(3);
        result.EffectiveToolGroups.ShouldContain("shell");
        result.EffectiveToolGroups.ShouldContain("git");
        result.EffectiveToolGroups.ShouldContain("web");
        result.FilteredOutToolGroups.Count.ShouldBe(0);
    }

    // 2. AllowedGroups filters to intersection
    [Fact]
    public void Apply_AllowedGroups_FiltersToIntersection()
    {
        var skill = new SkillDefinition { AllowedToolGroups = ["git", "web"] };
        var context = new SkillConstraintContext
        {
            AvailableToolGroups = ["shell", "git", "web"]
        };

        var result = _enforcer.Apply(skill, context);

        result.EffectiveToolGroups.Count.ShouldBe(2);
        result.EffectiveToolGroups.ShouldContain("git");
        result.EffectiveToolGroups.ShouldContain("web");
    }

    // 3. AllowedGroups reports filtered-out groups
    [Fact]
    public void Apply_AllowedGroups_ReportsFiltered()
    {
        var skill = new SkillDefinition { AllowedToolGroups = ["git", "web"] };
        var context = new SkillConstraintContext
        {
            AvailableToolGroups = ["shell", "git", "web"]
        };

        var result = _enforcer.Apply(skill, context);

        result.FilteredOutToolGroups.Count.ShouldBe(1);
        result.FilteredOutToolGroups.ShouldContain("shell");
    }

    // 4. RequiredModel overrides CurrentModel
    [Fact]
    public void Apply_RequiredModel_OverridesCurrentModel()
    {
        var skill = new SkillDefinition { RequiredModel = "claude-opus" };
        var context = new SkillConstraintContext
        {
            AvailableToolGroups = [],
            CurrentModel = "gpt-4"
        };

        var result = _enforcer.Apply(skill, context);

        result.EffectiveModel.ShouldBe("claude-opus");
    }

    // 5. Null RequiredModel keeps CurrentModel
    [Fact]
    public void Apply_NullRequiredModel_KeepsCurrent()
    {
        var skill = new SkillDefinition { RequiredModel = null };
        var context = new SkillConstraintContext
        {
            AvailableToolGroups = [],
            CurrentModel = "gpt-4"
        };

        var result = _enforcer.Apply(skill, context);

        result.EffectiveModel.ShouldBe("gpt-4");
    }

    // 6. RequiredProvider overrides CurrentProvider
    [Fact]
    public void Apply_RequiredProvider_OverridesCurrent()
    {
        var skill = new SkillDefinition { RequiredProvider = "anthropic" };
        var context = new SkillConstraintContext
        {
            AvailableToolGroups = [],
            CurrentProvider = "openai"
        };

        var result = _enforcer.Apply(skill, context);

        result.EffectiveProvider.ShouldBe("anthropic");
    }

    // 7. Empty AllowedToolGroups (Count=0) means no restriction → returns all groups
    [Fact]
    public void Apply_EmptyAllowedGroups_ReturnsAllGroups()
    {
        var skill = new SkillDefinition { AllowedToolGroups = [] };
        var context = new SkillConstraintContext
        {
            AvailableToolGroups = ["shell", "git", "web"]
        };

        var result = _enforcer.Apply(skill, context);

        result.EffectiveToolGroups.Count.ShouldBe(3);
        result.FilteredOutToolGroups.Count.ShouldBe(0);
    }
}
