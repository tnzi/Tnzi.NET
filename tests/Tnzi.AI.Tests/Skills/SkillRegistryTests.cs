using Moq;
using Tnzi.AI.Options;
using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// SkillRegistry 单元测试
/// </summary>
public class SkillRegistryTests : IDisposable
{
    private readonly string _tempDir;

    public SkillRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-registry-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void CreateSkillFile(string dirName, string content)
    {
        var dir = Path.Combine(_tempDir, dirName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
    }

    private FileSystemSkillStore CreateFileStore()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Skills = new SkillsOptions
                {
                    Paths = [_tempDir],
                    AllowList = [],
                    DenyList = [],
                    RequireChecksEnabled = false
                }
            }
        });
        var logger = Mock.Of<ILogger<FileSystemSkillStore>>();
        return new FileSystemSkillStore(logger, options);
    }

    private static SkillRegistry CreateRegistry(FileSystemSkillStore fileStore, ISkillSearchService? searchService = null)
    {
        searchService ??= new SkillSearchService();
        var logger = Mock.Of<ILogger<SkillRegistry>>();
        return new SkillRegistry(fileStore, searchService, logger);
    }

    // -------------------------------------------------------------------------
    // GetAvailableSkillsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAvailableSkillsAsync_ReturnsSystemSkills()
    {
        CreateSkillFile("code-review", """
            # Code Review

            Reviews code quality.

            ## When to Use

            Use when reviewing pull requests.
            """);

        var registry = CreateRegistry(CreateFileStore());

        var skills = await registry.GetAvailableSkillsAsync();

        skills.ShouldNotBeEmpty();
        skills.Any(s => s.Slug == "code-review").ShouldBeTrue();
    }

    [Fact]
    public async Task GetAvailableSkillsAsync_DeduplicatesBySlug_SystemWins()
    {
        // Two skills with the same slug but different scopes should deduplicate — System wins
        CreateSkillFile("skill-a", """
            # Duplicate Skill

            First instance.
            """);

        var fileStore = CreateFileStore();
        var registry = CreateRegistry(fileStore);

        var skills = await registry.GetAvailableSkillsAsync();

        // Should contain only one entry for the slug
        var matches = skills.Where(s => s.Slug == "duplicate-skill").ToList();
        matches.Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // GetBySlugAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBySlugAsync_FindsBySlug()
    {
        CreateSkillFile("code-review", """
            # Code Review

            Reviews code quality.
            """);

        var registry = CreateRegistry(CreateFileStore());

        var skill = await registry.GetBySlugAsync("code-review");

        skill.ShouldNotBeNull();
        skill!.Slug.ShouldBe("code-review");
    }

    [Fact]
    public async Task GetBySlugAsync_WithNamespace_FiltersScope()
    {
        CreateSkillFile("code-review", """
            # Code Review

            Reviews code quality.
            """);

        var registry = CreateRegistry(CreateFileStore());

        // FileSystemSkillStore sets Scope = System by default
        var skill = await registry.GetBySlugAsync("system:code-review");

        skill.ShouldNotBeNull();
        skill!.Scope.ShouldBe(SkillScope.System);
        skill.Slug.ShouldBe("code-review");
    }

    [Fact]
    public async Task GetBySlugAsync_WithWrongScope_ReturnsNull()
    {
        CreateSkillFile("code-review", """
            # Code Review

            Reviews code quality.
            """);

        var registry = CreateRegistry(CreateFileStore());

        // Skill is System-scoped; querying with tenant: prefix should not find it
        var skill = await registry.GetBySlugAsync("tenant:code-review");

        skill.ShouldBeNull();
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsNull_WhenNotFound()
    {
        var registry = CreateRegistry(CreateFileStore());

        var skill = await registry.GetBySlugAsync("nonexistent-skill");

        skill.ShouldBeNull();
    }

    [Fact]
    public async Task GetBySlugAsync_NullOrEmpty_ReturnsNull()
    {
        var registry = CreateRegistry(CreateFileStore());

        var skill1 = await registry.GetBySlugAsync("");
        var skill2 = await registry.GetBySlugAsync("   ");

        skill1.ShouldBeNull();
        skill2.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // SearchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_DelegatesToSearchService()
    {
        CreateSkillFile("code-review", """
            # Code Review

            Reviews code quality.
            """);

        var mockSearch = new Mock<ISkillSearchService>();
        mockSearch
            .Setup(s => s.SearchAsync(
                It.IsAny<IReadOnlyList<SkillDefinition>>(),
                It.Is<string>(q => q == "code"),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var registry = CreateRegistry(CreateFileStore(), mockSearch.Object);

        await registry.SearchAsync("code", maxResults: 5);

        mockSearch.Verify(s => s.SearchAsync(
            It.IsAny<IReadOnlyList<SkillDefinition>>(),
            "code",
            5,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ReturnsMatchingSkills()
    {
        CreateSkillFile("code-review", """
            # Code Review

            Reviews code quality.
            """);

        var registry = CreateRegistry(CreateFileStore());

        var results = await registry.SearchAsync("code", maxResults: 10);

        results.ShouldNotBeEmpty();
        results.Any(r => r.Slug == "code-review").ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // InvalidateCache
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvalidateCache_ClearsFileStoreCache()
    {
        CreateSkillFile("code-review", """
            # Code Review

            Reviews code quality.
            """);

        var fileStore = CreateFileStore();
        var registry = CreateRegistry(fileStore);

        // Prime the cache
        var first = await registry.GetAvailableSkillsAsync();
        first.ShouldNotBeEmpty();

        // Invalidate and add a new skill file
        registry.InvalidateCache();
        CreateSkillFile("security-audit", """
            # Security Audit

            Audits security vulnerabilities.
            """);

        // After invalidation, the new skill should be visible
        var second = await registry.GetAvailableSkillsAsync();
        second.Any(s => s.Slug == "security-audit").ShouldBeTrue();
    }
}
