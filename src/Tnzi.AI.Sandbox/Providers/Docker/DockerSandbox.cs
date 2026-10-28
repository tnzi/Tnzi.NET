
namespace Tnzi.AI.Sandbox.Providers.Docker;

/// <summary>
/// Docker container sandbox - executes commands and manages files inside a Docker container
/// via the Docker Engine REST API.
/// </summary>
public sealed class DockerSandbox : ISandbox
{
    /// <summary>
    /// Default denied-substring list applied when the provider did not pass a
    /// configured list. Kept in sync with <c>SandboxModuleOptions.Docker.DeniedCommands</c>.
    /// </summary>
    private static readonly string[] DefaultDeniedCommands = ["rm -rf /", "format c:", "chmod 777 /", "mkfs"];

    /// <summary>
    /// Grace period added on top of <see cref="_commandTimeout"/> when arming the
    /// in-container timeout, so the client-side cancellation always wins the race and
    /// the timeout contract (exit -1 + "Command timed out") stays unchanged. The
    /// in-container timeout is a janitor that runs after we have already walked away,
    /// not a second source of truth for "how long may this command run".
    /// </summary>
    private static readonly TimeSpan InContainerTimeoutGrace = TimeSpan.FromSeconds(5);

    private static readonly StringComparison HostPathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly StringComparer MountNameComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// 容器里挂四个子目录的父路径：<c>/mnt/{workspace,uploads,outputs,skills}</c>，与 agent 看到的虚拟路径逐字相同，
    /// 所以写进脚本、文件内容里的 <c>/mnt/...</c> 在容器里同样成立。见 <see cref="DockerSandboxProvider"/> 的 Binds。
    /// </summary>
    internal const string VirtualMountRoot = "/mnt";

    /// <summary>
    /// 一条 exec 命令里最多携带的 base64 字符数。
    /// </summary>
    /// <remarks>
    /// Linux 对单个 argv 字符串限 <c>MAX_ARG_STRLEN</c>（32 页 = 128 KiB），超出即 <c>E2BIG</c>，exec 根本起不来。
    /// 写文件时内容以 base64 走命令行，比这更长的内容分段落到暂存文件里，命令本身留出路径与引号的余量。
    /// </remarks>
    private const int MaxBase64CharsPerExec = 64 * 1024;

    private readonly HttpClient _httpClient;
    private readonly string _containerId;
    private readonly string _workspacePath;
    private readonly string? _hostWorkspacePath;
    private readonly TimeSpan _commandTimeout;
    private readonly long _maxOutputSize;
    private readonly long _maxFileSize;
    private readonly ILogger _logger;
    private readonly IReadOnlyCollection<string> _deniedCommands;
    private readonly IReadOnlyCollection<string> _deniedCommandPrefixes;
    private readonly IReadOnlyCollection<string> _deniedPatterns;
    private readonly IShellCommandAnalyzer? _commandAnalyzer;
    private readonly Action? _onDisposed;
    private bool _disposed;

    public string Id { get; }

    public DockerSandbox(string id, HttpClient httpClient, string containerId,
        string workspacePath, TimeSpan commandTimeout, long maxOutputSize, ILogger logger,
        IEnumerable<string>? deniedCommands = null,
        Action? onDisposed = null,
        IEnumerable<string>? deniedCommandPrefixes = null,
        IShellCommandAnalyzer? commandAnalyzer = null,
        IEnumerable<string>? deniedPatterns = null,
        long maxFileSize = 0,
        string? hostWorkspacePath = null)
    {
        Id = Check.NotNullOrWhiteSpace(id);
        _httpClient = Check.NotNull(httpClient);
        _containerId = Check.NotNullOrWhiteSpace(containerId);
        _workspacePath = Check.NotNullOrWhiteSpace(workspacePath);
        // 宿主侧挂载源。provider 一定会给；直接构造而不给的（测试）拿不到容器视图，MapPath 退化为恒等。
        _hostWorkspacePath = string.IsNullOrWhiteSpace(hostWorkspacePath)
            ? null
            : Path.TrimEndingDirectorySeparator(RealPathResolver.Resolve(hostWorkspacePath));
        _commandTimeout = commandTimeout;
        _maxOutputSize = maxOutputSize;
        // <= 0 disables the file-size precheck.
        _maxFileSize = maxFileSize;
        _logger = Check.NotNull(logger);
        _deniedCommands = (deniedCommands ?? DefaultDeniedCommands).ToArray();
        _deniedCommandPrefixes = (deniedCommandPrefixes ?? []).ToArray();
        _deniedPatterns = (deniedPatterns ?? []).ToArray();
        _commandAnalyzer = commandAnalyzer;
        _onDisposed = onDisposed;
    }

    /// <summary>
    /// 宿主线程目录下的路径 → 容器里的路径。
    /// </summary>
    /// <remarks>
    /// 只换根、只认前缀，分隔符统一成 <c>/</c>（Windows 宿主给的是反斜杠）：四个子目录在容器里挂在与虚拟路径逐字相同的
    /// <c>/mnt/{workspace,uploads,outputs,skills}</c>（<c>{host}/outputs/a.txt</c> → <c>/mnt/outputs/a.txt</c>），
    /// 其余（线程根本身、根下的散文件）落在线程根的挂载点 <c>/workspace</c> 下。不在挂载源之下的路径抛
    /// <see cref="SecurityException"/> —— 这条路径本不该走到这里（围栏在翻译器里），走到了就说明上游漏了，
    /// 猜一个容器位置只会把漏洞藏起来。
    /// </remarks>
    public string MapPath(string hostPath)
    {
        Check.NotNullOrWhiteSpace(hostPath);
        if (_hostWorkspacePath is null)
        {
            return hostPath;
        }

        // 与 ToPhysical 同一口径地解析链接：DataRoot 本身可能挂在一条符号链接之下
        // （/data → /mnt/disk/data），bash 里的字面 token 只有解析过才对得上解析过的挂载源。
        var full = Path.TrimEndingDirectorySeparator(RealPathResolver.Resolve(hostPath));
        if (string.Equals(full, _hostWorkspacePath, HostPathComparison))
        {
            return _workspacePath;
        }

        if (!full.StartsWith(_hostWorkspacePath + Path.DirectorySeparatorChar, HostPathComparison))
        {
            throw new SecurityException($"Path is outside sandbox workspace: {hostPath}");
        }

        var relative = full[(_hostWorkspacePath.Length + 1)..].Replace('\\', '/');
        // 挂载点名按规范拼写给出：Windows 宿主上 Workspace\a.txt 与 workspace\a.txt 是同一个文件，
        // 容器里的挂载点却区分大小写。
        var separator = relative.IndexOf('/');
        var firstSegment = separator < 0 ? relative : relative[..separator];
        var mount = ThreadDataState.MountNames.FirstOrDefault(m => MountNameComparer.Equals(m, firstSegment));
        if (mount is not null)
        {
            return $"{VirtualMountRoot}/{mount}{(separator < 0 ? string.Empty : relative[separator..])}";
        }

        return $"{_workspacePath.TrimEnd('/')}/{relative}";
    }

    public async Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(command);
        ThrowIfDisposed();

        if (DeniedCommandMatcher.IsDenied(_commandAnalyzer, command, _deniedCommands, _deniedCommandPrefixes, out var reason))
            return new CommandResult(-1, "", $"Command denied: {reason}");

        var execution = await ExecuteCoreAsync(command, _maxOutputSize, ct);
        return execution.Result;
    }

    /// <summary>
    /// Run one of this sandbox's own file-operation commands (<c>cat</c>, <c>mkdir</c>, the base64 writes, <c>find</c>).
    /// </summary>
    /// <remarks>
    /// The command blacklist screens commands an agent asked for. These are built here, with the agent's
    /// path single-quoted and its content base64-encoded, so neither can run as a command, and holding them
    /// to the blacklist only matches the data: the substring <c>mkfs</c> turns up case-insensitively in roughly
    /// one of every million base64 positions, so most writes of a megabyte were refused, and so was reading any
    /// file whose name contained a denied word. The Local provider does these operations with file APIs and
    /// never consults the blacklist either.
    /// </remarks>
    private async Task<CommandResult> ExecuteFileOperationAsync(string command, CancellationToken ct)
    {
        var execution = await ExecuteCoreAsync(command, _maxOutputSize, ct);
        return execution.Result;
    }

    /// <summary>
    /// Run a command and also report whether its stdout was cut at <paramref name="outputLimitBytes"/>.
    /// </summary>
    /// <remarks>
    /// The flag exists for <see cref="ReadFileAsync"/>: a bash caller is served by the notice
    /// appended to the cut stream, but a file read must refuse partial content outright,
    /// because whatever it returns is what <c>str_replace</c> writes back. The command blacklist is
    /// applied by the caller (<see cref="ExecuteCommandAsync"/>), before anything reaches the daemon.
    /// </remarks>
    private async Task<(CommandResult Result, bool StdoutTruncated)> ExecuteCoreAsync(
        string command, long outputLimitBytes, CancellationToken ct)
    {
        _logger.LogDebug("Docker sandbox {Id}: executing command in container {ContainerId}", Id, _containerId);

        // Declared outside the try so the timeout path can return whatever the command
        // already produced before it hung, instead of discarding it.
        var stdout = new ExecStreamCollector(outputLimitBytes);
        var stderr = new ExecStreamCollector(outputLimitBytes);

        try
        {
            // Step 1: Create exec instance. The command is wrapped so the container
            // kills it on timeout - see BuildExecArgv. Note the denial
            // check above ran on the raw command: the wrapper must never be able to
            // launder a blacklisted command past it.
            var execCreateBody = new
            {
                AttachStdout = true,
                AttachStderr = true,
                WorkingDir = _workspacePath,
                Cmd = BuildExecArgv(command)
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_commandTimeout);

            var createResponse = await _httpClient.PostAsJsonAsync(
                $"/containers/{_containerId}/exec", execCreateBody, cts.Token);

            if (!createResponse.IsSuccessStatusCode)
            {
                var error = await createResponse.Content.ReadAsStringAsync(cts.Token);
                return (new CommandResult(-1, "", $"Failed to create exec instance: {error}"), false);
            }

            var createResult = await createResponse.Content.ReadFromJsonAsync<DockerExecCreateResponse>(cts.Token);
            if (createResult is null || string.IsNullOrEmpty(createResult.Id))
                return (new CommandResult(-1, "", "Failed to parse exec create response"), false);

            // Step 2: Start exec instance.
            // ResponseHeadersRead is required, not an optimisation: the default
            // ResponseContentRead buffers the entire exec output into memory before this
            // call returns, which both defeats the _maxOutputSize cap (the memory is
            // already spent by the time we count it) and leaves us with nothing to report
            // when a long-running command hangs part-way through its output.
            using var startRequest = new HttpRequestMessage(
                HttpMethod.Post, $"/exec/{createResult.Id}/start")
            {
                Content = JsonContent.Create(new { Detach = false, Tty = false })
            };

            using var startResponse = await _httpClient.SendAsync(
                startRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!startResponse.IsSuccessStatusCode)
            {
                var error = await startResponse.Content.ReadAsStringAsync(cts.Token);
                return (new CommandResult(-1, "", $"Failed to start exec: {error}"), false);
            }

            // Step 3: Read multiplexed output stream
            await ReadOutputAsync(startResponse, stdout, stderr, cts.Token);
            stdout.Complete();
            stderr.Complete();

            // Step 4: Inspect exec to get exit code. An exit status that cannot be read is a
            // failure, never a success: defaulting to 0 turned a failed `cat` into "this file
            // is empty", and its empty content into what str_replace writes back.
            var exitStatus = await InspectExitCodeAsync(createResult.Id, cts.Token);
            if (exitStatus.ExitCode is not { } exitCode)
            {
                _logger.LogWarning("Docker sandbox {Id}: exit status of exec {ExecId} unavailable: {Reason}",
                    Id, createResult.Id, exitStatus.Problem);
                var notice = $"Unable to read the command's exit status from Docker: {exitStatus.Problem}.";
                var errorText = stderr.ToDisplayString();
                return (new CommandResult(-1, stdout.ToDisplayString(),
                    string.IsNullOrWhiteSpace(errorText) ? notice : $"{notice} {errorText}"), stdout.Truncated);
            }

            return (new CommandResult(exitCode, stdout.ToDisplayString(), stderr.ToDisplayString()), stdout.Truncated);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Mirrors the Local provider contract: the timeout notice is a prefix, never a
            // substitute. A command that hung mid-stream has usually already said why on
            // stderr, and that is the only evidence of where it stopped.
            var notice = $"Command timed out after {_commandTimeout.TotalSeconds:0.##}s.";
            var tail = stderr.ToDisplayString();
            return (new CommandResult(-1, stdout.ToDisplayString(),
                string.IsNullOrWhiteSpace(tail) ? notice : $"{notice} {tail}"), stdout.Truncated);
        }
    }

    /// <summary>
    /// Read the exit code of a finished exec, or say why it could not be read.
    /// </summary>
    /// <remarks>
    /// The daemon records the exit code when it handles the process-exit event, which can land
    /// a moment after the output stream has closed; until then it reports <c>Running</c> with a
    /// null <c>ExitCode</c>. A few short retries absorb that window; anything still unknown after
    /// them is reported as unknown, and the caller turns that into a failed command.
    /// </remarks>
    private async Task<(int? ExitCode, string? Problem)> InspectExitCodeAsync(string execId, CancellationToken ct)
    {
        const int maxAttempts = 4;
        var problem = "no response";

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var response = await _httpClient.GetAsync($"/exec/{execId}/json", ct);
            if (!response.IsSuccessStatusCode)
                return (null, $"exec inspect returned HTTP {(int)response.StatusCode}");

            DockerExecInspectResponse? inspect;
            try
            {
                inspect = await response.Content.ReadFromJsonAsync<DockerExecInspectResponse>(ct);
            }
            catch (JsonException ex)
            {
                return (null, $"exec inspect response could not be parsed ({ex.Message})");
            }

            if (inspect is { Running: false, ExitCode: { } exitCode })
                return (exitCode, null);

            problem = inspect is null ? "exec inspect returned no body" : "the exec has not reported an exit code";
            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), ct);
        }

        return (null, problem);
    }

    /// <summary>
    /// The exec argv: the command run by <c>/bin/sh -c</c>, wrapped so the container terminates it when it
    /// outruns the timeout.
    /// </summary>
    /// <remarks>
    /// The Docker Engine API has no kill-exec endpoint: once we stop reading the exec
    /// stream the process inside the container keeps running, still holding the
    /// <c>PidsLimit</c>, memory and CPU quota the caller believes it released on
    /// timeout. Repeated timeouts therefore leak the container to exhaustion while
    /// every call site was told the work had ended. Delegating the kill to the
    /// container's own <c>timeout</c> is the only mechanism that survives us walking away.
    /// <para>
    /// Two deliberate choices: the wrapper degrades to running the command unchanged
    /// when the image ships no <c>timeout</c> (an image without coreutils/busybox must
    /// not lose the ability to run commands at all - it just keeps the old leak), and
    /// <c>-s KILL</c> is used because by the time this fires we have already abandoned
    /// the result, so there is nothing to gain from a graceful shutdown. GNU
    /// <c>timeout</c> signals the whole process group, busybox only the direct child;
    /// grandchildren of a busybox image can still survive.
    /// </para>
    /// <para>
    /// The command travels as its own argument (<c>$1</c> of the wrapper script), never spliced into it:
    /// the kernel caps every single argv string at <c>MAX_ARG_STRLEN</c> (128 KiB on Linux), and a wrapper
    /// that repeated the command in both of its branches halved what a command could be. It also needs
    /// no re-quoting, so a quote in the command cannot reach the wrapper's own syntax.
    /// </para>
    /// </remarks>
    private string[] BuildExecArgv(string command)
    {
        // No positive budget means "do not time out" (TimeSpan.Zero or Timeout.InfiniteTimeSpan,
        // both of which CancelAfter treats as never-cancel). There is no deadline to hand the
        // container, and inventing one would kill commands the caller deliberately left unbounded.
        if (_commandTimeout <= TimeSpan.Zero) return ["/bin/sh", "-c", command];

        var seconds = (long)Math.Ceiling((_commandTimeout + InContainerTimeoutGrace).TotalSeconds);
        var wrapper = "if command -v timeout >/dev/null 2>&1; then "
                    + $"timeout -s KILL {seconds} /bin/sh -c \"$1\"; "
                    + "else /bin/sh -c \"$1\"; fi";

        // sh -c '<wrapper>' sh '<command>': the second word becomes $0, the command $1.
        return ["/bin/sh", "-c", wrapper, "sh", command];
    }

    public async Task<string> ReadFileAsync(string path, int? offset = null, int? limit = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(path);
        ThrowIfDisposed();

        if (SensitiveFileMatcher.IsDenied(path, _deniedPatterns, out var pattern))
            throw new SecurityException($"Access denied: file matches sensitive pattern '{pattern}'");

        var escaped = EscapePath(path);

        // Size precheck - reject before streaming a multi-GB file out of the
        // container. `wc -c` is portable across GNU coreutils and busybox.
        if (_maxFileSize > 0)
        {
            var sizeResult = await ExecuteFileOperationAsync($"wc -c < '{escaped}'", ct);
            if (sizeResult.ExitCode == 0)
            {
                // wc ran: a non-numeric result means we genuinely cannot determine the
                // size of an accessible file → fail closed rather than stream it unbounded.
                if (!long.TryParse(sizeResult.Output.Trim(), out var size))
                    throw new SecurityException("Unable to determine file size for sandbox limit enforcement.");
                if (size > _maxFileSize)
                    throw new SecurityException(
                        $"File exceeds the maximum readable size ({size} > {_maxFileSize} bytes)");
            }
            else
            {
                // wc failed - most often a missing/inaccessible file, which the read below
                // surfaces as FileNotFound. Log so an unverified size read isn't fully silent.
                _logger.LogWarning(
                    "Sandbox size precheck (wc -c) failed for {Path} (exit {ExitCode}); size limit not enforced for this read.",
                    path, sizeResult.ExitCode);
            }
        }

        // When a line window is requested, slice inside the container with
        // sed so only the requested lines cross the exec stream.
        string readCommand;
        if (offset is not null || limit is not null)
        {
            var start = Math.Max(1, offset ?? 1);
            var end = limit is not null ? (long)start + limit.Value - 1 : (long?)null;
            readCommand = end is not null
                ? $"sed -n '{start},{end}p' '{escaped}'"
                : $"sed -n '{start},$p' '{escaped}'";
        }
        else
        {
            readCommand = $"cat '{escaped}'";
        }

        // A file read is bounded by the file-size limit checked above, not by the bash output
        // cap: the Local provider returns any file under MaxFileSizeBytes in full, and so must
        // this one. Whatever still overflows (the precheck could not run, or the file grew in
        // between) is refused, never returned in part: what read_file returns is what
        // str_replace and a read-then-write_file round trip write back.
        var readLimit = _maxFileSize > 0 ? Math.Max(_maxFileSize, _maxOutputSize) : _maxOutputSize;
        var (result, truncated) = await ExecuteCoreAsync(readCommand, readLimit, ct);
        if (result.ExitCode != 0)
            throw new FileNotFoundException($"File not found or not readable: {path}. Error: {result.Error}");
        if (truncated)
            throw new IOException(
                $"File content exceeds the {readLimit}-byte read limit of this sandbox: {path}. Read it in parts with offset and limit.");

        return result.Output;
    }

    public async Task WriteFileAsync(string path, string content, bool append = false, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(path);
        Check.NotNull(content);
        ThrowIfDisposed();

        await WriteBytesAsync(path, Encoding.UTF8.GetBytes(content), append, "write", ct);
    }

    public async Task UpdateFileAsync(string path, byte[] content, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(path);
        Check.NotNull(content);
        ThrowIfDisposed();

        await WriteBytesAsync(path, content, append: false, "update", ct);
    }

    /// <summary>
    /// Write <paramref name="bytes"/> to <paramref name="path"/> inside the container, as the container user.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The content crosses as base64 on the command line (<c>printf</c> would interpret a <c>%</c> in raw
    /// text). A command line has a hard per-argument ceiling (see <see cref="MaxBase64CharsPerExec"/>), so
    /// content that does not fit one command is staged: its base64 is appended part by part to a hidden
    /// file next to the target, and only once every part has landed is it decoded into the target in one
    /// step. Writing the parts straight into the target would leave a truncated file behind whenever a later
    /// part failed, and <c>str_replace</c> writes the whole file back through here.
    /// </para>
    /// <para>
    /// Exec is used rather than the Engine's archive upload so the writer stays the container user, the
    /// same identity as every <c>bash</c> command; the archive endpoint extracts as the daemon (root).
    /// </para>
    /// </remarks>
    private async Task WriteBytesAsync(string path, byte[] bytes, bool append, string verb, CancellationToken ct)
    {
        var escapedPath = EscapePath(path);
        var dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : ".";
        await ExecuteFileOperationAsync($"mkdir -p '{EscapePath(dir)}'", ct);

        var base64 = Convert.ToBase64String(bytes);
        var op = append ? ">>" : ">";

        if (base64.Length <= MaxBase64CharsPerExec)
        {
            var result = await ExecuteFileOperationAsync($"printf '%s' '{base64}' | base64 -d {op} '{escapedPath}'", ct);
            if (result.ExitCode != 0)
                throw new IOException($"Failed to {verb} file: {path}. Error: {result.Error}");
            return;
        }

        var staging = EscapePath($"{dir}/.tnzi-write-{Guid.NewGuid():N}.b64");
        try
        {
            for (var offset = 0; offset < base64.Length; offset += MaxBase64CharsPerExec)
            {
                var part = base64.Substring(offset, Math.Min(MaxBase64CharsPerExec, base64.Length - offset));
                var redirect = offset == 0 ? ">" : ">>";
                var staged = await ExecuteFileOperationAsync($"printf '%s' '{part}' {redirect} '{staging}'", ct);
                if (staged.ExitCode != 0)
                    throw new IOException($"Failed to {verb} file: {path}. Error: {staged.Error}");
            }

            var decoded = await ExecuteFileOperationAsync($"base64 -d '{staging}' {op} '{escapedPath}'", ct);
            if (decoded.ExitCode != 0)
                throw new IOException($"Failed to {verb} file: {path}. Error: {decoded.Error}");
        }
        finally
        {
            // Not tied to the caller's token: a cancelled write is exactly when the staging file is left over.
            // The exec is still bounded by the command timeout.
            var removed = await ExecuteFileOperationAsync($"rm -f '{staging}'", CancellationToken.None);
            if (removed.ExitCode != 0)
            {
                _logger.LogWarning("Docker sandbox {Id}: could not remove the staging file next to {Path}: {Error}",
                    Id, path, removed.Error);
            }
        }
    }

    public async Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, int maxDepth = 2, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(path);
        ThrowIfDisposed();

        // 使用 find 命令列出目录内容
        var result = await ExecuteFileOperationAsync(
            $"find '{EscapePath(path)}' -maxdepth {maxDepth} -printf '%y\\t%s\\t%p\\n' 2>/dev/null", ct);

        if (result.ExitCode != 0)
            throw new DirectoryNotFoundException($"Directory not found: {path}. Error: {result.Error}");

        var entries = new List<FileEntry>();
        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 3);
            if (parts.Length < 3) continue;

            var type = parts[0];
            var size = long.TryParse(parts[1], out var s) ? s : 0;
            var fullPath = parts[2];
            var name = fullPath.Contains('/') ? fullPath[(fullPath.LastIndexOf('/') + 1)..] : fullPath;

            // 跳过根目录本身
            if (fullPath == path) continue;

            var isDirectory = type == "d";
            // Hide sensitive files from listings (mirrors read-time denial). Only
            // files are filtered - directory names are not matched as patterns.
            if (!isDirectory && SensitiveFileMatcher.IsDenied(fullPath, _deniedPatterns, out _))
                continue;

            entries.Add(new FileEntry(name, fullPath, size, IsDirectory: isDirectory));
        }

        return entries;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            // Kill and remove in one call. No graceful stop first: PID 1 is the keep-alive command, which
            // has no SIGTERM handler, and a namespace's init only receives signals it handles, so a stop
            // always waited out its whole timeout before the kill. Every run that used the sandbox returned
            // that much later and held its container slot that long. By now every command's result has been
            // collected and the files live on the host bind mount; there is nothing left to shut down.
            _logger.LogDebug("Docker sandbox {Id}: removing container {ContainerId}", Id, _containerId);
            using var removeResponse = await _httpClient.DeleteAsync($"/containers/{_containerId}?force=true");

            if (removeResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Already gone (removed by the daemon on exit, or by an operator): the goal is reached.
                _logger.LogDebug("Docker sandbox {Id}: container {ContainerId} was already removed", Id, _containerId);
            }
            else if (!removeResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Docker sandbox {Id}: failed to remove container {ContainerId}: HTTP {StatusCode}",
                    Id, _containerId, (int)removeResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Docker sandbox {Id}: error during container cleanup for {ContainerId}", Id, _containerId);
        }
        finally
        {
            // Always release provider-side resources (semaphore + active container map)
            // even when container cleanup fails, to prevent slot exhaustion.
            try { _onDisposed?.Invoke(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Docker sandbox {Id}: onDisposed callback failed", Id); }
        }

        _logger.LogDebug("Docker sandbox {Id} disposed (container {ContainerId})", Id, _containerId);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Read Docker exec multiplexed output stream into the caller's stdout/stderr builders.
    /// Docker multiplexed stream format: [8-byte header][payload]
    /// Header: [stream type (1 byte)][0,0,0][size (4 bytes big-endian)]
    /// </summary>
    /// <remarks>
    /// The collectors belong to the caller so that a cancellation part-way through a long
    /// running command still surfaces the output collected up to that point.
    /// </remarks>
    private static async Task ReadOutputAsync(HttpResponseMessage response,
        ExecStreamCollector stdout, ExecStreamCollector stderr, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var header = new byte[8];

        while (true)
        {
            var bytesRead = await ReadExactAsync(stream, header, ct);
            if (bytesRead == 0) break;
            if (bytesRead < 8) break;

            var streamType = header[0]; // 1 = stdout, 2 = stderr
            var payloadSize = (header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7];

            if (payloadSize <= 0) continue;

            var payload = new byte[payloadSize];
            var payloadRead = await ReadExactAsync(stream, payload, ct);
            if (payloadRead == 0) break;

            // Frames past the cap are still read off the wire (the stream has to be drained for
            // the exec to finish), the collector just stops keeping them.
            var target = streamType == 2 ? stderr : stdout;
            target.Append(payload.AsSpan(0, payloadRead));
        }
    }

    /// <summary>
    /// One exec output stream (stdout or stderr), decoded as one continuous UTF-8 text.
    /// </summary>
    /// <remarks>
    /// Docker cuts frames wherever its read of the exec pipe stopped, not on character
    /// boundaries, so a multi-byte character regularly straddles two frames. Decoding each
    /// frame on its own turns both halves into U+FFFD; one stateful decoder per stream keeps
    /// the pending bytes between frames. The cap counts bytes (the option is
    /// <c>MaxOutputSizeBytes</c>), and a cut is remembered so it can be announced or refused.
    /// </remarks>
    private sealed class ExecStreamCollector(long limitBytes)
    {
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _text = new();
        private long _bytesKept;

        public bool Truncated { get; private set; }

        public void Append(ReadOnlySpan<byte> payload)
        {
            if (Truncated) return;

            var room = Math.Max(0, limitBytes - _bytesKept);
            if (payload.Length > room)
            {
                payload = payload[..(int)room];
                Truncated = true;
            }

            if (payload.IsEmpty) return;

            _bytesKept += payload.Length;
            var chars = new char[_decoder.GetCharCount(payload, flush: false)];
            _decoder.GetChars(payload, chars, flush: false);
            _text.Append(chars);
        }

        /// <summary>
        /// The stream ended. A complete stream that ends inside a character is genuinely
        /// malformed and gets its replacement character; a cut stream ends inside one only
        /// because of the cut, so the half character is dropped rather than shown as garbage.
        /// </summary>
        public void Complete()
        {
            if (Truncated) return;

            var chars = new char[_decoder.GetCharCount([], flush: true)];
            _decoder.GetChars([], chars, flush: true);
            _text.Append(chars);
        }

        public string ToDisplayString() =>
            Truncated ? OutputTruncation.AppendNotice(_text.ToString(), limitBytes) : _text.ToString();
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) return totalRead;
            totalRead += read;
        }
        return totalRead;
    }

    /// <summary>
    /// Escape a value for embedding inside a single-quoted POSIX shell word.
    /// </summary>
    private static string EscapeSingleQuotes(string value) => value.Replace("'", "'\\''");

    private static string EscapePath(string path) => EscapeSingleQuotes(path);

    // Docker API response models
    private sealed class DockerExecCreateResponse
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class DockerExecInspectResponse
    {
        // Null while the exec is still running (or before the daemon has recorded its exit).
        [JsonPropertyName("ExitCode")]
        public int? ExitCode { get; set; }

        [JsonPropertyName("Running")]
        public bool Running { get; set; }
    }
}
