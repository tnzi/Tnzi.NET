using System.IO;

namespace Tnzi.AI.Tests;

/// <summary>
/// FileSystemTools 路径安全与文件操作单元测试
/// </summary>
public class FileSystemToolsTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tnzi-test-{Guid.NewGuid():N}");
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
                MaxFileReadSize = 1024 // 1KB for testing
            }
        });

    private FileSystemTools CreateTools(string? projectRoot = null)
    {
        var root = projectRoot ?? _tempDir;
        var pathValidator = new PathValidator(CreateOptions(root), Mock.Of<ILogger<PathValidator>>());
        return new FileSystemTools(pathValidator, CreateOptions(root), Mock.Of<ILogger<FileSystemTools>>());
    }

    [Fact]
    public async Task ReadFile_PathOutsideAllowed_ReturnsError()
    {
        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "Path outside allowed directories" });

        var tools = new FileSystemTools(pathValidator.Object, CreateOptions(_tempDir),
            Mock.Of<ILogger<FileSystemTools>>());

        var result = await tools.ReadFileAsync("/etc/passwd");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("outside");
    }

    [Fact]
    public async Task WriteFile_AndReadBack_Succeeds()
    {
        var tools = CreateTools();
        var filePath = Path.Combine(_tempDir, "test.txt");

        var writeResult = await tools.WriteFileAsync(filePath, "Hello World");
        var json = JsonSerializer.Serialize(writeResult);
        json.ShouldContain("success");

        var readResult = await tools.ReadFileAsync(filePath);
        var readJson = JsonSerializer.Serialize(readResult);
        readJson.ShouldContain("Hello World");
    }

    [Fact]
    public async Task EditFile_UniqueMatch_Succeeds()
    {
        var tools = CreateTools();
        var filePath = Path.Combine(_tempDir, "edit-test.txt");
        await File.WriteAllTextAsync(filePath, "Hello World\nGoodbye World");

        var result = await tools.EditFileAsync(filePath, "Hello World", "Hi World");
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("success");

        var content = await File.ReadAllTextAsync(filePath);
        content.ShouldContain("Hi World");
        content.ShouldContain("Goodbye World");
    }

    [Fact]
    public async Task DeleteFile_ExistingFile_Succeeds()
    {
        var tools = CreateTools();
        var filePath = Path.Combine(_tempDir, "delete-me.txt");
        await File.WriteAllTextAsync(filePath, "temp");

        var result = await tools.DeleteFilesAsync(filePath);
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("success");

        File.Exists(filePath).ShouldBeFalse();
    }

    [Fact]
    public async Task CopyFile_Succeeds()
    {
        var tools = CreateTools();
        var srcPath = Path.Combine(_tempDir, "source.txt");
        var dstPath = Path.Combine(_tempDir, "dest.txt");
        await File.WriteAllTextAsync(srcPath, "copy content");

        var result = await tools.CopyFileAsync(srcPath, dstPath);
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("success");

        var content = await File.ReadAllTextAsync(dstPath);
        content.ShouldBe("copy content");
    }

    [Fact]
    public async Task GlobAsync_FindsFiles()
    {
        var tools = CreateTools();
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "file1.cs"), "// C# file");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "file2.cs"), "// C# file");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "file3.txt"), "text file");

        var result = await tools.GlobAsync("*.cs", _tempDir, respectGitignore: false);
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("file1.cs");
        json.ShouldContain("file2.cs");
        json.ShouldNotContain("file3.txt");
    }

    [Fact]
    public async Task ListDirectory_ReturnsContents()
    {
        var tools = CreateTools();
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "list-test.txt"), "content");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));

        var result = await tools.ListDirectoryAsync(_tempDir);
        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("list-test.txt");
        json.ShouldContain("subdir");
    }

    [Fact]
    public async Task ReadFile_DeniedPattern_ReturnsError()
    {
        var pathValidator = new Mock<IPathValidator>();
        pathValidator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
            .ReturnsAsync(new PathValidationResult { IsValid = false, Error = "File matches denied pattern" });

        var tools = new FileSystemTools(pathValidator.Object, CreateOptions(_tempDir),
            Mock.Of<ILogger<FileSystemTools>>());

        var result = await tools.ReadFileAsync("/project/.env");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("denied");
    }
}
