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
    private readonly string _scratch = TestScratch.NewPath("proc");

    private readonly LocalProcessHost _host = LocalHosts.Create();

    public LocalProcessHostTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        TestScratch.Delete(_scratch);

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
    public void StartInfo_PinsEveryStandardStreamToUtf8WithoutABom()
    {
        // 提示词走 stdin。不指定 stdin 编码时 .NET 取宿主控制台的输入代码页：控制台是 936 / 437、
        // 应用注册了 CodePagesEncodingProvider 的 Windows 宿主上，中文提示按 GBK 字节写进去，
        // 按 UTF-8 解码的 CLI 收到乱码。这个前提在测试进程里造不出来，所以直接钉住启动参数。
        var startInfo = LocalProcessHost.CreateStartInfo("cli", _scratch);

        foreach (var encoding in new[] { startInfo.StandardInputEncoding, startInfo.StandardOutputEncoding, startInfo.StandardErrorEncoding })
        {
            encoding.ShouldNotBeNull();
            encoding.CodePage.ShouldBe(Encoding.UTF8.CodePage);
            encoding.GetPreamble().ShouldBeEmpty("a BOM would land in front of the first frame the CLI reads");
        }
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

    /// <summary>
    /// 等「不该卡住的事」完成的上限。条件一成立立即返回，只有真的卡死才会走满；
    /// 按满载机器定（几十个测试进程并行），不按单跑时的几十毫秒定。
    /// </summary>
    private static readonly TimeSpan StuckBound = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task WriteLine_WithLargePrompt_DoesNotDeadlock()
    {
        // ★这条回归测试<b>必须</b>用 ≥64KB 的提示。实测 1KB 顺序写不死锁（塞得进管道
        // 缓冲区，写调用立刻返回），64KB 必死锁 —— 也就是说开发期用短提示根本测不出来，
        // 上生产遇到长上下文才炸。用小 payload 等于没测。
        var payload = new string('x', 96 * 1024);

        // 子进程先往 stdout 灌远超管道缓冲区的数据，同时我们往 stdin 写一大段。
        // 若没有独立的抽水任务持续排空 stdout，子进程卡在写 stdout 上，永远走不到读 stdin
        // （Windows 分支是走不到退出、让管道断开），我们卡在写 stdin 上：两侧互相阻塞。
        //
        // ★灌数据必须是<b>一条命令</b>而不是逐行循环。cmd.exe 每执行一条命令都要与控制台宿主
        // 往返一次，满载机器上 400 次 echo 的循环单独就要一分多钟（实测：输出直接重定向到文件、
        // 不经过任何管道也一样），测试量到的是 cmd 的调度延迟而不是死锁。
        // ★灌的量按最大的默认管道缓冲区算（Linux 64KB），只超过 Windows 的 4KB 等于在 Linux 上没测。
        const int floodLines = 20_000;
        File.WriteAllText(
            Path.Combine(_scratch, "flood.txt"),
            string.Concat(Enumerable.Range(0, floodLines).Select(i => FloodLine(i) + "\n")));

        var script = OperatingSystem.IsWindows()
            ? "type flood.txt"
            : "cat flood.txt; cat > /dev/null";

        await using var process = await _host.StartAsync(Spec(script), CancellationToken.None);

        var write = process.Transport.WriteLineAsync(payload, CancellationToken.None);
        var completed = await Task.WhenAny(write, Task.Delay(StuckBound));

        completed.ShouldBe(write, "writing a 96KB prompt must not block on the child's stdout");
        await write;

        // 子进程确实把数据全灌出来了。否则（比如脚本没找到文件、立即退出）写入会因为管道断开
        // 而「顺利」返回，这条测试就成了自欺。
        var lastLine = FloodLine(floodLines - 1);
        var sawLastLine = false;
        using var readTimeout = new CancellationTokenSource(StuckBound);
        await foreach (var line in process.Transport.ReadLinesAsync(readTimeout.Token))
        {
            if (line.Trim() == lastLine)
            {
                sawLastLine = true;
                break;
            }
        }

        sawLastLine.ShouldBeTrue("the child must have flooded its stdout well past the pipe buffer");
    }

    private static string FloodLine(int index) => $"chatter-{index:D5}";

    [Fact]
    public async Task Terminate_WhileAWriteIsStuck_StillKillsTheProcess()
    {
        // 子进程既不读 stdin 也不退出（卡死的 CLI 就是这样），一次大写入就会卡在管道上、
        // 占着写通道。终止的第一档「关 stdin」要先等写通道让出来：不设上限地等，就永远走不到
        // 强制终止，而杀掉进程恰恰是让那次写入结束的唯一办法。取消和看门狗都会救不回这个进程。
        //
        // 子进程的寿命必须远长于 StuckBound：否则不修也会在它自然退出时「终止成功」。
        var script = OperatingSystem.IsWindows()
            ? "ping -n 300 127.0.0.1 > nul"
            : "sleep 300";

        var process = await _host.StartAsync(Spec(script), CancellationToken.None);
        var write = process.Transport.WriteLineAsync(new string('x', 96 * 1024), CancellationToken.None);

        // 前提：写入确实卡住了。子进程不读 stdin，96KB 超过任何平台的默认管道缓冲区，
        // 它只可能在子进程退出（管道断开）时返回；此刻返回说明子进程没起来，场景不成立。
        (await Task.WhenAny(write, Task.Delay(500))).ShouldNotBe(write, "the write should be stuck on the child's stdin");

        var terminate = process.TerminateAsync(CancellationToken.None);
        var completed = await Task.WhenAny(terminate, Task.Delay(StuckBound));

        completed.ShouldBe(terminate, "termination must not wait forever for a write the child will never read");
        await terminate;
        process.HasExited.ShouldBeTrue();

        // 进程没了，管道断开，卡住的写入随之结束（按设计吞掉 IOException，不抛）。
        await write;
        await process.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_BeforeAStuckWriteUnwinds_TheWriteStillEndsCleanly()
    {
        // 通道释放时，那次卡在管道上的写入不一定已经收尾：终止流程先杀进程，写入随后才以
        // 管道断开结束，两者之间没有先后保证（满载时，或有逃出进程树的后代还握着 stdin 时）。
        // 写入收尾时要归还写通道；通道若已释放就在这里抛 ObjectDisposedException，调用方等到的
        // 是一个与「子进程退出」毫不相干的异常，发后不管的写入则变成一条未观察异常。
        var script = OperatingSystem.IsWindows()
            ? "ping -n 300 127.0.0.1 > nul"
            : "sleep 300";

        var process = await _host.StartAsync(Spec(script), CancellationToken.None);
        var transport = (ProcessTransport)process.Transport;
        var write = transport.WriteLineAsync(new string('x', 96 * 1024), CancellationToken.None);
        (await Task.WhenAny(write, Task.Delay(500))).ShouldNotBe(write, "the write should be stuck on the child's stdin");

        // 把这个时序钉死：先让终止流程放弃关 stdin、释放通道，再杀进程让写入收尾。
        (await transport.TryCloseInputAsync(TimeSpan.Zero)).ShouldBeFalse("the stuck write still holds the write gate");
        await transport.DisposeAsync();
        await process.TerminateAsync(CancellationToken.None);

        var completed = await Task.WhenAny(write, Task.Delay(StuckBound));
        completed.ShouldBe(write, "the stuck write must end once the process is gone");
        await write;
        await process.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_WhileAWriteIsStuck_ReturnsWithoutWaitingForTheWrite()
    {
        // 释放此前无限期地等写通道：子进程不读 stdin 时那次卡住的写入永远让不出来，
        // 先于终止释放通道的调用方就跟着永远挂住。释放要与终止流程一样有上限地等。
        var script = OperatingSystem.IsWindows()
            ? "ping -n 300 127.0.0.1 > nul"
            : "sleep 300";

        var process = await _host.StartAsync(Spec(script), CancellationToken.None);
        try
        {
            var transport = (ProcessTransport)process.Transport;
            var write = transport.WriteLineAsync(new string('x', 96 * 1024), CancellationToken.None);
            (await Task.WhenAny(write, Task.Delay(500))).ShouldNotBe(write, "the write should be stuck on the child's stdin");

            var dispose = transport.DisposeAsync().AsTask();
            var completed = await Task.WhenAny(dispose, Task.Delay(ProcessTransport.DisposeWriteTimeout + TimeSpan.FromSeconds(10)));

            completed.ShouldBe(dispose, "disposing must not wait forever for a write the child will never read");
            await dispose;

            await process.TerminateAsync(CancellationToken.None);
            (await Task.WhenAny(write, Task.Delay(StuckBound))).ShouldBe(write, "the stuck write ends once the process is gone");
            await write;
        }
        finally
        {
            await process.TerminateAsync(CancellationToken.None);
        }
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

    /// <summary>
    /// Windows 上批处理经 cmd.exe 启动，参数被 cmd 再解析一遍：带 cmd 语法字符的参数当场拒绝，进程根本不起。
    /// </summary>
    [Fact]
    public async Task Start_ABatchFileWithAnArgumentCmdWouldInterpret_IsRefusedBeforeLaunch()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var shim = Path.Combine(_scratch, "tool.cmd");
        var sideEffect = Path.Combine(_scratch, "injected.txt");
        await File.WriteAllTextAsync(shim, "@echo %*\r\n");

        var spec = new CliProcessSpec
        {
            ExecutablePath = shim,
            Arguments = ["--model", $"x&echo pwned>\"{sideEffect}\""],
            WorkingDirectory = _scratch
        };

        var refused = await Should.ThrowAsync<CliProcessLaunchException>(() => _host.StartAsync(spec, CancellationToken.None));

        refused.Message.ShouldContain("cmd.exe");
        File.Exists(sideEffect).ShouldBeFalse();
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
