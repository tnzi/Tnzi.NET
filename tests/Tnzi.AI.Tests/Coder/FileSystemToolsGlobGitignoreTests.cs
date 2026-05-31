using System.IO;
using System.Text.Json;

namespace Tnzi.AI.Tests.Coder;

/// <summary>
/// FileSystemTools.GlobAsync 的 .gitignore 过滤行为测试。
///
/// 目的：验证 respectGitignore:true 时 GlobAsync 的 .gitignore 过滤行为
/// （现已由共享 IgnorePatternMatcher 支撑），符合正确的 git 语义。
///
/// 覆盖代表性 .gitignore 模式：纯文件名、*.ext 扩展名、dir/ 目录、
/// /rooted 根锚定（/foo 仅匹配根级）、!keep 取反、嵌套 .gitignore（仅向上加载）。
/// </summary>
public class FileSystemToolsGlobGitignoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemToolsGlobGitignoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-glob-gi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static IOptions<CoderOptions> CreateOptions(string projectRoot) =>
        Microsoft.Extensions.Options.Options.Create(new CoderOptions
        {
            ProjectRoot = projectRoot,
            Sandbox = new SandboxOptions
            {
                AllowedDirectories = ["."]
            }
        });

    private FileSystemTools CreateTools()
    {
        var pathValidator = new PathValidator(CreateOptions(_tempDir), Mock.Of<ILogger<PathValidator>>());
        return new FileSystemTools(pathValidator, CreateOptions(_tempDir), Mock.Of<ILogger<FileSystemTools>>());
    }

    private void WriteFile(string relativePath, string content = "x")
    {
        var full = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(full, content);
    }

    private async Task<List<string>> GlobFilesAsync(string pattern)
    {
        var tools = CreateTools();
        var result = await tools.GlobAsync(pattern, _tempDir, respectGitignore: true);
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("files", out var filesEl))
        {
            // 出错或无 files 字段
            return [];
        }

        return filesEl.EnumerateArray()
            .Select(e => e.GetString()!)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public async Task Glob_PlainName_IgnoresExactFile()
    {
        WriteFile(".gitignore", "secret.txt\n");
        WriteFile("secret.txt");
        WriteFile("keep.txt");

        var files = await GlobFilesAsync("**/*.txt");

        files.ShouldContain("keep.txt");
        files.ShouldNotContain("secret.txt");
    }

    [Fact]
    public async Task Glob_Extension_IgnoresMatchingExtension()
    {
        WriteFile(".gitignore", "*.log\n");
        WriteFile("app.log");
        WriteFile("app.txt");

        var files = await GlobFilesAsync("**/*");

        files.ShouldContain("app.txt");
        files.ShouldNotContain("app.log");
    }

    [Fact]
    public async Task Glob_DirPattern_IgnoresDirectoryContents()
    {
        WriteFile(".gitignore", "build/\n");
        WriteFile("build/output.txt");
        WriteFile("src/main.txt");

        var files = await GlobFilesAsync("**/*.txt");

        files.ShouldContain("src/main.txt");
        files.ShouldNotContain("build/output.txt");
    }

    [Fact]
    public async Task Glob_RootedPattern_AnchorsToRoot()
    {
        WriteFile(".gitignore", "/root-only.txt\n");
        WriteFile("root-only.txt");
        WriteFile("nested/root-only.txt");

        var files = await GlobFilesAsync("**/*.txt");

        // 前导 "/" 的模式是根锚定的（git 语义）：仅匹配 .gitignore 所在目录的根级文件，
        // 不匹配子目录中的同名文件。因此根级 root-only.txt 被忽略，
        // 而 nested/root-only.txt 保留。
        files.ShouldNotContain("root-only.txt");
        files.ShouldContain("nested/root-only.txt");
    }

    [Fact]
    public async Task Glob_Negation_ReincludesKeptFile()
    {
        WriteFile(".gitignore", "*.tmp\n!keep.tmp\n");
        WriteFile("scratch.tmp");
        WriteFile("keep.tmp");

        var files = await GlobFilesAsync("**/*.tmp");

        files.ShouldContain("keep.tmp");
        files.ShouldNotContain("scratch.tmp");
    }

    [Fact]
    public async Task Glob_NestedGitignore_BaselineBehavior()
    {
        WriteFile(".gitignore", "top.txt\n");
        WriteFile("top.txt");
        WriteFile("sub/.gitignore", "local.txt\n");
        WriteFile("sub/local.txt");
        WriteFile("sub/keep.txt");

        var files = await GlobFilesAsync("**/*.txt");

        // 基线锁定：当前本地引擎只从搜索目录"向上"加载 .gitignore 直到 ProjectRoot，
        // 不会"向下"加载子目录中的嵌套 .gitignore。因此从根目录 glob 时，
        // 根 .gitignore 的 "top.txt" 生效，但 sub/.gitignore 的 "local.txt" 不生效。
        files.ShouldContain("sub/keep.txt");
        files.ShouldContain("sub/local.txt");
        files.ShouldNotContain("top.txt");
    }
}
