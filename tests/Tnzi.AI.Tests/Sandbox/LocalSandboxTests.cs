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
}
