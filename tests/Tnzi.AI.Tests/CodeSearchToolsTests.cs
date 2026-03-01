using System.IO;

namespace Tnzi.AI.Tests;

/// <summary>
/// CodeSearchTools 搜索安全与功能单元测试
/// </summary>
public class CodeSearchToolsTests : IDisposable
{
    private readonly string _tempDir;

    public CodeSearchToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private static IOptions<CoderOptions> CreateOptions(string projectRoot) =>
        Microsoft.Extensions.Options.Options.Create(new CoderOptions
        {
            ProjectRoot = projectRoot,
            Sandbox = new SandboxOptions
            {
                AllowedDirectories = ["."],
                MaxFileReadSize = 10_485_760
            }
        });

    private CodeSearchTools CreateTools(string? projectRoot = null)
    {
        var root = projectRoot ?? _tempDir;
        var pathValidator = new PathValidator(CreateOptions(root), Mock.Of<ILogger<PathValidator>>());
        return new CodeSearchTools(pathValidator, CreateOptions(root), Mock.Of<ILogger<CodeSearchTools>>());
    }

    [Fact]
    public async Task Grep_InvalidRegex_ReturnsError()
    {
        var tools = CreateTools();

        var result = await tools.GrepAsync("[invalid", _tempDir);

        var json = JsonSerializer.Serialize(result);
        // 无效正则应返回错误而非崩溃
        json.ShouldContain("error");
    }

    [Fact]
    public async Task Grep_PathOutsideAllowed_ReturnsError()
    {
        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync("/forbidden"))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "Path not allowed" });

        var tools = new CodeSearchTools(pathValidator.Object, CreateOptions(_tempDir),
            Mock.Of<ILogger<CodeSearchTools>>());

        var result = await tools.GrepAsync("test", "/forbidden");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("not allowed");
    }

    [Fact]
    public async Task Grep_FindsMatches_InTempDirectory()
    {
        var tools = CreateTools();

        // 创建测试文件
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "sample.cs"), """
            public class HelloWorld
            {
                public void SayHello() => Console.WriteLine("Hello!");
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "other.txt"), "No match here");

        var result = await tools.GrepAsync("HelloWorld", _tempDir, respectGitignore: false);
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("HelloWorld");
        json.ShouldContain("sample.cs");
    }

    [Fact]
    public async Task Grep_EmptyPattern_ReturnsEmptyOrError()
    {
        var tools = CreateTools();

        var result = await tools.GrepAsync("  ", _tempDir);

        var json = JsonSerializer.Serialize(result);
        // 空模式可能返回 error 或空结果（取决于实现），不应崩溃
        (json.Contains("error") || json.Contains("\"count\":0")).ShouldBeTrue();
    }

    [Fact]
    public async Task Grep_NoMatches_ReturnsEmptyResult()
    {
        var tools = CreateTools();
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "no-match.txt"), "Hello World");

        var result = await tools.GrepAsync("ZZZNonExistent", _tempDir, respectGitignore: false);
        var json = JsonSerializer.Serialize(result);

        // 应成功返回但无匹配
        json.ShouldContain("count");
    }

    [Fact]
    public async Task Grep_GlobFilter_OnlySearchesMatchingFiles()
    {
        var tools = CreateTools();
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "match.cs"), "target_string");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "skip.txt"), "target_string");

        var result = await tools.GrepAsync("target_string", _tempDir, glob: "*.cs", respectGitignore: false);
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("match.cs");
        json.ShouldNotContain("skip.txt");
    }
}
