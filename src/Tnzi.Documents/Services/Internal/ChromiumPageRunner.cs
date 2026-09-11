namespace Tnzi.Documents.Services.Internal;

/// <summary>
/// 「起一个 headless 浏览器、把文档加载进去、在页面上干一件事、收拾干净」的共享骨架。
/// </summary>
/// <remarks>
/// <para>
/// 出 PDF（<see cref="ChromiumHtmlDocumentConverter"/>）与出缩略图（<see cref="ChromiumDocumentImageRenderer"/>）
/// 只在<b>最后那一条 CDP 命令</b>上不同，前面的进程启动、端口发现、会话连接、目标附着、
/// 导航等待、字体就绪、超时归类、临时目录清理全都一模一样。抽在这里而不是各写一份。
/// </para>
/// <para>
/// ★ <b>并发闸门是 <c>static</c> 的</b>，与 <see cref="LibreOfficeDocumentConverter"/> 同一条理由：
/// 它限的是**这台机器的内存**（每个 headless 实例是百 MB 量级），不是某个服务实例的。
/// 挂在实例上的话，两个服务各持一个闸门 = 实际上限翻倍，而那正是「不设上限就是一个现成的拒绝服务面」
/// 想避免的事。容量在首次使用时定死，故改 <c>MaxConcurrency</c> 需要重启进程（已写进配置注释）。
/// </para>
/// </remarks>
internal static class ChromiumPageRunner
{
    /// <summary>渲染用的临时工作目录根（逐次创建、用完即删，含浏览器 profile）。</summary>
    private const string WorkRootName = "tnzi-chromium";

    private const string WorkFileBaseName = "source";
    private const string ProfileDirectoryName = "profile";

    private static readonly object GateSync = new();
    private static SemaphoreSlim? _gate;

    /// <summary>本机找不找得到浏览器。</summary>
    public static bool IsAvailable(HtmlPdfOptions options) => ChromiumLocator.Resolve(options.BrowserPath) != null;

    /// <summary>
    /// 把 <paramref name="source"/> 落到临时目录、用浏览器打开它，然后执行 <paramref name="action"/>。
    /// </summary>
    /// <typeparam name="T">产物类型。</typeparam>
    /// <param name="options">HTML 渲染配置。</param>
    /// <param name="source">源文档字节。</param>
    /// <param name="sourceFileName">源文件名（只取扩展名）。</param>
    /// <param name="action">页面就绪后要干的事，拿到会话与 <c>sessionId</c>。</param>
    /// <param name="logger">日志（只用于清理失败的告警）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="prepare">
    /// 可选：<b>导航之前</b>在会话上做的准备（视口、媒体类型这类会影响排版的设置）。
    /// 刻意放在导航前而不是加载后 —— 加载后再改视口要靠一次重排才生效，多一个时序变量。
    /// </param>
    public static async Task<T> RunAsync<T>(
        HtmlPdfOptions options,
        byte[] source,
        string sourceFileName,
        Func<DevToolsSession, string, CancellationToken, Task<T>> action,
        ILogger logger,
        CancellationToken ct,
        Func<DevToolsSession, string, CancellationToken, Task>? prepare = null)
    {
        var executable = ChromiumLocator.Resolve(options.BrowserPath)
            ?? throw new DocumentConversionException(ChromiumLocator.NotFoundMessage(options.BrowserPath));

        var workDirectory = Path.Combine(Path.GetTempPath(), WorkRootName, Guid.NewGuid().ToString("N"));
        var profileDirectory = Path.Combine(workDirectory, ProfileDirectoryName);
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        try
        {
            Directory.CreateDirectory(profileDirectory);

            // 扩展名已过白名单，且文件名不参与命令行：浏览器只拿到我们自己拼的工作路径。
            var inputPath = Path.Combine(workDirectory, WorkFileBaseName + Path.GetExtension(sourceFileName));
            await File.WriteAllBytesAsync(inputPath, source, ct);

            var gate = GetGate(options.MaxConcurrency);
            await gate.WaitAsync(ct);
            try
            {
                // 计时从拿到闸门那一刻开始，不含排队：TimeoutSeconds 是「一次渲染」的预算。
                // 若在排队前就启动，闸门满载时后面的请求会一帧都没渲染就以「渲染超时」失败，
                // 而那句提示会让人去调大超时，实际该调的是 MaxConcurrency。
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutSource.CancelAfter(timeout);

                return await OpenAsync(executable, profileDirectory, inputPath, options, action, prepare, timeout, ct, timeoutSource.Token);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            await TryDeleteDirectoryAsync(workDirectory, logger);
        }
    }

    private static async Task<T> OpenAsync<T>(
        string executable,
        string profileDirectory,
        string inputPath,
        HtmlPdfOptions options,
        Func<DevToolsSession, string, CancellationToken, Task<T>> action,
        Func<DevToolsSession, string, CancellationToken, Task>? prepare,
        TimeSpan timeout,
        CancellationToken ct,
        CancellationToken deadline)
    {
        using var browser = await Guard(() => ChromiumProcess.StartAsync(executable, profileDirectory, options, timeout, deadline), timeout, ct);

        await using var session = await Guard(() => DevToolsSession.ConnectAsync(browser.Endpoint, deadline), timeout, ct);

        return await Guard(() => NavigateAndRunAsync(session, inputPath, action, prepare, deadline), timeout, ct, browser);
    }

    private static async Task<T> NavigateAndRunAsync<T>(
        DevToolsSession session,
        string inputPath,
        Func<DevToolsSession, string, CancellationToken, Task<T>> action,
        Func<DevToolsSession, string, CancellationToken, Task>? prepare,
        CancellationToken ct)
    {
        var target = await session.SendAsync("Target.createTarget", new { url = "about:blank" }, ct: ct);
        var targetId = target.GetProperty("targetId").GetString();

        var attached = await session.SendAsync("Target.attachToTarget", new { targetId, flatten = true }, ct: ct);
        var sessionId = attached.GetProperty("sessionId").GetString()
            ?? throw new DocumentConversionException("The browser attached to the page without returning a session id.");

        await session.SendAsync("Page.enable", sessionId: sessionId, ct: ct);

        if (prepare != null)
            await prepare(session, sessionId, ct);

        // ★ 先登记事件再导航：页面可能快到 navigate 的响应还没回来 load 就已经发出了。
        var loaded = session.WhenEventAsync("Page.loadEventFired");

        var navigation = await session.SendAsync(
            "Page.navigate", new { url = new Uri(inputPath).AbsoluteUri }, sessionId, ct);

        if (navigation.TryGetProperty("errorText", out var errorText) && errorText.GetString() is { Length: > 0 } reason)
            throw new DocumentConversionException($"The browser failed to load the document: {reason}");

        await loaded.WaitAsync(ct);

        // 字体没就位就出图会让文本按回退字形排版（行宽随之改变）。拿不到结果不是致命错误：
        // 老浏览器可能没有 document.fonts，此时按「已就绪」继续。
        try
        {
            await session.SendAsync(
                "Runtime.evaluate",
                new { expression = "document.fonts ? document.fonts.ready.then(() => true) : true", awaitPromise = true },
                sessionId,
                ct);
        }
        catch (DocumentConversionException)
        {
            // 忽略：字体就绪只是排版质量的优化，不值得让整次渲染失败
        }

        return await action(session, sessionId, ct);
    }

    /// <summary>
    /// 把「超时」与「调用方取消」区分开：前者要给出可操作的提示，后者原样抛出。
    /// </summary>
    /// <remarks>
    /// 超时后浏览器进程树由 <paramref name="browser"/> 的 <c>Dispose</c> 收拾（<c>using</c> 已经安排好），
    /// 但抛出前先把它的诊断输出捞进消息里 —— 崩溃原因只在它的 stderr 上。
    /// </remarks>
    private static async Task<T> Guard<T>(Func<Task<T>> action, TimeSpan timeout, CancellationToken ct, ChromiumProcess? browser = null)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var diagnostics = browser?.Diagnostics;
            var detail = string.IsNullOrEmpty(diagnostics) ? string.Empty : $" Browser output: {diagnostics}";

            throw new DocumentConversionException(
                $"Browser rendering timed out after {timeout.TotalSeconds:0} seconds. " +
                $"Raise 'Documents:Html:TimeoutSeconds' if large documents or slow remote resources are expected.{detail}",
                isRetryable: true);
        }
    }

    private static SemaphoreSlim GetGate(int maxConcurrency)
    {
        if (_gate != null)
            return _gate;

        lock (GateSync)
        {
            var permits = Math.Clamp(maxConcurrency, 1, 16);
            return _gate ??= new SemaphoreSlim(permits, permits);
        }
    }

    private static async Task TryDeleteDirectoryAsync(string directory, ILogger logger)
    {
        // 浏览器刚被杀掉时 profile 里的文件可能还锁着几十毫秒，重试几次再放弃。
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return;

                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 2)
                {
                    // 清理失败不影响渲染结果，但要留痕（临时目录堆积是可观测的运维问题）
                    logger.LogWarning(ex, "Failed to clean up the browser rendering work directory '{Directory}'.", directory);
                    return;
                }

                // 取消令牌刻意不传：清理跑在 finally 里，取消之后更要把临时目录收拾干净。
                await Task.Delay(200);
            }
        }
    }
}
