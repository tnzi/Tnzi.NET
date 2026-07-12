using Microsoft.Extensions.Options;
using Tnzi.AI.Infrastructure.ContextProviders;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// 参数化测试：验证所有内置技能 SKILL.md 解析正确。
/// 每个技能作为独立的 Theory case 运行，方便定位失败的具体技能。
/// </summary>
public class BuiltInSkillsValidationTests
{
    private static readonly Lazy<List<SkillDefinition>> AllSkills = new(LoadAllBuiltInSkills);

    // -------------------------------------------------------------------------
    // 获取所有内置技能作为 Theory data
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> AllBuiltInSkills()
    {
        foreach (var skill in AllSkills.Value)
        {
            yield return [skill.Slug, skill];
        }
    }

    public static IEnumerable<object[]> SkillsWithScripts()
    {
        foreach (var skill in AllSkills.Value.Where(s => s.Resources.Any(r => r.Key.StartsWith("scripts/"))))
        {
            yield return [skill.Slug, skill];
        }
    }

    // -------------------------------------------------------------------------
    // 1. 基础解析验证
    // -------------------------------------------------------------------------

    [Fact]
    public void AllBuiltInSkills_LoadAtLeastOne()
    {
        AllSkills.Value.ShouldNotBeEmpty("No built-in skills loaded — check BuiltIn directory path");
    }

    [Theory]
    [MemberData(nameof(AllBuiltInSkills))]
    public void Skill_HasValidName(string slug, SkillDefinition skill)
    {
        skill.Name.ShouldNotBeNullOrWhiteSpace($"Skill '{slug}' missing name");
    }

    [Theory]
    [MemberData(nameof(AllBuiltInSkills))]
    public void Skill_HasValidDescription(string slug, SkillDefinition skill)
    {
        skill.Description.ShouldNotBeNullOrWhiteSpace($"Skill '{slug}' missing description");
        skill.Description.Length.ShouldBeGreaterThan(10, $"Skill '{slug}' description too short");
    }

    [Theory]
    [MemberData(nameof(AllBuiltInSkills))]
    public void Skill_HasValidSlug(string slug, SkillDefinition skill)
    {
        skill.Slug.ShouldNotBeNullOrWhiteSpace($"Skill '{slug}' missing slug");
        // slug 只能包含小写字母、数字、连字符
        skill.Slug.ShouldMatch("^[a-z0-9][a-z0-9\\-]*[a-z0-9]$", $"Skill '{slug}' has invalid slug format");
    }

    [Theory]
    [MemberData(nameof(AllBuiltInSkills))]
    public void Skill_HasContent(string slug, SkillDefinition skill)
    {
        skill.Content.ShouldNotBeNullOrWhiteSpace($"Skill '{slug}' has empty content");
    }

    [Theory]
    [MemberData(nameof(AllBuiltInSkills))]
    public void Skill_HasSystemScope(string slug, SkillDefinition skill)
    {
        skill.Scope.ShouldBe(SkillScope.System, $"Built-in skill '{slug}' should have System scope");
    }

    // -------------------------------------------------------------------------
    // 2. 有 scripts 的技能验证
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(SkillsWithScripts))]
    public void SkillWithScripts_ResourcesNotEmpty(string slug, SkillDefinition skill)
    {
        var scriptResources = skill.Resources.Where(r => r.Key.StartsWith("scripts/")).ToList();
        scriptResources.ShouldNotBeEmpty($"Skill '{slug}' has scripts/ dir but no script resources loaded");

        foreach (var (relPath, content) in scriptResources)
        {
            // __init__.py 是合法的空文件（Python 包标记）
            if (relPath.EndsWith("__init__.py")) continue;

            content.ShouldNotBeNullOrWhiteSpace($"Script '{relPath}' in skill '{slug}' is empty");
            content.Length.ShouldBeGreaterThan(10, $"Script '{relPath}' in skill '{slug}' suspiciously short");
        }
    }

    [Theory]
    [MemberData(nameof(SkillsWithScripts))]
    public void SkillWithScripts_ResourcePathsAreRelative(string slug, SkillDefinition skill)
    {
        slug.ShouldNotBeNullOrWhiteSpace();
        foreach (var relPath in skill.Resources.Keys)
        {
            relPath.ShouldNotStartWith("/");
            relPath.ShouldNotStartWith("\\");
            relPath.ShouldNotContain("\\");
        }
    }

    // -------------------------------------------------------------------------
    // 3. 技能间唯一性验证
    // -------------------------------------------------------------------------

    [Fact]
    public void AllBuiltInSkills_HaveUniqueSlug()
    {
        var slugs = AllSkills.Value.Select(s => s.Slug).ToList();
        var duplicates = slugs.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        duplicates.ShouldBeEmpty($"Duplicate slugs found: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void AllBuiltInSkills_HaveUniqueName()
    {
        var names = AllSkills.Value.Select(s => s.Name).ToList();
        var duplicates = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        duplicates.ShouldBeEmpty($"Duplicate names found: {string.Join(", ", duplicates)}");
    }

    // -------------------------------------------------------------------------
    // 4. 已知技能的特定验证
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("data-analysis", "scripts/analyze.py")]
    [InlineData("chart-visualization", "scripts/generate.js")]
    [InlineData("pdf-processing", "scripts/convert_pdf_to_images.py")]
    [InlineData("xlsx-processing", "scripts/recalc.py")]
    [InlineData("find-skills", "scripts/install-skill.sh")]
    public void KnownSkill_HasExpectedScript(string slug, string expectedScript)
    {
        var skill = AllSkills.Value.FirstOrDefault(s => s.Slug == slug);
        skill.ShouldNotBeNull($"Built-in skill '{slug}' not found");
        skill.Resources.ShouldContainKey(expectedScript, $"Skill '{slug}' missing expected script '{expectedScript}'");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static List<SkillDefinition> LoadAllBuiltInSkills()
    {
        var builtInPath = FindBuiltInSkillsPath();
        var options = new StaticOptionsMonitor<AIOptions>(new AIOptions
        {
            ContextProviders = new ContextProvidersOptions
            {
                Skills = new SkillsOptions
                {
                    Paths = [builtInPath],
                    LoadBuiltIn = false
                }
            }
        });

        var store = new FileSystemSkillStore(
            NullLogger<FileSystemSkillStore>.Instance,
            options);

        return store.GetAllAsync().GetAwaiter().GetResult();
    }

    private static string FindBuiltInSkillsPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src", "Tnzi.AI.Skills", "Skills", "BuiltIn");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Tnzi.AI.Skills", "Skills", "BuiltIn"));
        if (Directory.Exists(fallback)) return fallback;

        throw new DirectoryNotFoundException("Cannot find BuiltIn skills directory");
    }
}
