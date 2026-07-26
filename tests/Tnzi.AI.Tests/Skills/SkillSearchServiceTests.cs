using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// SkillSearchService 单元测试
/// </summary>
public class SkillSearchServiceTests
{
    // -------------------------------------------------------------------------
    // Test helpers
    // -------------------------------------------------------------------------

    private static SkillSearchService CreateService() =>
        new(logger: NullLogger<SkillSearchService>.Instance, embeddingService: null);

    private static List<SkillDefinition> BuildCandidates() =>
    [
        new SkillDefinition
        {
            Slug = "code-review",
            Name = "Code Review",
            Description = "Analyzes code quality and suggests improvements.",
            Tags = ["code", "review", "quality"],
            WhenToUse = "Use when you want a thorough review of submitted code."
        },
        new SkillDefinition
        {
            Slug = "security-audit",
            Name = "Security Audit",
            Description = "Scans for vulnerabilities and security issues.",
            Tags = ["security", "audit", "vulnerability"],
            WhenToUse = "Use when you need to assess the security posture of an application."
        },
        new SkillDefinition
        {
            Slug = "performance-profiler",
            Name = "Performance Profiler",
            Description = "Identifies performance bottlenecks and optimization opportunities.",
            Tags = ["performance", "profiling", "optimization"],
            WhenToUse = "Use when an application is slow or resource-intensive."
        },
        new SkillDefinition
        {
            Slug = "doc-generator",
            Name = "Documentation Generator",
            Description = "Generates documentation from source code and comments.",
            Tags = ["docs", "documentation"],
            WhenToUse = "Use when you need auto-generated API or library documentation."
        },
        new SkillDefinition
        {
            Slug = "test-writer",
            Name = "Test Writer",
            Description = "Creates unit tests for existing code.",
            Tags = ["testing", "unit-tests", "code"],
            WhenToUse = "Use when you need to improve test coverage."
        }
    ];

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_MatchesName_HighestWeight()
    {
        var service = CreateService();
        var candidates = BuildCandidates();

        var results = await service.SearchAsync(candidates, "code", maxResults: 10);

        results.ShouldNotBeEmpty();
        results.Any(r => r.Slug == "code-review").ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_MatchesTags()
    {
        var service = CreateService();
        var candidates = BuildCandidates();

        var results = await service.SearchAsync(candidates, "security", maxResults: 10);

        results.ShouldNotBeEmpty();
        results.Any(r => r.Slug == "security-audit").ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_MatchesDescription()
    {
        var service = CreateService();
        var candidates = BuildCandidates();

        var results = await service.SearchAsync(candidates, "performance", maxResults: 10);

        results.ShouldNotBeEmpty();
        results.Any(r => r.Slug == "performance-profiler").ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var service = CreateService();
        var candidates = BuildCandidates();

        var results = await service.SearchAsync(candidates, "xyz123", maxResults: 10);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        var service = CreateService();

        // All 5 skills contain "code" somewhere or general enough query
        var candidates = BuildCandidates();

        // Use "use" - appears in every WhenToUse section
        var results = await service.SearchAsync(candidates, "use", maxResults: 3);

        results.Count.ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task SearchAsync_CaseInsensitive()
    {
        var service = CreateService();
        var candidates = BuildCandidates();

        var results = await service.SearchAsync(candidates, "CODE", maxResults: 10);

        results.ShouldNotBeEmpty();
        results.Any(r => r.Slug == "code-review").ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_MultipleTokens_ScoresCombined()
    {
        var service = CreateService();
        var candidates = BuildCandidates();

        // "code review" should score the code-review skill higher than just "code"
        var resultsMulti = await service.SearchAsync(candidates, "code review", maxResults: 10);
        var resultsSingle = await service.SearchAsync(candidates, "code", maxResults: 10);

        // "Code Review" skill must rank first in multi-token query
        resultsMulti.ShouldNotBeEmpty();
        resultsMulti[0].Slug.ShouldBe("code-review");

        // With single token, code-review still appears
        resultsSingle.Any(r => r.Slug == "code-review").ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var service = CreateService();
        var candidates = BuildCandidates();

        var results = await service.SearchAsync(candidates, "   ", maxResults: 10);

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_EmptyCandidates_ReturnsEmpty()
    {
        var service = CreateService();

        var results = await service.SearchAsync([], "code", maxResults: 10);

        results.ShouldBeEmpty();
    }
}
