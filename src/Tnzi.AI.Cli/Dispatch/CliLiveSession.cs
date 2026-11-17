namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 一个在两条运行之间交接的 CLI 进程：进程本身、读它输出的适配器与事件流、它所在的工作区，以及它手里那份回写凭据（文档 §6.18）。
/// </summary>
/// <remarks>
/// 事件流的读取令牌是进程自己的（<see cref="Lifetime"/>），不是哪一条运行的：交出进程的那条运行结束时它的令牌被取消，不能连带停掉进程。
/// 拿着它的那条运行把自己的看门狗与取消接到 <see cref="Lifetime"/> 上。
/// </remarks>
internal sealed class CliLiveSession
{
    /// <summary>进程。</summary>
    public required ICliProcess Process { get; init; }

    /// <summary>读它输出的适配器（有状态，跟着进程走）。</summary>
    public required ICliProtocolAdapter Adapter { get; init; }

    /// <summary>适配器的事件流。</summary>
    public required IAsyncEnumerator<CliAgentEvent> Events { get; init; }

    /// <summary>交出时正在等的那一次读取（等下一行输出）：接手的运行先等它。</summary>
    public required Task<bool> PendingNext { get; init; }

    /// <summary>事件流的读取令牌：取消即结束读取。</summary>
    public required CancellationTokenSource Lifetime { get; init; }

    /// <summary>起进程时的启动规格：下一条运行的规格与它不同（换了模型、权限档、工具）时不能接着用这个进程。</summary>
    public required CliProcessSpec Spec { get; init; }

    /// <summary>起进程时启动环境里按运行签发的凭据与它们的标识（<see cref="CliLaunchEnvironment.RunScopedVariables"/>）：与 <see cref="Spec"/> 一起比较。</summary>
    public IReadOnlyDictionary<string, string?> RunScopedVariables { get; init; }
        = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>进程所在的工作区（接手的运行结束时照常收尾）。</summary>
    public required CliWorkspace Workspace { get; init; }

    /// <summary>交出这个进程的运行（已经落了终态）。</summary>
    public required Guid FromRunId { get; init; }

    /// <summary>起这个进程的运行（进程的启动环境绑在它上面）；一路交接下去也是最初起它的那条。</summary>
    public required Guid LaunchedByRunId { get; init; }

    /// <summary>进程的 CLI 会话。</summary>
    public string? SessionId { get; init; }

    /// <summary>进程的 MCP 配置里那份回写凭据的哈希与过期时间：接手的运行接过它，进程里的回写调用才继续有效。</summary>
    public string? WriteBackTokenHash { get; init; }

    /// <inheritdoc cref="WriteBackTokenHash"/>
    public DateTime? WriteBackTokenExpiresAt { get; init; }

    /// <summary>
    /// 那份回写凭据本身（与进程 MCP 配置里的逐字相同）：接手的运行拿它向执行目标组出这条运行的回写条目，与布置工作区时一样，
    /// 目标据此回答这条运行的启动档案。服务端重启后接回来的进程交出时没有它（只存了哈希），为 null。
    /// </summary>
    public string? WriteBackToken { get; init; }

    /// <summary>接手的运行已经把它当成自己的进程管起来（之后由那次执行结束它）；在那之前出错时由取到它的一方结束它。</summary>
    public bool Adopted { get; set; }

    /// <summary>
    /// 不接着用了（交接没成、或者规格对不上）：结束读取、整树终止进程（后台任务随之结束）、释放它。
    /// </summary>
    public async Task EndAsync()
    {
        await Lifetime.CancelAsync();
        await Process.TerminateAsync(CancellationToken.None);
        try
        {
            await PendingNext;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // 读取随进程一起结束：这里只是等它停下
        }

        await Events.DisposeAsync();
        await Process.DisposeAsync();
        Lifetime.Dispose();
    }
}
