using Tnzi.AI.Infrastructure.ContextProviders;
using Tnzi.AI.Skills.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// Tests for skill listing budget management in BuildSkillSummary.
/// When total skill listing exceeds 8000 characters, lower-priority skills should be truncated.
/// </summary>
public class SkillListingBudgetTests
{
    [Fact]
    public void BuildSkillSummary_UnderBudget_IncludesAllSkills()
    {
        var skills = new List<SkillDefinition>
        {
            new() { Slug = "skill-1", Name = "Skill One", Description = "Short desc", Priority = 1 },
            new() { Slug = "skill-2", Name = "Skill Two", Description = "Another desc", Priority = 2 }
        };

        var result = SkillContextProvider.BuildSkillSummary(skills);

        result.ShouldContain("Skill One");
        result.ShouldContain("Skill Two");
        result.ShouldNotContain("omitted");
    }

    [Fact]
    public void BuildSkillSummary_OverBudget_TruncatesByPriority()
    {
        // Create many skills with long descriptions to exceed budget
        var skills = new List<SkillDefinition>();
        for (var i = 0; i < 200; i++)
        {
            skills.Add(new SkillDefinition
            {
                Slug = $"skill-{i:D3}",
                Name = $"Skill Number {i:D3}",
                Description = new string('x', 100), // ~140 chars per line
                Priority = 200 - i // Higher index = lower priority
            });
        }

        var result = SkillContextProvider.BuildSkillSummary(skills);

        // Total chars would be ~200 * 140 = 28000, well over 8000
        result.Length.ShouldBeLessThanOrEqualTo(SkillContextProvider.SkillListingBudget + 200); // some margin for truncation notice
        result.ShouldContain("omitted");
    }

    [Fact]
    public void BuildSkillSummary_OverBudget_KeepsHigherPriorityFirst()
    {
        var skills = new List<SkillDefinition>();
        for (var i = 0; i < 200; i++)
        {
            skills.Add(new SkillDefinition
            {
                Slug = $"skill-{i:D3}",
                Name = $"Skill {i:D3}",
                Description = new string('a', 100),
                Priority = i // skill-199 has highest priority
            });
        }

        var result = SkillContextProvider.BuildSkillSummary(skills);

        // The highest priority skill (Skill 199) should always be included
        result.ShouldContain("Skill 199");
        result.ShouldContain("Skill 198");

        // The lowest priority skill (Skill 000) should be truncated
        result.ShouldContain("omitted");
    }

    [Fact]
    public void BuildSkillSummary_ExactlyAtBudget_NoTruncation()
    {
        // Create a few skills that fit within budget
        var skills = new List<SkillDefinition>
        {
            new() { Slug = "alpha", Name = "Alpha", Description = "First skill", Priority = 10 },
            new() { Slug = "beta", Name = "Beta", Description = "Second skill", Priority = 5 },
            new() { Slug = "gamma", Name = "Gamma", Description = "Third skill", Priority = 1 }
        };

        var result = SkillContextProvider.BuildSkillSummary(skills);

        result.ShouldContain("Alpha");
        result.ShouldContain("Beta");
        result.ShouldContain("Gamma");
        result.ShouldNotContain("omitted");
    }

    [Fact]
    public void BuildSkillSummary_EmptyList_ReturnsHeaderAndFooter()
    {
        var result = SkillContextProvider.BuildSkillSummary([]);

        result.ShouldContain("## Available Skills");
        result.ShouldContain("skill_get");
        result.ShouldNotContain("omitted");
    }

    [Fact]
    public void BuildSkillSummary_SortedByPriorityDescending()
    {
        var skills = new List<SkillDefinition>
        {
            new() { Slug = "low", Name = "Low Priority", Priority = 1 },
            new() { Slug = "high", Name = "High Priority", Priority = 10 },
            new() { Slug = "mid", Name = "Mid Priority", Priority = 5 }
        };

        var result = SkillContextProvider.BuildSkillSummary(skills);

        // High priority should appear before low priority
        var highIdx = result.IndexOf("High Priority", StringComparison.Ordinal);
        var midIdx = result.IndexOf("Mid Priority", StringComparison.Ordinal);
        var lowIdx = result.IndexOf("Low Priority", StringComparison.Ordinal);

        highIdx.ShouldBeLessThan(midIdx);
        midIdx.ShouldBeLessThan(lowIdx);
    }

    [Fact]
    public void BuildSkillSummary_TruncationNotice_ShowsCount()
    {
        // Create enough skills to trigger truncation
        var skills = new List<SkillDefinition>();
        for (var i = 0; i < 100; i++)
        {
            skills.Add(new SkillDefinition
            {
                Slug = $"s-{i}",
                Name = $"Skill {i}",
                Description = new string('z', 150),
                Priority = 100 - i
            });
        }

        var result = SkillContextProvider.BuildSkillSummary(skills);

        // Should mention how many were omitted
        result.ShouldContain("more skill(s) omitted");
    }
}
