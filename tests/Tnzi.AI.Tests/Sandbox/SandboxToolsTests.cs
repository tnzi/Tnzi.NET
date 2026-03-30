using Tnzi.AI.Sandbox.Tools;

namespace Tnzi.AI.Tests.Sandbox;

public class SandboxToolsTests : IAsyncLifetime
{
    private string _workDir = null!;
    private LocalSandbox _sandbox = null!;
    private VirtualPathTranslator _translator = null!;
    private SandboxTools _tools = null!;
    private readonly Guid _threadId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"tnzi-tools-test-{Guid.NewGuid():N}");
        var threadDir = Path.Combine(_workDir, _threadId.ToString("N"));
        Directory.CreateDirectory(Path.Combine(threadDir, "workspace"));
        Directory.CreateDirectory(Path.Combine(threadDir, "uploads"));
        Directory.CreateDirectory(Path.Combine(threadDir, "outputs"));

        _translator = new VirtualPathTranslator(_workDir);
        _sandbox = new LocalSandbox("test", Path.Combine(threadDir, "workspace"),
            TimeSpan.FromSeconds(10), 1024);
        _tools = new SandboxTools(_translator, NullLogger<SandboxTools>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _sandbox.DisposeAsync();
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    [Fact]
    public async Task WriteAndRead_RoundTrips()
    {
        var writeResult = await _tools.WriteFileAsync(_sandbox, _threadId, "/mnt/workspace/test.txt", "hello world");
        Assert.Contains("success", writeResult.ToString()!, StringComparison.OrdinalIgnoreCase);

        var readResult = await _tools.ReadFileAsync(_sandbox, _threadId, "/mnt/workspace/test.txt");
        Assert.Contains("hello world", readResult.ToString()!);
    }

    [Fact]
    public async Task Bash_Echo_ReturnsOutput()
    {
        var result = await _tools.BashAsync(_sandbox, _threadId, "echo test123");
        Assert.Contains("test123", result.ToString()!);
    }

    [Fact]
    public async Task ListDirectory_ReturnsEntries()
    {
        await _tools.WriteFileAsync(_sandbox, _threadId, "/mnt/workspace/file1.txt", "a");
        var result = await _tools.ListDirectoryAsync(_sandbox, _threadId, "/mnt/workspace");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("file1.txt", json);
    }

    [Fact]
    public async Task StrReplace_ReplacesContent()
    {
        await _tools.WriteFileAsync(_sandbox, _threadId, "/mnt/workspace/replace.txt", "hello world");
        var result = await _tools.StrReplaceAsync(_sandbox, _threadId, "/mnt/workspace/replace.txt", "world", "earth");
        Assert.Contains("success", result.ToString()!, StringComparison.OrdinalIgnoreCase);

        var content = await _tools.ReadFileAsync(_sandbox, _threadId, "/mnt/workspace/replace.txt");
        Assert.Contains("hello earth", content.ToString()!);
    }
}
