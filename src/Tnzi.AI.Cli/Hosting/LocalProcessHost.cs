namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 在 API 进程所在宿主上直接启动外部 agent 子进程。
/// </summary>
/// <remarks>
/// 整树终止按平台分流：Windows 走 Job Object（内核保证），Unix 走进程组信号
/// （<c>setsid</c> 可用时；不可用则回落到 .NET 的进程树遍历）。两条路径的差异与各自的
/// 保证强度写在 <see cref="WindowsJobObject"/> / <see cref="UnixProcessGroup"/> 上。
/// </remarks>
public class LocalProcessHost : ICliProcessHost
{
    private readonly ILogger<LocalProcessHost> _logger;

    /// <summary>初始化本机进程宿主。</summary>
    public LocalProcessHost(ILogger<LocalProcessHost> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public string Name => "local";

    /// <inheritdoc />
    public Task<ICliProcess> StartAsync(CliProcessSpec spec, CancellationToken cancellationToken)
    {
        Check.NotNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(spec.ExecutablePath))
        {
            throw new CliExecutableNotFoundException(spec.ExecutablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = spec.ExecutablePath,
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        var arguments = spec.Arguments;
        string? setsidPath = null;
        if (!OperatingSystem.IsWindows())
        {
            setsidPath = UnixProcessGroup.FindSetsid();
            if (setsidPath is not null)
            {
                // 经 setsid 启动，让子进程成为自己的进程组 leader。是否真的成了，
                // 终止时用 getpgid 核实（见 UnixProcessGroup）。
                startInfo.FileName = setsidPath;
                arguments = [spec.ExecutablePath, .. spec.Arguments];
            }
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // 环境：先清空继承，再按白名单重建。ProcessStartInfo.Environment 初始值是
        // 当前进程的完整环境，不清空的话「默认不透传」就成了空话。
        startInfo.Environment.Clear();
        foreach (var (key, value) in CliEnvironmentBuilder.Build(spec))
        {
            startInfo.Environment[key] = value;
        }

        WindowsJobObject? job = null;
        if (OperatingSystem.IsWindows())
        {
            job = WindowsJobObject.TryCreate();
            if (job is null)
            {
                _logger.LogWarning(
                    "Could not create a Windows Job Object; descendant processes of {Executable} will be terminated best-effort only",
                    spec.ExecutablePath);
            }
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            job?.Dispose();
            process.Dispose();
            throw new CliProcessLaunchException(spec.ExecutablePath, ex);
        }

        if (job is not null && OperatingSystem.IsWindows() && !job.TryAssign(process.Handle))
        {
            _logger.LogWarning(
                "Failed to assign process {ProcessId} to the job object; descendant cleanup falls back to process-tree walking",
                process.Id);
        }

        _logger.LogInformation(
            "Started external agent process {ProcessId}: {Executable} (cwd={WorkingDirectory})",
            process.Id, spec.ExecutablePath, spec.WorkingDirectory);

        var transport = new ProcessTransport(process, Path.GetFileNameWithoutExtension(spec.ExecutablePath), _logger);
        return Task.FromResult<ICliProcess>(
            new LocalCliProcess(process, transport, job, spec.TerminateGrace, setsidPath is not null, _logger));
    }
}

/// <summary>
/// 一个本机外部 agent 子进程。
/// </summary>
internal sealed class LocalCliProcess : ICliProcess
{
    private readonly Process _process;
    private readonly ProcessTransport _transport;
    private readonly WindowsJobObject? _job;
    private readonly TimeSpan _grace;
    private readonly bool _hasProcessGroup;
    private readonly ILogger _logger;
    private int _terminated;

    public LocalCliProcess(
        Process process,
        ProcessTransport transport,
        WindowsJobObject? job,
        TimeSpan grace,
        bool hasProcessGroup,
        ILogger logger)
    {
        _process = process;
        _transport = transport;
        _job = job;
        _grace = grace;
        _hasProcessGroup = hasProcessGroup;
        _logger = logger;
    }

    public ICliAgentTransport Transport => _transport;

    public int ProcessId => _process.Id;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int? ExitCode
    {
        get
        {
            try
            {
                return _process.HasExited ? _process.ExitCode : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken);
        return _process.ExitCode;
    }

    public async Task TerminateAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _terminated, 1) != 0)
        {
            return;
        }

        try
        {
            if (HasExited)
            {
                return;
            }

            // 1) 先关 stdin：多数 CLI 以 EOF 为「没有更多输入了」，会自行收尾退出。
            await _transport.CloseInputAsync();
            if (await WaitForExitWithinAsync(_grace, cancellationToken))
            {
                return;
            }

            // 2) 温和请求整组退出。
            RequestGracefulExit();
            if (await WaitForExitWithinAsync(_grace, cancellationToken))
            {
                return;
            }

            // 3) 强制整树终止。
            ForceKill();
            await WaitForExitWithinAsync(_grace, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Termination of process {ProcessId} raced with its own exit", SafeProcessId());
        }
        finally
        {
            // 杀干净之后才读 stderr 尾部：子进程写出的最后几行往往正是死因，
            // 抢在抽水任务收尾前采样会把它们漏掉。
            await _transport.DrainAsync(TimeSpan.FromSeconds(2));
        }
    }

    private void RequestGracefulExit()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows 没有 SIGTERM 语义可用于控制台之外的进程；优雅退出这一档由
            // 上一步的 stdin EOF 承担，这里直接进入强制终止档。
            return;
        }

        if (_hasProcessGroup && UnixProcessGroup.TrySignalGroup(_process.Id, forceful: false))
        {
            return;
        }

        UnixProcessGroup.TrySignalProcess(_process.Id);
    }

    private void ForceKill()
    {
        if (_job is not null)
        {
            // 关闭作业句柄 = 内核终止全部成员。这是 Windows 上唯一有保证的整树终止方式。
            _job.Dispose();
            return;
        }

        if (!OperatingSystem.IsWindows() && _hasProcessGroup
            && UnixProcessGroup.TrySignalGroup(_process.Id, forceful: true))
        {
            return;
        }

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            _logger.LogWarning(ex, "Failed to kill process tree for {ProcessId}", SafeProcessId());
        }
    }

    private async Task<bool> WaitForExitWithinAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return HasExited;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return HasExited;
        }
    }

    private int SafeProcessId()
    {
        try
        {
            return _process.Id;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await TerminateAsync(CancellationToken.None);
        await _transport.DisposeAsync();
        _job?.Dispose();
        _process.Dispose();
    }
}
