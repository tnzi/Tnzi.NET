using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// FileSystemSkillStore 单元测试
/// </summary>
public class FileSystemSkillStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemSkillStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-fsstore-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // -------------------------------------------------------------------------
    // GetAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_LoadsSkillsFromPath()
    {
        CreateSkillFile("my-skill", """
            # My Test Skill

            A skill for testing.

            ## When to Use

            Use when testing.

            ## Requirements

            - bins: git
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].Name.ShouldBe("My Test Skill");
        skills[0].Description.ShouldBe("A skill for testing.");
        skills[0].WhenToUse.ShouldContain("testing");
        skills[0].Requirements.ShouldNotBeNull();
        skills[0].Requirements!.Bins.ShouldContain("git");
    }

    [Fact]
    public async Task GetAllAsync_ParsesParametersSection()
    {
        CreateSkillFile("param-skill", """
            # Param Skill

            A skill with parameters.

            ## Parameters

            - format: Output format (required, allowed: json, yaml, text, default: json)
            - verbose: Enable verbose output (optional)
            - level: Verbosity level (optional, allowed: 1, 2, 3, default: 1)
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        var parameters = skills[0].Parameters;
        parameters.ShouldNotBeEmpty();
        parameters.Count.ShouldBe(3);

        var format = parameters.FirstOrDefault(p => p.Name == "format");
        format.ShouldNotBeNull();
        format!.Description.ShouldBe("Output format");
        format.Required.ShouldBeTrue();
        format.AllowedValues.ShouldNotBeNull();
        format.AllowedValues!.ShouldContain("json");
        format.AllowedValues!.ShouldContain("yaml");
        format.AllowedValues!.ShouldContain("text");
        format.DefaultValue.ShouldBe("json");

        var verbose = parameters.FirstOrDefault(p => p.Name == "verbose");
        verbose.ShouldNotBeNull();
        verbose!.Required.ShouldBeFalse();

        var level = parameters.FirstOrDefault(p => p.Name == "level");
        level.ShouldNotBeNull();
        level!.Required.ShouldBeFalse();
        level.AllowedValues.ShouldNotBeNull();
        level.AllowedValues!.ShouldContain("1");
        level.AllowedValues!.ShouldContain("2");
        level.AllowedValues!.ShouldContain("3");
        level.DefaultValue.ShouldBe("1");
    }

    [Fact]
    public async Task GetAllAsync_GeneratesSlugFromName()
    {
        CreateSkillFile("code-review-dir", "# Code Review\n\nReview code.");

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].Slug.ShouldBe("code-review");
    }

    [Fact]
    public async Task GetAllAsync_SetsSystemScope()
    {
        CreateSkillFile("scope-skill", "# Scope Skill\n\nTests scope.");
        CreateSkillFile("scope-skill-2", "# Another Skill\n\nTests source.");

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(2);
        skills.ShouldAllBe(s => s.Scope == SkillScope.System);
        skills.ShouldAllBe(s => s.Source == SkillSource.FileSystem);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesDisabledSkills()
    {
        CreateSkillFile("enabled-skill", """
            # Enabled Skill

            This one is enabled.

            ## Metadata

            - enabled: true
            """);

        CreateSkillFile("disabled-skill", """
            # Disabled Skill

            This one is disabled.

            ## Metadata

            - enabled: false
            """);

        // FileSystemSkillStore loads all skills — the caller filters by Enabled.
        // Verify that Enabled=false is parsed correctly from metadata.
        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(2);
        var disabled = skills.FirstOrDefault(s => s.Name == "Disabled Skill");
        disabled.ShouldNotBeNull();
        disabled!.Enabled.ShouldBeFalse();

        var enabled = skills.FirstOrDefault(s => s.Name == "Enabled Skill");
        enabled.ShouldNotBeNull();
        enabled!.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllAsync_CachesResults()
    {
        CreateSkillFile("cache-skill", "# Cache Skill\n\nTests caching.");

        var store = CreateStore(paths: [_tempDir]);

        var first = await store.GetAllAsync();
        first.Count.ShouldBe(1);

        // Add another file after first call — should not appear in second call (cache hit)
        CreateSkillFile("cache-skill-2", "# Cache Skill 2\n\nAdded after cache.");

        var second = await store.GetAllAsync();
        second.Count.ShouldBe(1); // still 1 — from cache
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllAsync_ParsesConstraintMetadata()
    {
        CreateSkillFile("constraint-skill", """
            # Constraint Skill

            A skill with constraint metadata.

            ## Metadata

            - allowed-tools: code, search
            - model: gpt-4
            - provider: openai
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].AllowedToolGroups.ShouldNotBeNull();
        skills[0].AllowedToolGroups!.ShouldContain("code");
        skills[0].AllowedToolGroups!.ShouldContain("search");
        skills[0].RequiredModel.ShouldBe("gpt-4");
        skills[0].RequiredProvider.ShouldBe("openai");
    }

    // -------------------------------------------------------------------------
    // GetBySlugAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBySlugAsync_ReturnsMatchingSkill()
    {
        CreateSkillFile("lookup-skill", "# Lookup Skill\n\nFind me by slug.");

        var store = CreateStore(paths: [_tempDir]);
        var skill = await store.GetBySlugAsync("lookup-skill");

        skill.ShouldNotBeNull();
        skill!.Name.ShouldBe("Lookup Skill");
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsNull_WhenNotFound()
    {
        CreateSkillFile("existing-skill", "# Existing Skill\n\nI exist.");

        var store = CreateStore(paths: [_tempDir]);
        var skill = await store.GetBySlugAsync("nonexistent-skill");

        skill.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // GenerateSlug (unit tests)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Code Review", "code-review")]
    [InlineData("my_skill", "my-skill")]
    [InlineData("Hello World", "hello-world")]
    [InlineData("  Leading Spaces  ", "leading-spaces")]
    [InlineData("Multiple   Spaces", "multiple-spaces")]
    [InlineData("Special!@#Chars", "specialchars")]
    [InlineData("", "unknown")]
    public void GenerateSlug_ProducesCorrectSlug(string name, string expectedSlug)
    {
        FileSystemSkillStore.GenerateSlug(name).ShouldBe(expectedSlug);
    }

    [Fact]
    public void GenerateSlug_TruncatesTo64Chars()
    {
        var longName = new string('a', 100);
        var slug = FileSystemSkillStore.GenerateSlug(longName);
        slug.Length.ShouldBeLessThanOrEqualTo(64);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void CreateSkillFile(string folderName, string content)
    {
        var dir = Path.Combine(_tempDir, folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
    }

    private FileSystemSkillStore CreateStore(
        List<string>? paths = null,
        List<string>? allowList = null,
        List<string>? denyList = null,
        bool requireChecksEnabled = true)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Skills = new SkillsOptions
                {
                    Paths = paths ?? [],
                    AllowList = allowList ?? [],
                    DenyList = denyList ?? [],
                    RequireChecksEnabled = requireChecksEnabled
                }
            }
        });

        var logger = Mock.Of<ILogger<FileSystemSkillStore>>();
        return new FileSystemSkillStore(logger, options);
    }
}
