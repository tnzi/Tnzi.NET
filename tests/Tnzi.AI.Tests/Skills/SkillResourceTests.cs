using System.Reflection;

namespace Tnzi.AI.Tests.Skills;

public class SkillResourceTests
{
    private static readonly string[] Resources = typeof(FileSystemSkillStore).Assembly
        .GetManifestResourceNames();

    // 统一路径分隔符（Windows 上 %(RecursiveDir) 使用反斜杠）
    private static readonly string[] NormalizedResources = Resources
        .Select(r => r.Replace('\\', '/'))
        .ToArray();

    [Fact]
    public void EmbeddedResources_UseLogicalNames()
    {
        // LogicalName 格式: BuiltInSkill:{slug}/{path}
        NormalizedResources.ShouldContain(r => r.StartsWith("BuiltInSkill:"));
        NormalizedResources.ShouldContain(r => r == "BuiltInSkill:deep-research/SKILL.md");
    }

    [Fact]
    public void EmbeddedResources_PreserveOriginalPaths()
    {
        // chart-visualization 应保留连字符（不被转为下划线）
        NormalizedResources.ShouldContain(r =>
            r.Contains("chart-visualization/references/generate_bar_chart.md"));
    }

    [Fact]
    public void EmbeddedResources_IncludeScriptsAndReferences()
    {
        var builtInResources = NormalizedResources
            .Where(r => r.StartsWith("BuiltInSkill:"))
            .ToList();

        // 应有 SKILL.md 和附属资源
        builtInResources.Count.ShouldBeGreaterThan(30); // 30 SKILL.md + 44 附属资源
        builtInResources.ShouldContain(r => r.Contains("/scripts/")); // Python 脚本
        builtInResources.ShouldContain(r => r.Contains("/references/")); // 参考文档
    }

    [Fact]
    public void LoadBuiltInSkills_GroupsResourcesBySlug()
    {
        var chartResources = NormalizedResources
            .Where(r => r.StartsWith("BuiltInSkill:chart-visualization/"))
            .ToList();

        // chart-visualization 应有 1 个 SKILL.md + 26 个 references
        chartResources.Count.ShouldBeGreaterThanOrEqualTo(27);
        chartResources.ShouldContain("BuiltInSkill:chart-visualization/SKILL.md");
    }
}
