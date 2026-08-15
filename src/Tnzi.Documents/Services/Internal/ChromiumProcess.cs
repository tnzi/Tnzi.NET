using System.ComponentModel;

namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 一个用完即弃的 headless 浏览器进程（含它自己的 profile 目录与 DevTools 端点）。
/// </summary>
/// <remarks>
/// <para><b>命令行形态是实测出来的，改动前先读完这段。</b></para>
/// <list type="bullet">
/// <item><c>--remote-debugging-port=0</c> 让内核挑一个空闲端口，端口号写进
/// <c>&lt;profile&gt;/DevToolsActivePort</c>。**要读这个文件而不是去解析 stderr 的那行提示**：
/// 提示文本不是契约，而端口文件是浏览器自动化一直依赖的既定形态。</item>
/// <item>每个实例一个**全新的** profile 目录，用完连目录一起删。与 LibreOffice 那条路径
/// 「长期复用一个 profile」相反 —— 那边是因为冷 profile 引导会崩，这边是因为浏览器会锁住
/// profile 目录，复用等于把并发上限锁死成 1。</item>
/// <item>退出一律 <c>Kill(entireProcessTree: true)</c>：浏览器是多进程的，只杀父进程会留下一堆
/// 渲染进程继续占着 profile 目录（实测：调试阶段用 <c>rm -rf</c> 清临时目录时被一整屏
/// "Device or resource busy" 挡下来，就是这么留下的）。</item>
/// <item>stdout / stderr 必须持续排空，否则管道写满会让浏览器卡住 —— 与 LibreOffice 那条路径
/// 同一个坑，这里用事件式读取（<c>BeginErrorReadLine</c>）而不是等进程退出后再读，
/// 因为本进程在整个转换期间都是活着的。</item>
/// </list>
/// </remarks>
internal sealed class ChromiumProcess : IDisposable
{
    private const string PortFileName = "DevToolsActivePort";
    private const int CapturedOutputLimit = 2000;

    private readonly Process _process;
    private readonly StringBuilder _diagnostics;

    private ChromiumProcess(Process process, StringBuilder diagnostics, Uri endpoint)
    {
        _process = process;
        _diagnostics = diagnostics;
        Endpoint = endpoint;
    }

    /// <summary>浏览器级 DevTools WebSocket 端点。</summary>
    public Uri Endpoint { get; }

    /// <summary>启动浏览器并等它把 DevTools 端口写出来。</summary>
    /// <param name="executable">浏览器可执行文件。</param>
    /// <param name="profileDirectory">本次专用的 profile 目录（调用方负责创建与删除）。</param>
    /// <param name="options">HTML 渲染配置。</param>
    /// <param name="timeout">启动超时。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<ChromiumProcess> StartAsync(
        string executable,
        string profileDirectory,
        HtmlPdfOptions options,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in BuildArguments(profileDirectory, options))
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var diagnostics = new StringBuilder();

        void Capture(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null)
                return;

            lock (diagnostics)
            {
                if (diagnostics.Length < CapturedOutputLimit)
                    diagnostics.AppendLine(args.Data);
            }
        }

        process.OutputDataReceived += Capture;
        process.ErrorDataReceived += Capture;

        try
        {
            if (!process.Start())
                throw new DocumentConversionException($"Failed to start the browser at '{executable}'.");
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new DocumentConversionException($"Failed to start the browser at '{executable}': {ex.Message}", innerException: ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var endpoint = await WaitForEndpointAsync(process, profileDirectory, diagnostics, timeout, ct);
            return new ChromiumProcess(process, diagnostics, endpoint);
        }
        catch
        {
            KillTree(process);
            process.Dispose();
            throw;
        }
    }

    /// <summary>浏览器输出的诊断文本（截断），用于把失败原因带回给调用方。</summary>
    public string Diagnostics
    {
        get
        {
            lock (_diagnostics)
                return _diagnostics.ToString().Trim();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        KillTree(_process);
        _process.Dispose();
    }

    private static IEnumerable<string> BuildArguments(string profileDirectory, HtmlPdfOptions options)
    {
        // 逐个塞 ArgumentList（而不是拼一条 Arguments 字符串）：路径里的空格由运行时负责转义。
        yield return "--headless=new";
        yield return "--disable-gpu";
        yield return "--no-first-run";
        yield return "--no-default-browser-check";
        yield return "--no-service-autorun";
        yield return "--disable-extensions";
        yield return "--disable-default-apps";
        yield return "--disable-background-networking";
        yield return "--disable-sync";
        yield return "--disable-component-update";
        yield return "--disable-client-side-phishing-detection";
        yield return "--metrics-recording-only";
        yield return "--mute-audio";
        yield return "--hide-scrollbars";

        // 容器里 /dev/shm 常常只有 64MB，不加这条渲染大文档会随机崩在共享内存上
        yield return "--disable-dev-shm-usage";

        if (options.NoSandbox)
            yield return "--no-sandbox";

        yield return "--user-data-dir=" + profileDirectory;
        yield return "--remote-debugging-port=0";
        yield return "about:blank";
    }

    private static async Task<Uri> WaitForEndpointAsync(
        Process process,
        string profileDirectory,
        StringBuilder diagnostics,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var portFile = Path.Combine(profileDirectory, PortFileName);
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (TryReadEndpoint(portFile, out var endpoint))
                return endpoint;

            if (process.HasExited)
            {
                string captured;
                lock (diagnostics)
                    captured = diagnostics.ToString().Trim();

                throw new DocumentConversionException(
                    $"The browser exited with code {process.ExitCode} before it was ready to render. {captured}".TrimEnd());
            }

            await Task.Delay(50, ct);
        }

        throw new DocumentConversionException(
            $"The browser did not become ready within {timeout.TotalSeconds:0} seconds. " +
            "Raise 'Documents:Html:TimeoutSeconds' if the host is heavily loaded.",
            isRetryable: true);
    }

    private static bool TryReadEndpoint(string portFile, out Uri endpoint)
    {
        endpoint = null!;

        string[] lines;
        try
        {
            if (!File.Exists(portFile))
                return false;

            // 浏览器正在写这个文件时读到半截是正常的：拿不到完整两行就当作「还没好」，下一轮再读。
            lines = File.ReadAllLines(portFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (lines.Length < 2 || !int.TryParse(lines[0].Trim(), out var port) || port <= 0)
            return false;

        var path = lines[1].Trim();
        if (path.Length == 0)
            return false;

        endpoint = new Uri($"ws://127.0.0.1:{port}{path}");
        return true;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception or AggregateException)
        {
            // 进程已自行退出，或平台不支持杀进程树：这里没有可做的补救，也不能盖过原始异常
        }
    }
}
