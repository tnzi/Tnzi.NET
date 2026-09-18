using System.Security;
using Tnzi.AI.Sandbox.Tools;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// Docker provider 的容器侧路径视图：<c>SandboxTools</c> 交给 <c>DockerSandbox</c> 的必须是
/// 容器里的路径（线程目录挂在 <c>/workspace</c>），不是宿主的物理路径。
/// </summary>
/// <remarks>
/// <para>
/// 此前 <c>ToPhysical</c> 出来的宿主路径（<c>{DataRoot}/{tid}/workspace/a.txt</c>，Windows 宿主上甚至是
/// <c>D:\...</c>）被原样写进 <c>cat '...'</c> / <c>find '...'</c> / <c>mkdir -p '...'</c> 在容器里执行，
/// 而容器的文件系统里只有 <c>/workspace</c> —— Docker provider 从来没有服务过一次文件工具调用。
/// </para>
/// <para>
/// 宿主侧的围栏（<c>ToPhysical</c> 解析链接后的越界判定、bash 的 token 守卫）都保留：映射发生在围栏之后，
/// 只换根，不放宽任何判定。
/// </para>
/// </remarks>
public class SandboxToolsDockerPathViewTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly Guid _threadId = Guid.NewGuid();
    private readonly VirtualPathTranslator _translator;
    private readonly string _threadDir;

    public SandboxToolsDockerPathViewTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"tnzi-docker-view-{Guid.NewGuid():N}");
        _translator = new VirtualPathTranslator(_dataRoot);
        _translator.EnsureThreadDirectories(_threadId);
        _threadDir = _translator.GetThreadDirectory(_threadId);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dataRoot)) Directory.Delete(_dataRoot, recursive: true); } catch { /* best effort */ }
    }

    // ── MapPath ────────────────────────────────────────────────────────────

    [Fact]
    public void MapPath_HostThreadPath_BecomesContainerPath()
    {
        var sandbox = CreateSandbox(new MockDockerHandler());

        sandbox.MapPath(Path.Combine(_threadDir, "workspace", "a.txt")).ShouldBe("/workspace/workspace/a.txt");
        sandbox.MapPath(Path.Combine(_threadDir, "skills", "x", "scripts", "y.py")).ShouldBe("/workspace/skills/x/scripts/y.py");
        sandbox.MapPath(_threadDir).ShouldBe("/workspace");
        sandbox.MapPath(_threadDir + Path.DirectorySeparatorChar).ShouldBe("/workspace");
    }

    [Fact]
    public void MapPath_OutsideThreadDir_Throws()
    {
        var sandbox = CreateSandbox(new MockDockerHandler());

        Should.Throw<SecurityException>(() => sandbox.MapPath(Path.Combine(_dataRoot, "other", "a.txt")));
        Should.Throw<SecurityException>(() => sandbox.MapPath(Path.Combine(_threadDir, "..", "a.txt")));
        // 同前缀不同目录：{threadDir}-evil 不是 {threadDir} 的子路径。
        Should.Throw<SecurityException>(() => sandbox.MapPath(_threadDir + "-evil" + Path.DirectorySeparatorChar + "a.txt"));
    }

    [Fact]
    public void MapPath_WithoutHostView_IsIdentity()
    {
        // 直接构造、没有宿主视图的沙箱（既有测试的形态）：没有东西可映射，原样返回。
        var httpClient = new HttpClient(new MockDockerHandler()) { BaseAddress = new Uri("http://localhost/v1.45") };
        var sandbox = new DockerSandbox("docker-test", httpClient, "container-123", "/workspace",
            TimeSpan.FromSeconds(30), 512 * 1024, NullLogger.Instance);

        sandbox.MapPath("/workspace/test.txt").ShouldBe("/workspace/test.txt");
    }

    // ── SandboxTools + DockerSandbox ───────────────────────────────────────

    [Fact]
    public async Task ReadFile_SendsContainerPath()
    {
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "hello");
        var tools = CreateTools(CreateSandbox(handler));

        var result = JsonSerializer.Serialize(await tools.ReadFileAsync("/mnt/workspace/a.txt"));

        result.ShouldContain("hello");
        var command = ExtractExecCommands(handler).Single();
        command.ShouldContain("cat ");
        command.ShouldContain("/workspace/workspace/a.txt");
        command.ShouldNotContain(_dataRoot);
    }

    [Fact]
    public async Task Ls_SendsContainerPath()
    {
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "d\t0\t/workspace/workspace/sub\nf\t3\t/workspace/workspace/a.txt\n");
        var tools = CreateTools(CreateSandbox(handler));

        var result = JsonSerializer.Serialize(await tools.ListDirectoryAsync("/mnt/workspace"));

        result.ShouldContain("a.txt");
        var command = ExtractExecCommands(handler).Single();
        command.ShouldContain("find ");
        command.ShouldContain("/workspace/workspace");
        command.ShouldNotContain(_dataRoot);
    }

    [Fact]
    public async Task WriteFile_MkdirAndRedirectUseContainerPath()
    {
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "");
        var tools = CreateTools(CreateSandbox(handler));

        await tools.WriteFileAsync("/mnt/outputs/report/out.txt", "x");

        var commands = ExtractExecCommands(handler);
        commands.Count.ShouldBe(2);
        commands[0].ShouldContain("mkdir -p ");
        commands[0].ShouldContain("/workspace/outputs/report");
        commands[1].ShouldContain("/workspace/outputs/report/out.txt");
        commands.ShouldAllBe(c => !c.Contains(_dataRoot));
    }

    [Fact]
    public async Task Bash_TranslatesMntToContainerPath()
    {
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "ok");
        var tools = CreateTools(CreateSandbox(handler));

        await tools.BashAsync("python /mnt/workspace/build_report.py > /mnt/outputs/report.txt");

        var command = ExtractExecCommands(handler).Single();
        command.ShouldContain("python /workspace/workspace/build_report.py > /workspace/outputs/report.txt");
        command.ShouldNotContain(_dataRoot);
    }

    [Fact]
    public async Task Bash_EscapingPath_IsStillRefusedBeforeReachingTheContainer()
    {
        // 映射发生在宿主侧守卫之后：越界的命令一个请求都不该发到 Docker。
        var handler = new MockDockerHandler();
        handler.SetupExecFlow(exitCode: 0, stdout: "ok");
        var tools = CreateTools(CreateSandbox(handler));

        var result = JsonSerializer.Serialize(await tools.BashAsync("cat /mnt/workspace/../../../etc/passwd"));

        result.ShouldContain("Command denied");
        handler.RequestLog.ShouldBeEmpty();
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private DockerSandbox CreateSandbox(MockDockerHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/v1.45") };
        return new DockerSandbox(
            id: "docker-test",
            httpClient: httpClient,
            containerId: "container-123",
            workspacePath: "/workspace",
            commandTimeout: TimeSpan.FromSeconds(30),
            maxOutputSize: 512 * 1024,
            logger: NullLogger.Instance,
            hostWorkspacePath: _threadDir);
    }

    private SandboxTools CreateTools(ISandbox sandbox)
    {
        var accessor = new AgentExecutionContextAccessor();
        accessor.Properties[SandboxPropertyKeys.ToolEnvironment] = new SandboxToolEnvironment(sandbox, _threadId);
        return new SandboxTools(_translator, NullLogger<SandboxTools>.Instance, accessor);
    }

    /// <summary>
    /// 按发送顺序取出每个 exec-create 请求里的 shell 命令。命令外面套着容器内 timeout 的包裹
    /// （单引号被重新转义），所以断言只看路径本身，不看引号形态。
    /// </summary>
    private static List<string> ExtractExecCommands(MockDockerHandler handler)
    {
        var commands = new List<string>();
        foreach (var request in handler.RequestLog.Where(r =>
                     r.Method == HttpMethod.Post && r.Url.Contains("/exec")
                     && !r.Url.Contains("/start") && !r.Url.Contains("/json")))
        {
            using var document = JsonDocument.Parse(request.Body!);
            var cmd = document.RootElement.EnumerateObject()
                .First(p => string.Equals(p.Name, "Cmd", StringComparison.OrdinalIgnoreCase));
            commands.Add(cmd.Value[2].GetString()!);
        }

        return commands;
    }
}
