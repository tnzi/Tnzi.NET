using Tnzi.AI.Sandbox.Tools;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// SandboxTools 单元测试 — 工具方法只接收 LLM 参数，
/// 沙箱环境（ISandbox + threadId）经 IAgentExecutionContextAccessor（AsyncLocal）解析。
/// </summary>
public class SandboxToolsTests : IAsyncLifetime
{
    private string _workDir = null!;
    private LocalSandbox _sandbox = null!;
    private VirtualPathTranslator _translator = null!;
    private SandboxTools _tools = null!;
    private readonly AgentExecutionContextAccessor _accessor = new();
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
        _tools = new SandboxTools(_translator, NullLogger<SandboxTools>.Instance, _accessor);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _sandbox.DisposeAsync();
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    /// <summary>
    /// 在当前测试方法的异步流上发布沙箱环境（模拟 SandboxMiddleware 的发布动作）。
    /// 必须在测试方法体内调用 — AsyncLocal 值不会从 InitializeAsync 流到测试方法。
    /// </summary>
    private void SeedEnvironment()
    {
        _accessor.Properties[SandboxPropertyKeys.ToolEnvironment] =
            new SandboxToolEnvironment(_sandbox, _threadId);
    }

    [Fact]
    public async Task WriteAndRead_RoundTrips()
    {
        SeedEnvironment();

        var writeResult = await _tools.WriteFileAsync("/mnt/workspace/test.txt", "hello world");
        Assert.Contains("success", writeResult.ToString()!, StringComparison.OrdinalIgnoreCase);

        var readResult = await _tools.ReadFileAsync("/mnt/workspace/test.txt");
        Assert.Contains("hello world", readResult.ToString()!);
    }

    [Fact]
    public async Task Bash_Echo_ReturnsOutput()
    {
        SeedEnvironment();

        var result = await _tools.BashAsync("echo test123");
        Assert.Contains("test123", result.ToString()!);
    }

    [Fact]
    public async Task ListDirectory_ReturnsEntries()
    {
        SeedEnvironment();

        await _tools.WriteFileAsync("/mnt/workspace/file1.txt", "a");
        var result = await _tools.ListDirectoryAsync("/mnt/workspace");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("file1.txt", json);
    }

    [Fact]
    public async Task StrReplace_ReplacesContent()
    {
        SeedEnvironment();

        await _tools.WriteFileAsync("/mnt/workspace/replace.txt", "hello world");
        var result = await _tools.StrReplaceAsync("/mnt/workspace/replace.txt", "world", "earth");
        Assert.Contains("success", result.ToString()!, StringComparison.OrdinalIgnoreCase);

        var content = await _tools.ReadFileAsync("/mnt/workspace/replace.txt");
        Assert.Contains("hello earth", content.ToString()!);
    }

    // -------------------------------------------------------------------------
    // 环境缺失 — 工具必须返回结构化错误而不是抛异常
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AllTools_WithoutEnvironment_ReturnStructuredError()
    {
        // 不调用 SeedEnvironment() — 模拟中间件未运行/子代理裸执行器路径
        var results = new[]
        {
            await _tools.BashAsync("echo nope"),
            await _tools.ListDirectoryAsync("/mnt/workspace"),
            await _tools.ReadFileAsync("/mnt/workspace/x.txt"),
            await _tools.WriteFileAsync("/mnt/workspace/x.txt", "content"),
            await _tools.StrReplaceAsync("/mnt/workspace/x.txt", "a", "b")
        };

        foreach (var result in results)
        {
            Assert.Contains("Sandbox is not available", result.ToString()!);
        }
    }

    [Fact]
    public async Task Tools_WithoutAccessor_ReturnStructuredError()
    {
        // 构造时根本没有 accessor（DI 中未注册 IAgentExecutionContextAccessor 的极端情况）
        var tools = new SandboxTools(_translator, NullLogger<SandboxTools>.Instance);

        var result = await tools.BashAsync("echo nope");

        Assert.Contains("Sandbox is not available", result.ToString()!);
    }

    [Fact]
    public async Task Bash_EmptyCommand_ReturnsStructuredError()
    {
        SeedEnvironment();

        var result = await _tools.BashAsync("   ");

        Assert.Contains("Command cannot be empty", result.ToString()!);
    }

    [Fact]
    public async Task ReadFile_PathTraversal_ReturnsStructuredError_NotException()
    {
        SeedEnvironment();

        // VirtualPathTranslator 对 .. 路径穿越抛异常 — 工具必须转换为结构化错误
        var result = await _tools.ReadFileAsync("/mnt/workspace/../../etc/passwd");

        Assert.Contains("read_file failed", result.ToString()!);
    }

    [Fact]
    public async Task Bash_PathEscapeViaMntPath_IsDenied()
    {
        SeedEnvironment();

        // The naive /mnt translation turns this into {threadDir}/workspace/../../../etc/passwd,
        // which resolves OUTSIDE the thread directory. The best-effort jail guard
        // must deny it rather than hand it to the host shell verbatim.
        var result = await _tools.BashAsync("cat /mnt/workspace/../../../etc/passwd");
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Contains("Command denied", json);
        Assert.Contains("outside the sandbox thread directory", json);
    }

    [Fact]
    public async Task Bash_NormalMntPath_IsAllowed()
    {
        SeedEnvironment();

        // A well-formed /mnt path stays inside the thread directory — must NOT trip
        // the escape guard. We assert it is NOT denied (the shell `cat`/`type` may
        // differ per OS, so we check the guard let it through rather than output).
        await _tools.WriteFileAsync("/mnt/workspace/ok.txt", "content-here");
        var result = await _tools.BashAsync("echo /mnt/workspace/ok.txt");
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.DoesNotContain("Command denied", json);
        Assert.DoesNotContain("outside the sandbox thread directory", json);
    }
}
