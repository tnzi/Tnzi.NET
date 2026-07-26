using System.Security;

namespace Tnzi.AI.Tests.Sandbox;

public class LocalSandboxTests : IAsyncLifetime
{
    private readonly string _workDir;
    private LocalSandbox _sandbox = null!;

    public LocalSandboxTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"tnzi-sandbox-test-{Guid.NewGuid():N}");
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_workDir);
        _sandbox = new LocalSandbox(
            id: "test-sandbox",
            workspacePath: _workDir,
            commandTimeout: TimeSpan.FromSeconds(10),
            maxOutputSize: 1024,
            deniedCommands: ["rm -rf /"],
            environmentBlacklist: ["SECRET_KEY"]);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _sandbox.DisposeAsync();
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    [Fact]
    public void Id_ReturnsConfiguredId()
    {
        Assert.Equal("test-sandbox", _sandbox.Id);
    }

    [Fact]
    public async Task ExecuteCommand_Echo_ReturnsOutput()
    {
        var result = await _sandbox.ExecuteCommandAsync("echo hello");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public async Task ExecuteCommand_DeniedCommand_ReturnsError()
    {
        var result = await _sandbox.ExecuteCommandAsync("rm -rf /");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("denied", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFile_ThenReadFile_RoundTrips()
    {
        var path = Path.Combine(_workDir, "test.txt");
        await _sandbox.WriteFileAsync(path, "hello world");
        var content = await _sandbox.ReadFileAsync(path);
        Assert.Equal("hello world", content);
    }

    [Fact]
    public async Task WriteFile_Append_AppendsContent()
    {
        var path = Path.Combine(_workDir, "append.txt");
        await _sandbox.WriteFileAsync(path, "line1\n");
        await _sandbox.WriteFileAsync(path, "line2\n", append: true);
        var content = await _sandbox.ReadFileAsync(path);
        Assert.Equal("line1\nline2\n", content);
    }

    [Fact]
    public async Task ListDirectory_ReturnsEntries()
    {
        await _sandbox.WriteFileAsync(Path.Combine(_workDir, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(_workDir, "subdir"));

        var entries = await _sandbox.ListDirectoryAsync(_workDir, maxDepth: 1);
        Assert.True(entries.Count >= 2);
        Assert.Contains(entries, e => e.Name == "a.txt" && !e.IsDirectory);
        Assert.Contains(entries, e => e.Name == "subdir" && e.IsDirectory);
    }

    [Fact]
    public async Task ReadFile_NonExistent_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sandbox.ReadFileAsync(Path.Combine(_workDir, "nonexistent.txt")));
    }

    // -------------------------------------------------------------------------
    // DeniedPatterns - sensitive-file blocklist (P1 fix 2026-06-20)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReadFile_SensitivePattern_ThrowsSecurityException()
    {
        var sandbox = new LocalSandbox(
            id: "patterns",
            workspacePath: _workDir,
            commandTimeout: TimeSpan.FromSeconds(10),
            maxOutputSize: 1024,
            deniedPatterns: [".env", "*.key", "*.pem", "credentials*"]);

        var envPath = Path.Combine(_workDir, ".env");
        await File.WriteAllTextAsync(envPath, "SECRET=abc");

        var ex = await Assert.ThrowsAsync<SecurityException>(() => sandbox.ReadFileAsync(envPath));
        Assert.Contains("sensitive pattern", ex.Message);
    }

    [Fact]
    public async Task ReadFile_WildcardPattern_ThrowsSecurityException()
    {
        var sandbox = new LocalSandbox(
            id: "patterns",
            workspacePath: _workDir,
            commandTimeout: TimeSpan.FromSeconds(10),
            maxOutputSize: 1024,
            deniedPatterns: ["*.pem"]);

        var pemPath = Path.Combine(_workDir, "server.pem");
        await File.WriteAllTextAsync(pemPath, "-----BEGIN-----");

        await Assert.ThrowsAsync<SecurityException>(() => sandbox.ReadFileAsync(pemPath));
    }

    [Fact]
    public async Task ListDirectory_HidesSensitiveFiles()
    {
        var sandbox = new LocalSandbox(
            id: "patterns",
            workspacePath: _workDir,
            commandTimeout: TimeSpan.FromSeconds(10),
            maxOutputSize: 1024,
            deniedPatterns: [".env", "*.key"]);

        await File.WriteAllTextAsync(Path.Combine(_workDir, ".env"), "x");
        await File.WriteAllTextAsync(Path.Combine(_workDir, "id_rsa.key"), "x");
        await File.WriteAllTextAsync(Path.Combine(_workDir, "readme.txt"), "x");

        var entries = await sandbox.ListDirectoryAsync(_workDir, maxDepth: 1);

        Assert.Contains(entries, e => e.Name == "readme.txt");
        Assert.DoesNotContain(entries, e => e.Name == ".env");
        Assert.DoesNotContain(entries, e => e.Name == "id_rsa.key");
    }

    [Fact]
    public async Task ReadFile_NonSensitive_StillReadable()
    {
        var sandbox = new LocalSandbox(
            id: "patterns",
            workspacePath: _workDir,
            commandTimeout: TimeSpan.FromSeconds(10),
            maxOutputSize: 1024,
            deniedPatterns: [".env", "*.key"]);

        var path = Path.Combine(_workDir, "notes.txt");
        await File.WriteAllTextAsync(path, "ok");

        Assert.Equal("ok", await sandbox.ReadFileAsync(path));
    }

    // -------------------------------------------------------------------------
    // MaxFileSizeBytes - size precheck + streaming line slicing (P1 fix 2026-06-20)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReadFile_ExceedingMaxSize_ThrowsSecurityException()
    {
        var sandbox = new LocalSandbox(
            id: "sized",
            workspacePath: _workDir,
            commandTimeout: TimeSpan.FromSeconds(10),
            maxOutputSize: 1024,
            maxFileSize: 16);

        var path = Path.Combine(_workDir, "big.txt");
        await File.WriteAllTextAsync(path, new string('a', 64));

        var ex = await Assert.ThrowsAsync<SecurityException>(() => sandbox.ReadFileAsync(path));
        Assert.Contains("maximum readable size", ex.Message);
    }

    [Fact]
    public async Task ReadFile_UnderMaxSize_Succeeds()
    {
        var sandbox = new LocalSandbox(
            id: "sized",
            workspacePath: _workDir,
            commandTimeout: TimeSpan.FromSeconds(10),
            maxOutputSize: 1024,
            maxFileSize: 1024);

        var path = Path.Combine(_workDir, "small.txt");
        await File.WriteAllTextAsync(path, "tiny");

        Assert.Equal("tiny", await sandbox.ReadFileAsync(path));
    }

    [Fact]
    public async Task ReadFile_OffsetAndLimit_SlicesLines()
    {
        var path = Path.Combine(_workDir, "lines.txt");
        await File.WriteAllTextAsync(path, "l1\nl2\nl3\nl4\nl5\n");

        // 1-based offset=2, limit=2 → lines 2 and 3.
        var content = await _sandbox.ReadFileAsync(path, offset: 2, limit: 2);

        Assert.Equal("l2\nl3", content);
    }

    [Fact]
    public async Task ReadFile_OffsetOnly_SlicesToEnd()
    {
        var path = Path.Combine(_workDir, "lines2.txt");
        await File.WriteAllTextAsync(path, "a\nb\nc\n");

        var content = await _sandbox.ReadFileAsync(path, offset: 2);

        Assert.Equal("b\nc", content);
    }
}
