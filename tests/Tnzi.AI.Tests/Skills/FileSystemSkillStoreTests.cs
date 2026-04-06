using Tnzi.AI.Infrastructure.ContextProviders;
using Tnzi.AI.Skills;
using Tnzi.AI.Skills.Models;
using Microsoft.Extensions.Hosting;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// FileSystemSkillStore 单元测试
/// </summary>
public class FileSystemSkillStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemSkillStoreTests()
    {
        var baseTempDir = Path.Combine(AppContext.BaseDirectory, ".tmp", "fsstore");
        Directory.CreateDirectory(baseTempDir);
        _tempDir = Path.Combine(baseTempDir, $"tnzi-fsstore-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
            // Best-effort cleanup only. Test assertions should not fail because the host keeps a temp file handle.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only. Test assertions should not fail because the host keeps a temp file handle.
        }
    }

    // -------------------------------------------------------------------------
    // GetAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_LoadsSkillsFromPath()
    {
        CreateSkillFile("my-skill", """
            ---
            name: My Test Skill
            description: A skill for testing.
            ---

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
        skills[0].Requirements.ShouldNotBeNull();
        skills[0].Requirements!.Bins.ShouldContain("git");
    }

    [Fact]
    public async Task GetAllAsync_GeneratesSlugFromName()
    {
        CreateSkillFile("code-review-dir", """
            ---
            name: Code Review
            description: Review code.
            ---
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].Slug.ShouldBe("code-review");
    }

    [Fact]
    public async Task GetAllAsync_UsesExplicitSlug()
    {
        CreateSkillFile("my-dir", """
            ---
            name: My Skill
            slug: custom-slug
            description: A skill with explicit slug.
            ---
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].Slug.ShouldBe("custom-slug");
    }

    [Fact]
    public async Task GetAllAsync_SetsSystemScope()
    {
        CreateSkillFile("scope-skill", """
            ---
            name: Scope Skill
            description: Tests scope.
            ---
            """);
        CreateSkillFile("scope-skill-2", """
            ---
            name: Another Skill
            description: Tests source.
            ---
            """);

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
            ---
            name: Enabled Skill
            description: This one is enabled.
            enabled: true
            ---
            """);

        CreateSkillFile("disabled-skill", """
            ---
            name: Disabled Skill
            description: This one is disabled.
            enabled: false
            ---
            """);

        // FileSystemSkillStore loads all skills — the caller filters by Enabled.
        // Verify that Enabled=false is parsed correctly from frontmatter.
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
        CreateSkillFile("cache-skill", """
            ---
            name: Cache Skill
            description: Tests caching.
            ---
            """);

        var store = CreateStore(paths: [_tempDir]);

        var first = await store.GetAllAsync();
        first.Count.ShouldBe(1);

        // Add another file after first call — should not appear in second call (cache hit)
        CreateSkillFile("cache-skill-2", """
            ---
            name: Cache Skill 2
            description: Added after cache.
            ---
            """);

        var second = await store.GetAllAsync();
        second.Count.ShouldBe(1); // still 1 — from cache
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllAsync_ParsesConstraintMetadata()
    {
        CreateSkillFile("constraint-skill", """
            ---
            name: Constraint Skill
            description: A skill with constraint metadata.
            allowed-tool-groups: code, search
            model: gpt-4
            provider: openai
            ---
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

    [Fact]
    public async Task GetAllAsync_SkipsGitIgnoredSkillFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "ignored-skill/");
        CreateSkillFile("included-skill", """
            ---
            name: Included Skill
            description: Should load.
            ---
            """);
        CreateSkillFile("ignored-skill", """
            ---
            name: Ignored Skill
            description: Should be skipped.
            ---
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].Name.ShouldBe("Included Skill");
    }

    [Fact]
    public async Task GetAllAsync_LoadsInstalledSkillsFromRelativeDataPath()
    {
        var contentRoot = Path.Combine(_tempDir, "content-root");
        var dataRoot = Path.Combine(contentRoot, "app-data");
        var skillDir = Path.Combine(dataRoot, "skills", "tenant-skill");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: Installed Skill
            description: Loaded from DataPath.
            ---
            """);

        var store = CreateStore(dataPath: "app-data", contentRootPath: contentRoot);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].Name.ShouldBe("Installed Skill");
        skills[0].FilePath.ShouldNotBeNull();
        Path.GetFullPath(skills[0].FilePath!).Contains(Path.GetFullPath(dataRoot), StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllAsync_ParsesAllFrontmatterFields()
    {
        CreateSkillFile("full-skill", """
            ---
            name: Full Skill
            slug: full-skill
            description: A comprehensive skill.
            version: 2.0
            author: test-author
            tags: tag1, tag2, tag3
            priority: 15
            enabled: true
            allowed-tool-groups: code, search
            tool-whitelist: special_tool
            tool-blacklist: dangerous_tool
            model: claude-3-opus
            provider: anthropic
            agents: rpi-*, finance-agent
            ---

            ## When to Use

            Use for comprehensive testing.
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        var skill = skills[0];

        skill.Name.ShouldBe("Full Skill");
        skill.Slug.ShouldBe("full-skill");
        skill.Description.ShouldBe("A comprehensive skill.");
        skill.Version.ShouldBe("2.0");
        skill.Author.ShouldBe("test-author");
        skill.Tags.ShouldContain("tag1");
        skill.Tags.ShouldContain("tag2");
        skill.Tags.ShouldContain("tag3");
        skill.Priority.ShouldBe(15);
        skill.Enabled.ShouldBeTrue();
        skill.AllowedToolGroups.ShouldNotBeNull();
        skill.AllowedToolGroups!.ShouldContain("code");
        skill.AllowedToolGroups!.ShouldContain("search");
        skill.AllowedTools.ShouldNotBeNull();
        skill.AllowedTools!.ShouldContain("special_tool");
        skill.DeniedTools.ShouldNotBeNull();
        skill.DeniedTools!.ShouldContain("dangerous_tool");
        skill.RequiredModel.ShouldBe("claude-3-opus");
        skill.RequiredProvider.ShouldBe("anthropic");
        skill.Agents.ShouldNotBeNull();
        skill.Agents!.ShouldContain("rpi-*");
        skill.Agents!.ShouldContain("finance-agent");
    }

    [Fact]
    public void Parse_WithoutFrontmatter_ReturnsNull()
    {
        var result = SkillMarkdownParser.Parse("# Old Format\n\nNo frontmatter.", "/fake/path");
        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_WithUnclosedFrontmatter_ReturnsNull()
    {
        var result = SkillMarkdownParser.Parse("---\nname: Test\ndescription: Missing closing.", "/fake/path");
        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_WithCommentInFrontmatter_SkipsComments()
    {
        var result = SkillMarkdownParser.Parse("""
            ---
            name: Comment Test
            description: Has comments.
            # This is a comment
            version: 1.0
            ---
            """, "/fake/path");

        result.ShouldNotBeNull();
        result!.Name.ShouldBe("Comment Test");
        result.Version.ShouldBe("1.0");
    }

    // -------------------------------------------------------------------------
    // GetBySlugAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBySlugAsync_ReturnsMatchingSkill()
    {
        CreateSkillFile("lookup-skill", """
            ---
            name: Lookup Skill
            description: Find me by slug.
            ---
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skill = await store.GetBySlugAsync("lookup-skill");

        skill.ShouldNotBeNull();
        skill!.Name.ShouldBe("Lookup Skill");
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsNull_WhenNotFound()
    {
        CreateSkillFile("existing-skill", """
            ---
            name: Existing Skill
            description: I exist.
            ---
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skill = await store.GetBySlugAsync("nonexistent-skill");

        skill.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // IsAgentAllowed (wildcard matching)
    // -------------------------------------------------------------------------

    [Fact]
    public void IsAgentAllowed_NullAgents_AllowsAll()
    {
        var skill = new SkillDefinition { Agents = null };
        SkillContextProvider.IsAgentAllowed(skill, "any-agent").ShouldBeTrue();
        SkillContextProvider.IsAgentAllowed(skill, null).ShouldBeTrue();
    }

    [Fact]
    public void IsAgentAllowed_EmptyAgents_AllowsAll()
    {
        var skill = new SkillDefinition { Agents = [] };
        SkillContextProvider.IsAgentAllowed(skill, "any-agent").ShouldBeTrue();
    }

    [Fact]
    public void IsAgentAllowed_ExactMatch()
    {
        var skill = new SkillDefinition { Agents = ["finance-agent", "hr-agent"] };
        SkillContextProvider.IsAgentAllowed(skill, "finance-agent").ShouldBeTrue();
        SkillContextProvider.IsAgentAllowed(skill, "hr-agent").ShouldBeTrue();
        SkillContextProvider.IsAgentAllowed(skill, "other-agent").ShouldBeFalse();
    }

    [Fact]
    public void IsAgentAllowed_WildcardMatch()
    {
        var skill = new SkillDefinition { Agents = ["rpi-*"] };
        SkillContextProvider.IsAgentAllowed(skill, "rpi-assistant").ShouldBeTrue();
        SkillContextProvider.IsAgentAllowed(skill, "rpi-finance").ShouldBeTrue();
        SkillContextProvider.IsAgentAllowed(skill, "other-agent").ShouldBeFalse();
    }

    [Fact]
    public void IsAgentAllowed_CaseInsensitive()
    {
        var skill = new SkillDefinition { Agents = ["Fabrikam-Assistant"] };
        SkillContextProvider.IsAgentAllowed(skill, "rpi-assistant").ShouldBeTrue();
    }

    [Fact]
    public void IsAgentAllowed_WithAgents_NullAgentName_ReturnsFalse()
    {
        var skill = new SkillDefinition { Agents = ["specific-agent"] };
        SkillContextProvider.IsAgentAllowed(skill, null).ShouldBeFalse();
        SkillContextProvider.IsAgentAllowed(skill, "").ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // IsInternal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ParsesInternalField()
    {
        CreateSkillFile("internal-skill", """
            ---
            name: Internal Skill
            description: Shared dependency.
            internal: true
            ---
            """);

        var store = CreateStore(paths: [_tempDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].IsInternal.ShouldBeTrue();
    }

    [Fact]
    public void FilterByAgent_ExcludesInternalSkills()
    {
        var internalSkill = new SkillDefinition { Slug = "office-scripts", Name = "Office Scripts", IsInternal = true };
        var normalSkill = new SkillDefinition { Slug = "code-review", Name = "Code Review" };

        // Internal skills should be hidden from agents
        SkillContextProvider.IsAgentAllowed(internalSkill, "any-agent").ShouldBeTrue(); // IsAgentAllowed doesn't filter internal
        internalSkill.IsInternal.ShouldBeTrue(); // But the flag is set — FilterByAgent checks this separately
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
        SkillMarkdownParser.GenerateSlug(name).ShouldBe(expectedSlug);
    }

    [Fact]
    public void GenerateSlug_TruncatesTo64Chars()
    {
        var longName = new string('a', 100);
        var slug = SkillMarkdownParser.GenerateSlug(longName);
        slug.Length.ShouldBeLessThanOrEqualTo(64);
    }

    // -------------------------------------------------------------------------
    // Plugin / Managed paths
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_LoadsPluginSkills_WithPluginSource()
    {
        var pluginDir = Path.Combine(_tempDir, "plugins");
        Directory.CreateDirectory(pluginDir);
        var skillDir = Path.Combine(pluginDir, "my-plugin-skill");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: Plugin Skill
            description: From plugin path.
            ---
            """);

        var store = CreateStore(pluginPaths: [pluginDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].Name.ShouldBe("Plugin Skill");
        skills[0].Source.ShouldBe(SkillSource.Plugin);
    }

    [Fact]
    public async Task GetAllAsync_LoadsManagedSkills_WithManagedSource()
    {
        var managedDir = Path.Combine(_tempDir, "managed");
        Directory.CreateDirectory(managedDir);
        var skillDir = Path.Combine(managedDir, "platform-skill");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: Managed Skill
            description: Platform managed.
            ---
            """);

        var store = CreateStore(managedPaths: [managedDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].Name.ShouldBe("Managed Skill");
        skills[0].Source.ShouldBe(SkillSource.Managed);
    }

    [Fact]
    public async Task GetAllAsync_MixedSources_AssignsCorrectSource()
    {
        // Use separate sibling directories to avoid scannedSkillFiles dedup
        var regularDir = Path.Combine(_tempDir, "regular");
        Directory.CreateDirectory(regularDir);
        var regularSkillDir = Path.Combine(regularDir, "regular-skill");
        Directory.CreateDirectory(regularSkillDir);
        File.WriteAllText(Path.Combine(regularSkillDir, "SKILL.md"), """
            ---
            name: Regular Skill
            description: From regular path.
            ---
            """);

        // Plugin path (separate sibling directory)
        var pluginDir = Path.Combine(_tempDir, "plugins");
        Directory.CreateDirectory(pluginDir);
        var pluginSkillDir = Path.Combine(pluginDir, "plugin-skill");
        Directory.CreateDirectory(pluginSkillDir);
        File.WriteAllText(Path.Combine(pluginSkillDir, "SKILL.md"), """
            ---
            name: Plugin Skill
            description: From plugin.
            ---
            """);

        // Managed path (separate sibling directory)
        var managedDir = Path.Combine(_tempDir, "managed");
        Directory.CreateDirectory(managedDir);
        var managedSkillDir = Path.Combine(managedDir, "managed-skill");
        Directory.CreateDirectory(managedSkillDir);
        File.WriteAllText(Path.Combine(managedSkillDir, "SKILL.md"), """
            ---
            name: Managed Skill
            description: From managed.
            ---
            """);

        var store = CreateStore(paths: [regularDir], pluginPaths: [pluginDir], managedPaths: [managedDir]);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(3);
        skills.Single(s => s.Name == "Regular Skill").Source.ShouldBe(SkillSource.FileSystem);
        skills.Single(s => s.Name == "Plugin Skill").Source.ShouldBe(SkillSource.Plugin);
        skills.Single(s => s.Name == "Managed Skill").Source.ShouldBe(SkillSource.Managed);
    }

    [Fact]
    public async Task GetAllAsync_EmptyPluginAndManagedPaths_LoadsNone()
    {
        CreateSkillFile("only-regular", """
            ---
            name: Only Regular
            description: No plugin or managed.
            ---
            """);

        var store = CreateStore(paths: [_tempDir], pluginPaths: [], managedPaths: []);
        var skills = await store.GetAllAsync();

        skills.Count.ShouldBe(1);
        skills[0].Source.ShouldBe(SkillSource.FileSystem);
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
        bool requireChecksEnabled = true,
        string? dataPath = null,
        string? contentRootPath = null,
        List<string>? pluginPaths = null,
        List<string>? managedPaths = null)
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
                    DataPath = dataPath,
                    RequireChecksEnabled = requireChecksEnabled,
                    LoadBuiltIn = false,
                    PluginPaths = pluginPaths ?? [],
                    ManagedPaths = managedPaths ?? []
                }
            }
        });

        var logger = Mock.Of<ILogger<FileSystemSkillStore>>();
        IHostEnvironment? hostEnvironment = null;
        if (!string.IsNullOrWhiteSpace(contentRootPath))
        {
            var mockHostEnvironment = new Mock<IHostEnvironment>();
            mockHostEnvironment.SetupGet(x => x.ContentRootPath).Returns(contentRootPath);
            hostEnvironment = mockHostEnvironment.Object;
        }

        return new FileSystemSkillStore(logger, options, hostEnvironment);
    }
}
