namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 测试用执行宿主：一个「远程」目标，文件系统在内存里（POSIX 路径），进程由测试给出。
/// </summary>
/// <remarks>
/// 用它跑执行器与工作区布置，证明这两层只经 <see cref="ICliHost"/> 碰文件与进程：
/// 这里的路径（<c>/home/agent/...</c>）在 Windows 测试机上根本不存在，任何一处直接用 <see cref="File"/> /
/// <see cref="Directory"/> / <see cref="Path"/> 的代码都会在这里失败或把文件写到本机磁盘上。
/// </remarks>
internal sealed class TestCliHost : ICliHost, ICliHostTarget
{
    private readonly Func<CliProcessSpec, CancellationToken, Task<ICliProcess>> _start;
    private readonly string? _executablePath;
    private readonly Func<CliWriteBackChannel, JsonObject>? _writeBackEntry;
    private readonly ConcurrentQueue<CliProcessSpec> _starts = new();
    private readonly ConcurrentQueue<CliHostScope> _scopes = new();

    /// <param name="start">起进程（收到的规格已记进 <see cref="Starts"/>）。</param>
    /// <param name="executablePath"><see cref="FindExecutableAsync"/> 的答案；null = 找不到。</param>
    /// <param name="fileSystem">文件系统；缺省是一个新的内存文件系统。</param>
    /// <param name="workspacesRoot">工作区根。</param>
    /// <param name="writeBackEntry">回写通道条目；缺省是直连的 HTTP 条目。</param>
    public TestCliHost(
        Func<CliProcessSpec, CancellationToken, Task<ICliProcess>> start,
        string? executablePath = "/nonexistent/claude",
        ICliHostFileSystem? fileSystem = null,
        string workspacesRoot = "/home/agent/workspaces",
        Func<CliWriteBackChannel, JsonObject>? writeBackEntry = null)
    {
        _start = start;
        _executablePath = executablePath;
        FileSystem = fileSystem ?? new InMemoryHostFileSystem();
        WorkspacesRoot = workspacesRoot;
        _writeBackEntry = writeBackEntry;
    }

    /// <summary>每次起进程都返回 <paramref name="process"/> 造出的进程。</summary>
    public static TestCliHost Starting(Func<ICliProcess> process, string? executablePath = "/nonexistent/claude")
        => new((_, _) => Task.FromResult(process()), executablePath);

    /// <summary>起过的进程规格，按顺序。</summary>
    public IReadOnlyList<CliProcessSpec> Starts => [.. _starts];

    /// <summary>取过目标的作用域，按顺序。</summary>
    public IReadOnlyList<CliHostScope> Scopes => [.. _scopes];

    /// <summary>取目标时抛这个异常（模拟远程端没连上）。</summary>
    public Exception? UnavailableWith { get; set; }

    public string Name => "fake-remote";

    /// <summary>这个目标是哪个远程执行目标；null = 接口的缺省（不是远程运行时登记的那种目标）。</summary>
    public string? TargetId { get; set; }

    /// <summary>按运行指定的工作目录允许落在哪些根之下；缺省空 = 不接受。</summary>
    public IReadOnlyList<string> WorkDirectoryRoots { get; set; } = [];

    /// <summary>这个「副本」此刻执行得了哪些运行时上的运行。</summary>
    public CliHostCoverage Coverage { get; set; } = CliHostCoverage.InProcess;

    public CliHostCoverage GetCoverage() => Coverage;

    public ICliHostFileSystem FileSystem { get; }

    public string WorkspacesRoot { get; }

    public Task<ICliHostTarget> GetTargetAsync(CliHostScope scope, CancellationToken cancellationToken)
    {
        _scopes.Enqueue(scope);
        return UnavailableWith is { } unavailable
            ? Task.FromException<ICliHostTarget>(unavailable)
            : Task.FromResult<ICliHostTarget>(this);
    }

    public Task<string?> FindExecutableAsync(CliProviderDescriptor provider, CancellationToken cancellationToken)
        => Task.FromResult(_executablePath);

    public Task<ICliProcess> StartAsync(CliProcessSpec spec, CancellationToken cancellationToken)
    {
        _starts.Enqueue(spec);
        return StartWith is { } startWith ? Task.FromResult(startWith(spec)) : _start(spec, cancellationToken);
    }

    public JsonObject CreateWriteBackServerEntry(CliWriteBackChannel channel)
        => _writeBackEntry?.Invoke(channel) ?? channel.ToHttpEntry();

    /// <summary>启动档案的回答；null = 接口的缺省（<see cref="CliLaunchProfile.Default"/>）。</summary>
    public Func<CliLaunchProfileRequest, CliLaunchProfile>? LaunchProfile { get; set; }

    private readonly ConcurrentQueue<CliLaunchProfileRequest> _profileRequests = new();

    /// <summary>问过的启动档案，按顺序。</summary>
    public IReadOnlyList<CliLaunchProfileRequest> ProfileRequests => [.. _profileRequests];

    public Task<CliLaunchProfile> GetLaunchProfileAsync(CliLaunchProfileRequest request, CancellationToken cancellationToken)
    {
        _profileRequests.Enqueue(request);
        return Task.FromResult(LaunchProfile?.Invoke(request) ?? CliLaunchProfile.Default);
    }

    /// <summary>起进程时优先用它（测试中途换一种进程）；null 时用构造时给的那个。</summary>
    public Func<CliProcessSpec, ICliProcess>? StartWith { get; set; }

    /// <summary>重新附着的回答；null = 进程不在。</summary>
    public Func<CliReattachRequest, CliReattachResult>? OnReattach { get; set; }

    /// <summary>重新附着时抛这个异常（模拟代理还没连回来）。</summary>
    public Exception? ReattachUnavailableWith { get; set; }

    private readonly ConcurrentQueue<CliReattachRequest> _reattaches = new();

    /// <summary>收到过的重新附着请求，按顺序。</summary>
    public IReadOnlyList<CliReattachRequest> Reattaches => [.. _reattaches];

    public Task<CliReattachResult> ReattachAsync(CliReattachRequest request, CancellationToken cancellationToken)
    {
        _reattaches.Enqueue(request);
        if (ReattachUnavailableWith is { } unavailable)
        {
            return Task.FromException<CliReattachResult>(unavailable);
        }

        return Task.FromResult(OnReattach?.Invoke(request) ?? CliReattachResult.Gone("This test host cannot reattach."));
    }
}

/// <summary>
/// 本机宿主（<see cref="LocalProcessHost"/>）的测试构造。
/// </summary>
internal static class LocalHosts
{
    /// <summary>以给定配置（工作区根等）构造本机宿主。</summary>
    public static LocalProcessHost Create(CliAgentOptions? options = null)
        => new(NullLogger<LocalProcessHost>.Instance, new TestOptionsMonitor<CliAgentOptions>(options ?? new CliAgentOptions()));
}

/// <summary>
/// 内存里的 POSIX 文件系统，语义照 <see cref="ICliHostFileSystem"/> 的约定（写文件要求父目录存在、非递归删非空目录抛错）。
/// </summary>
internal sealed class InMemoryHostFileSystem : ICliHostFileSystem
{
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { "/" };

    public CliHostPaths Paths => CliHostPaths.Posix;

    /// <summary>全部文件路径（快照）。</summary>
    public IReadOnlyList<string> Files
    {
        get
        {
            lock (_gate)
            {
                return [.. _files.Keys.Order(StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>读一个文件的文本（UTF-8）；不在时为 null。</summary>
    public string? ReadText(string path)
    {
        lock (_gate)
        {
            return _files.TryGetValue(Normalize(path), out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
        }
    }

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_files.ContainsKey(Normalize(path)));
        }
    }

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_directories.Contains(Normalize(path)));
        }
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            for (var current = Normalize(path); current is not null; current = Paths.GetDirectoryName(current))
            {
                if (_files.ContainsKey(current))
                {
                    throw new IOException($"'{current}' is a file.");
                }

                _directories.Add(current);
            }
        }

        return Task.CompletedTask;
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _files.TryGetValue(Normalize(path), out var bytes)
                ? Task.FromResult(bytes.ToArray())
                : Task.FromException<byte[]>(new FileNotFoundException($"'{path}' does not exist.", path));
        }
    }

    public Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var normalized = Normalize(path);
            var parent = Paths.GetDirectoryName(normalized);
            if (parent is null || !_directories.Contains(parent))
            {
                return Task.FromException(new DirectoryNotFoundException($"The parent directory of '{path}' does not exist."));
            }

            _files[normalized] = contents.ToArray();
        }

        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _files.Remove(Normalize(path));
        }

        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var normalized = Normalize(path);
            if (!_directories.Contains(normalized))
            {
                return Task.CompletedTask;
            }

            var prefix = normalized + "/";
            var hasChildren = _files.Keys.Any(f => f.StartsWith(prefix, StringComparison.Ordinal))
                              || _directories.Any(d => d.StartsWith(prefix, StringComparison.Ordinal));
            if (hasChildren && !recursive)
            {
                return Task.FromException(new IOException($"The directory '{path}' is not empty."));
            }

            foreach (var file in _files.Keys.Where(f => f.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                _files.Remove(file);
            }

            _directories.RemoveWhere(d => d == normalized || d.StartsWith(prefix, StringComparison.Ordinal));
        }

        return Task.CompletedTask;
    }

    private string Normalize(string path) => Paths.GetFullPath(path);
}

/// <summary>
/// 一个吐完给定 stdout 行就退出的进程（探测命令的替身）。
/// </summary>
internal sealed class ScriptedExitProcess(IEnumerable<string> stdout, int exitCode = 0) : ICliProcess
{
    private readonly FakeCliAgentTransport _transport = new(stdout);

    public ICliAgentTransport Transport => _transport;

    public int ProcessId => 4242;

    public bool HasExited => true;

    public int? ExitCode => exitCode;

    public Task TerminateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(exitCode);

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
