using Tnzi.AI.Infrastructure.ContextProviders;
using Tnzi.AI.Skills;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// Tests for the <c>whenToUse</c> frontmatter field added in Phase 3.2:
/// <list type="bullet">
///   <item>SkillMarkdownParser must accept the field in kebab-, camel-, and snake-case.</item>
///   <item>SkillContextProvider.BuildSkillSummary must prefer <c>WhenToUse</c> over <c>Description</c> when both are present.</item>
///   <item>BuildSkillSummary must fall back to <c>Description</c> when <c>WhenToUse</c> is empty (legacy SKILL.md).</item>
/// </list>
/// </summary>
public class SkillWhenToUseTests
{
    // ------------------------------------------------------------------ //
    // Frontmatter parsing
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData("when-to-use", "Use when reviewing PRs, auditing code quality.")]
    [InlineData("whenToUse", "Use when reviewing PRs, auditing code quality.")]
    [InlineData("when_to_use", "Use when reviewing PRs, auditing code quality.")]
    public void Parser_AcceptsAllThreeKeyCasings(string key, string value)
    {
        var content = $"""
            ---
            name: Demo
            slug: demo
            description: Short summary.
            {key}: {value}
            ---

            ## When to Use

            Body.
            """;

        var skill = SkillMarkdownParser.Parse(content, "/virtual/SKILL.md");

        skill.ShouldNotBeNull();
        skill!.WhenToUse.ShouldBe(value);
    }

    [Fact]
    public void Parser_KebabCasePreferredOverCamelCase()
    {
        // If both are present the kebab-case form wins (matches the rest of
        // the frontmatter convention: allowed-tool-groups, tool-whitelist, etc.)
        var content = """
            ---
            name: Demo
            slug: demo
            when-to-use: Kebab wins
            whenToUse: Camel loses
            ---

            Body.
            """;

        var skill = SkillMarkdownParser.Parse(content, "/virtual/SKILL.md");

        skill!.WhenToUse.ShouldBe("Kebab wins");
    }

    [Fact]
    public void Parser_WhenToUseAbsent_LeavesPropertyNull()
    {
        var content = """
            ---
            name: Demo
            slug: demo
            description: only description provided
            ---

            Body.
            """;

        var skill = SkillMarkdownParser.Parse(content, "/virtual/SKILL.md");

        skill!.WhenToUse.ShouldBeNull();
        skill.Description.ShouldBe("only description provided");
    }

    [Fact]
    public void Parser_DescriptionAndWhenToUse_AreIndependentFields()
    {
        var content = """
            ---
            name: Demo
            slug: demo
            description: One-line human-readable summary.
            when-to-use: Triggers: review, audit, LGTM, code smell.
            ---

            Body.
            """;

        var skill = SkillMarkdownParser.Parse(content, "/virtual/SKILL.md");

        skill!.Description.ShouldBe("One-line human-readable summary.");
        skill.WhenToUse.ShouldBe("Triggers: review, audit, LGTM, code smell.");
    }

    // ------------------------------------------------------------------ //
    // BuildSkillSummary - routing hint prefers WhenToUse, falls back to Description
    // ------------------------------------------------------------------ //

    [Fact]
    public void Summary_PrefersWhenToUseOverDescription()
    {
        var skill = new SkillDefinition
        {
            Name = "Code Review",
            Slug = "code-review",
            Description = "Human-friendly summary.",
            WhenToUse = "Use when reviewing PRs or terms like LGTM/code smell.",
            Priority = 100
        };

        var summary = SkillContextProvider.BuildSkillSummary([skill]);

        summary.ShouldContain("Use when reviewing PRs or terms like LGTM/code smell.");
        summary.ShouldNotContain("Human-friendly summary.");
    }

    [Fact]
    public void Summary_FallsBackToDescription_WhenWhenToUseIsEmpty()
    {
        // Legacy SKILL.md path - only description is populated.
        var skill = new SkillDefinition
        {
            Name = "Code Review",
            Slug = "code-review",
            Description = "Code quality review. Use when reviewing PRs.",
            WhenToUse = null
        };

        var summary = SkillContextProvider.BuildSkillSummary([skill]);

        summary.ShouldContain("Code quality review. Use when reviewing PRs.");
    }

    [Fact]
    public void Summary_FallsBackToDescription_WhenWhenToUseIsWhitespace()
    {
        var skill = new SkillDefinition
        {
            Name = "Code Review",
            Slug = "code-review",
            Description = "Description text.",
            WhenToUse = "   "
        };

        var summary = SkillContextProvider.BuildSkillSummary([skill]);

        summary.ShouldContain("Description text.");
    }

    [Fact]
    public void Summary_BothEmpty_OmitsHintGracefully()
    {
        var skill = new SkillDefinition
        {
            Name = "Code Review",
            Slug = "code-review",
            Description = string.Empty,
            WhenToUse = null
        };

        var summary = SkillContextProvider.BuildSkillSummary([skill]);

        // Skill still appears in listing, just without a routing hint.
        summary.ShouldContain("**Code Review**");
        summary.ShouldContain("`code-review`");
    }
}
