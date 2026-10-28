namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 默认执行宿主：API 进程所在的机器。进程、文件、可执行文件查找都直接作用于本机。
/// </summary>
/// <remarks>
/// <para>
/// 对任何作用域都交出它自己（本机只有一个执行位置）：所有外部运行以 API 进程这一个 OS 账号执行，
/// 工作区不是运行之间的权限边界。需要按用户隔离的部署提供自己的 <see cref="ICliHost"/>。
/// </para>
/// <para>
/// 整树终止按平台分流：Windows 走 Job Object（内核保证），Unix 走进程组信号
/// （<c>setsid</c> 可用时；不可用则回落到 .NET 的进程树遍历）。两条路径的差异与各自的
/// 保证强度写在 <see cref="WindowsJobObject"/> / <see cref="UnixProcessGroup"/> 上。
/// </para>
/// </remarks>
public class LocalProcessHost : ICliHost, ICliHostTarget
{
    private readonly ILogger<LocalProcessHost> _logger;
    private readonly IOptionsMonitor<CliAgentOptions> _options;

    /// <summary>初始化本机宿主。</summary>
    public LocalProcessHost(ILogger<LocalProcessHost> logger, IOptionsMonitor<CliAgentOptions> options)
    {
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public string Name => Environment.MachineName;

    /// <inheritdoc />
    public ICliHostFileSystem FileSystem => LocalHostFileSystem.Instance;

    /// <inheritdoc />
    /// <remarks><c>AI:Cli:WorkspacesRoot</c>，未配置时 <see cref="CliWorkspaceLayout.DefaultWorkspacesRoot"/>；每次读当前配置。</remarks>
    public string WorkspacesRoot => CliWorkspaceLayout.ResolveWorkspacesRoot(_options.CurrentValue);

    /// <inheritdoc />
    /// <remarks><c>AI:Cli:AssignedWorkDirectoryRoots</c>，默认空：本机不接受按运行指定的工作目录。每次读当前配置。</remarks>
    public IReadOnlyList<string> WorkDirectoryRoots => _options.CurrentValue.AssignedWorkDirectoryRoots;

    /// <inheritdoc />
    public Task<ICliHostTarget> GetTargetAsync(CliHostScope scope, CancellationToken cancellationToken)
    {
        Check.NotNull(scope);
        return Task.FromResult<ICliHostTarget>(this);
    }

    /// <inheritdoc />
    public Task<string?> FindExecutableAsync(CliProviderDescriptor provider, CancellationToken cancellationToken)
        => Task.FromResult(CliExecutableResolver.Resolve(provider));

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="CliLaunchProfile.Local"/>：只用平台给的 MCP 配置。这台机器上 CLI 能找到的 MCP 是起 API 进程那个系统账号的，
    /// 不是派出这次运行的用户的。
    /// </remarks>
    public Task<CliLaunchProfile> GetLaunchProfileAsync(CliLaunchProfileRequest request, CancellationToken cancellationToken)
    {
        Check.NotNull(request);
        return Task.FromResult(CliLaunchProfile.Local);
    }

    /// <inheritdoc />
    public Task<ICliProcess> StartAsync(CliProcessSpec spec, CancellationToken cancellationToken)
    {
        Check.NotNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(spec.ExecutablePath))
        {
            throw new CliExecutableNotFoundException(spec.ExecutablePath);
        }

        if (OperatingSystem.IsWindows() && FindUnsafeBatchArgument(spec.ExecutablePath, spec.Arguments) is not null)
        {
            throw new CliProcessLaunchException(
                spec.ExecutablePath,
                "an argument contains a character that cmd.exe interprets (& | < > ^ % \" or a line break), "
                + "and batch files are started through cmd.exe. Remove it from the binding's arguments or model, "
                + "or point the provider's executable path at the program itself.");
        }

        var startInfo = CreateStartInfo(spec.ExecutablePath, spec.WorkingDirectory ?? string.Empty);

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

        // 探测命令按周期反复执行，记成 Information 会淹掉真正的会话启动记录
        _logger.Log(
            spec.Purpose == CliProcessPurpose.Probe ? LogLevel.Debug : LogLevel.Information,
            "Started external agent process {ProcessId}: {Executable} (cwd={WorkingDirectory})",
            process.Id, spec.ExecutablePath, spec.WorkingDirectory);

        var transport = new ProcessTransport(process, Path.GetFileNameWithoutExtension(spec.ExecutablePath), _logger);
        return Task.FromResult<ICliProcess>(
            new LocalCliProcess(process, transport, job, spec.TerminateGrace, setsidPath is not null, _logger));
    }

    /// <summary>
    /// 子进程启动参数的骨架：三路标准流全部重定向，并且三路都固定为不带 BOM 的 UTF-8。
    /// </summary>
    /// <remarks>
    /// 提示词经 stdin 投递（stream-json 的 user 帧、ACP 的 JSON-RPC 请求），编码 CLI 按 UTF-8 解码。
    /// 不指定时 .NET 按宿主控制台的输入代码页编码 stdin：挂着控制台、代码页是 936 / 437、
    /// 应用又注册了 <c>CodePagesEncodingProvider</c> 的 Windows 宿主上，中文与其它非 ASCII 提示词
    /// 会以 GBK 等字节写进去，CLI 收到的是乱码，而这一路没有任何报错。
    /// </remarks>
    internal static ProcessStartInfo CreateStartInfo(string executablePath, string workingDirectory) => new()
    {
        FileName = executablePath,
        WorkingDirectory = workingDirectory,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardInputEncoding = new UTF8Encoding(false),
        StandardOutputEncoding = new UTF8Encoding(false),
        StandardErrorEncoding = new UTF8Encoding(false)
    };

    /// <summary>
    /// 可执行文件是批处理（<c>.cmd</c> / <c>.bat</c>）时，第一个会被 cmd.exe 当成语法的参数；没有返回 null。
    /// </summary>
    /// <remarks>
    /// Windows 上 npm 安装的 CLI 落在 PATH 里的是 <c>.cmd</c> 垫片，<c>CreateProcess</c> 经 cmd.exe 起它，
    /// 参数会被 cmd 再解析一遍：.NET 只给含空格或引号的参数加引号，一个不带空格的 <c>&amp;</c> 就是命令分隔符，
    /// <c>%</c> 会展开环境变量。参数来自绑定的模型名与自定义参数（管理员填写）以及 CLI 自己报出的会话 id，
    /// 不在这里拦，填错一个字符就是在宿主上执行任意命令。
    /// </remarks>
    internal static string? FindUnsafeBatchArgument(string executablePath, IEnumerable<string> arguments)
    {
        var extension = Path.GetExtension(executablePath);
        if (!string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return arguments.FirstOrDefault(argument => argument.IndexOfAny(BatchMetacharacters) >= 0);
    }

    private static readonly char[] BatchMetacharacters = ['&', '|', '<', '>', '^', '%', '"', '\r', '\n'];
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
            //    关不上（一次写入卡在管道上：子进程根本不读 stdin）就直接进下一档，
            //    等那次写入让路只会等到子进程退出，而让它退出正是下面几档的事。
            if (await _transport.TryCloseInputAsync(_grace)
                && await WaitForExitWithinAsync(_grace, cancellationToken))
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
