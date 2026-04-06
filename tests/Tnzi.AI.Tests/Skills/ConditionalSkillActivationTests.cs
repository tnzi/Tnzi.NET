using Tnzi.AI.Infrastructure.ContextProviders;
using Tnzi.AI.Skills.Models;

namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// 条件激活 — 资源路径 glob 匹配测试
/// </summary>
public class ConditionalSkillActivationTests
{
    #region GlobMatch — Exact Match

    [Fact]
    public void GlobMatch_ExactMatch_ReturnsTrue()
    {
        SkillContextProvider.GlobMatch("api/users", "api/users").ShouldBeTrue();
    }

    [Fact]
    public void GlobMatch_ExactMatch_CaseInsensitive()
    {
        SkillContextProvider.GlobMatch("Api/Users", "api/users").ShouldBeTrue();
    }

    #endregion

    #region GlobMatch — Star Wildcard

    [Fact]
    public void GlobMatch_StarWildcard_MatchesAnyFile()
    {
        SkillContextProvider.GlobMatch("foo.cs", "*.cs").ShouldBeTrue();
    }

    [Fact]
    public void GlobMatch_StarWildcard_MatchesInDirectory()
    {
        SkillContextProvider.GlobMatch("src/Program.cs", "src/*.cs").ShouldBeTrue();
    }

    [Fact]
    public void GlobMatch_StarWildcard_DoesNotCrossDirectoryBoundary()
    {
        // * 只匹配单层，不跨目录
        SkillContextProvider.GlobMatch("src/Tnzi/Program.cs", "src/*.cs").ShouldBeFalse();
    }

    [Fact]
    public void GlobMatch_StarWildcard_MatchesPrefix()
    {
        SkillContextProvider.GlobMatch("src/MyController.cs", "src/My*.cs").ShouldBeTrue();
    }

    #endregion

    #region GlobMatch — Double Star Wildcard

    [Fact]
    public void GlobMatch_DoubleStarWildcard_MatchesRecursive()
    {
        SkillContextProvider.GlobMatch("src/Tnzi/AI/Tools/ToolScanner.cs", "src/**/*.cs").ShouldBeTrue();
    }

    [Fact]
    public void GlobMatch_DoubleStarWildcard_MatchesZeroLevels()
    {
        // ** 可以匹配零个段
        SkillContextProvider.GlobMatch("src/File.cs", "src/**/*.cs").ShouldBeTrue();
    }

    [Fact]
    public void GlobMatch_DoubleStarWildcard_MatchesDeepNesting()
    {
        SkillContextProvider.GlobMatch("a/b/c/d/e/f.txt", "a/**/*.txt").ShouldBeTrue();
    }

    [Fact]
    public void GlobMatch_DoubleStarWildcard_AloneMatchesAll()
    {
        SkillContextProvider.GlobMatch("any/path/file.txt", "**").ShouldBeTrue();
    }

    #endregion

    #region GlobMatch — No Match

    [Fact]
    public void GlobMatch_NoMatch_ReturnsFalse()
    {
        SkillContextProvider.GlobMatch("foo.txt", "*.cs").ShouldBeFalse();
    }

    [Fact]
    public void GlobMatch_DifferentPath_ReturnsFalse()
    {
        SkillContextProvider.GlobMatch("api/products", "api/users").ShouldBeFalse();
    }

    [Fact]
    public void GlobMatch_BackslashNormalization()
    {
        // 反斜杠应被统一为正斜杠后匹配
        SkillContextProvider.GlobMatch("src\\Tnzi\\File.cs", "src/**/*.cs").ShouldBeTrue();
    }

    #endregion

    #region MatchesResourcePaths

    [Fact]
    public void MatchesResourcePaths_EmptyPaths_ReturnsFalse()
    {
        var skill = new SkillDefinition
        {
            Slug = "test",
            Paths = []
        };

        SkillContextProvider.MatchesResourcePaths(skill, ["src/file.cs"]).ShouldBeFalse();
    }

    [Fact]
    public void MatchesResourcePaths_MatchingPath_ReturnsTrue()
    {
        var skill = new SkillDefinition
        {
            Slug = "csharp-review",
            Paths = ["src/**/*.cs", "*.csproj"]
        };

        SkillContextProvider.MatchesResourcePaths(skill, ["src/Tnzi/AI/Agent.cs"]).ShouldBeTrue();
    }

    [Fact]
    public void MatchesResourcePaths_NoMatch_ReturnsFalse()
    {
        var skill = new SkillDefinition
        {
            Slug = "python-review",
            Paths = ["**/*.py"]
        };

        SkillContextProvider.MatchesResourcePaths(skill, ["src/Program.cs", "README.md"]).ShouldBeFalse();
    }

    [Fact]
    public void MatchesResourcePaths_MultipleResourcePaths_MatchesAny()
    {
        var skill = new SkillDefinition
        {
            Slug = "api-skill",
            Paths = ["api/users"]
        };

        SkillContextProvider.MatchesResourcePaths(skill, ["api/products", "api/users"]).ShouldBeTrue();
    }

    [Fact]
    public void MatchesResourcePaths_EmptyResourcePaths_ReturnsFalse()
    {
        var skill = new SkillDefinition
        {
            Slug = "test",
            Paths = ["**/*.cs"]
        };

        SkillContextProvider.MatchesResourcePaths(skill, []).ShouldBeFalse();
    }

    #endregion
}
