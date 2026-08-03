namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 本机进程宿主：不死锁、能整树终止、留得住 stderr。
/// </summary>
/// <remarks>
/// <b>全部用测试自建的脚本</b>，绝不碰用户安装的任何 agent CLI ——
/// CI 机器上可能真的装了 claude，一个手滑的测试会真的调用账号、消耗配额。
/// </remarks>
public class LocalProcessHostTests : IDisposable
{
    private readonly string _scratch = Path.Combine(
        Path.GetTempPath(), "tnzi-cli-proc-" + Guid.NewGuid().ToString("N"));

    private readonly LocalProcessHost _host = new(NullLogger<LocalProcessHost>.Instance);

    public LocalProcessHostTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不该让测试红。
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 用当前进程的 dotnet 宿主跑一段脚本 —— 但那太重。改用平台原生 shell：
    /// Windows 走 cmd.exe，其余走 /bin/sh。两者都必然存在。
    /// </summary>
    private static (string Executable, List<string> Args) Shell(string script)
        => OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe",
               ["/c", script])
            : ("/bin/sh", ["-c", script]);

    private CliProcessSpec Spec(string script)
    {
        var (executable, args) = Shell(script);
        return new CliProcessSpec
        {
            ExecutablePath = executable,
            Arguments = args,
            WorkingDirectory = _scratch,
            TerminateGrace = TimeSpan.FromSeconds(2)
        };
    }

    [Fact]
    public async Task Start_WhenExecutableMissing_ThrowsTypedException()
    {
        // 类型化异常是失败分类的来源：分类必须在做出判断的那一刻确定，
        // 而不是事后从错误文案反推。
        var spec = new CliProcessSpec
        {
            ExecutablePath = Path.Combine(_scratch, "definitely-not-here"),
            WorkingDirectory = _scratch
        };

        await Should.ThrowAsync<CliExecutableNotFoundException>(
            () => _host.StartAsync(spec, CancellationToken.None));
    }

    [Fact]
    public async Task Transport_ReadsStdoutLines()
    {
        await using var process = await _host.StartAsync(
            Spec(OperatingSystem.IsWindows() ? "echo hello&& echo world" : "echo hello; echo world"),
            CancellationToken.None);

        var lines = new List<string>();
        await foreach (var line in process.Transport.ReadLinesAsync(CancellationToken.None))
        {
            lines.Add(line.Trim());
        }

        lines.ShouldContain("hello");
        lines.ShouldContain("world");
    }

    [Fact]
    public async Task Transport_CapturesStderrTailForCrashDiagnosis()
    {
        // 没有 stderr 尾部，一次崩溃只剩 "exit code 3"，无从定位。
        var script = OperatingSystem.IsWindows()
            ? "echo boom 1>&2& exit 3"
            : "echo boom 1>&2; exit 3";

        await using var process = await _host.StartAsync(Spec(script), CancellationToken.None);

        await process.WaitForExitAsync(CancellationToken.None);
        await process.TerminateAsync(CancellationToken.None);

        process.Transport.StderrTail.ShouldContain("boom");
        process.ExitCode.ShouldBe(3);
    }

    [Fact]
    public async Task WriteLine_WithLargePrompt_DoesNotDeadlock()
    {
        // ★这条回归测试<b>必须</b>用 ≥64KB 的提示。实测 1KB 顺序写不死锁（塞得进管道
        // 缓冲区，写调用立刻返回），64KB 必死锁 —— 也就是说开发期用短提示根本测不出来，
        // 上生产遇到长上下文才炸。用小 payload 等于没测。
        var payload = new string('x', 96 * 1024);

        // 子进程持续写 stdout（占满它那侧的管道），同时我们往 stdin 写一大段。
        // 若没有独立的抽水任务持续排空 stdout，两侧互相阻塞。
        var script = OperatingSystem.IsWindows()
            ? "for /L %i in (1,1,400) do @echo chatter-%i"
            : "i=0; while [ $i -lt 400 ]; do echo chatter-$i; i=$((i+1)); done; cat > /dev/null";

        await using var process = await _host.StartAsync(Spec(script), CancellationToken.None);

        var write = process.Transport.WriteLineAsync(payload, CancellationToken.None);
        var completed = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(20)));

        completed.ShouldBe(write, "writing a 96KB prompt must not block on the child's stdout");
        await write;
    }

    [Fact]
    public async Task Terminate_KillsTheProcess()
    {
        var script = OperatingSystem.IsWindows()
            ? "ping -n 60 127.0.0.1 > nul"
            : "sleep 60";

        var process = await _host.StartAsync(Spec(script), CancellationToken.None);
        var pid = process.ProcessId;

        await process.TerminateAsync(CancellationToken.None);

        process.HasExited.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Fact]
    public async Task Terminate_KillsDescendantProcesses()
    {
        // 只杀直接子进程会留下孤儿：CLI 自己拉起的 MCP server 与工具子进程会继续跑，
        // 在无硬超时的续接会话上能持续烧掉数小时的模型预算。
        //
        // 脚本写成文件而不是内联字符串：内联时嵌套引号在 cmd.exe 上极易被吞掉，
        // 于是"孙进程没起来"会伪装成"终止成功"，测试就变成了自欺。
        var marker = Path.Combine(_scratch, "descendant-alive.txt");
        var process = await _host.StartAsync(WriteDescendantScript(marker), CancellationToken.None);

        // 等孙进程真的开始写。
        for (var i = 0; i < 40 && !File.Exists(marker); i++)
        {
            await Task.Delay(250);
        }

        File.Exists(marker).ShouldBeTrue("the descendant process should have started writing");

        await process.TerminateAsync(CancellationToken.None);
        await Task.Delay(1500);

        var sizeAfterTerminate = new FileInfo(marker).Length;
        await Task.Delay(2500);
        var sizeLater = new FileInfo(marker).Length;

        sizeLater.ShouldBe(sizeAfterTerminate, "no descendant may keep running after the process tree is terminated");

        await process.DisposeAsync();
    }

    /// <summary>
    /// 写出「父进程派生一个持续写文件的孙进程，然后自己长睡」的脚本。
    /// </summary>
    private CliProcessSpec WriteDescendantScript(string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            var child = Path.Combine(_scratch, "descendant-child.cmd");
            File.WriteAllText(child, $"""
                @echo off
                :loop
                echo alive>>"{marker}"
                ping -n 2 127.0.0.1 >nul
                goto loop
                """);

            var parent = Path.Combine(_scratch, "descendant-parent.cmd");
            File.WriteAllText(parent, $"""
                @echo off
                start "" /b cmd /c "{child}"
                ping -n 60 127.0.0.1 >nul
                """);

            return new CliProcessSpec
            {
                ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe",
                Arguments = ["/c", parent],
                WorkingDirectory = _scratch,
                TerminateGrace = TimeSpan.FromSeconds(2)
            };
        }

        var script = Path.Combine(_scratch, "descendant-parent.sh");
        File.WriteAllText(script, $"""
            #!/bin/sh
            ( while true; do echo alive >> "{marker}"; sleep 1; done ) &
            sleep 60
            """);

        return new CliProcessSpec
        {
            ExecutablePath = "/bin/sh",
            Arguments = [script],
            WorkingDirectory = _scratch,
            TerminateGrace = TimeSpan.FromSeconds(2)
        };
    }

    [Fact]
    public async Task Start_DoesNotLeakHostEnvironmentByDefault()
    {
        // 外部 agent 能执行任意命令。把应用进程的完整环境交给它，等于把数据库连接串、
        // 签名密钥、云凭据一并交出去。
        const string secret = "TNZI_CLI_TEST_SECRET";
        Environment.SetEnvironmentVariable(secret, "must-not-leak");

        try
        {
            var script = OperatingSystem.IsWindows()
                ? $"echo [%{secret}%]"
                : $"echo \"[${secret}]\"";

            await using var process = await _host.StartAsync(Spec(script), CancellationToken.None);

            var output = new StringBuilder();
            await foreach (var line in process.Transport.ReadLinesAsync(CancellationToken.None))
            {
                output.Append(line);
            }

            output.ToString().ShouldNotContain("must-not-leak");
        }
        finally
        {
            Environment.SetEnvironmentVariable(secret, null);
        }
    }

    [Fact]
    public async Task Start_PassesExplicitEnvironmentEntries()
    {
        const string key = "TNZI_CLI_TEST_EXPLICIT";
        var script = OperatingSystem.IsWindows()
            ? $"echo [%{key}%]"
            : $"echo \"[${key}]\"";

        var (executable, args) = Shell(script);
        var spec = new CliProcessSpec
        {
            ExecutablePath = executable,
            Arguments = args,
            WorkingDirectory = _scratch,
            Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [key] = "provided" }
        };

        await using var process = await _host.StartAsync(spec, CancellationToken.None);

        var output = new StringBuilder();
        await foreach (var line in process.Transport.ReadLinesAsync(CancellationToken.None))
        {
            output.Append(line);
        }

        output.ToString().ShouldContain("provided");
    }
}
