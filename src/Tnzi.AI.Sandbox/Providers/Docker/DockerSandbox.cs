using Tnzi.AI.Sandbox.Security;
using Tnzi.AI.Security;

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

    private readonly HttpClient _httpClient;
    private readonly string _containerId;
    private readonly string _workspacePath;
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
        long maxFileSize = 0)
    {
        Id = Check.NotNullOrWhiteSpace(id);
        _httpClient = Check.NotNull(httpClient);
        _containerId = Check.NotNullOrWhiteSpace(containerId);
        _workspacePath = Check.NotNullOrWhiteSpace(workspacePath);
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

    public async Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(command);
        ThrowIfDisposed();

        if (DeniedCommandMatcher.IsDenied(_commandAnalyzer, command, _deniedCommands, _deniedCommandPrefixes, out var reason))
            return new CommandResult(-1, "", $"Command denied: {reason}");

        _logger.LogDebug("Docker sandbox {Id}: executing command in container {ContainerId}", Id, _containerId);

        try
        {
            // Step 1: Create exec instance
            var execCreateBody = new
            {
                AttachStdout = true,
                AttachStderr = true,
                WorkingDir = _workspacePath,
                Cmd = new[] { "/bin/sh", "-c", command }
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_commandTimeout);

            var createResponse = await _httpClient.PostAsJsonAsync(
                $"/containers/{_containerId}/exec", execCreateBody, cts.Token);

            if (!createResponse.IsSuccessStatusCode)
            {
                var error = await createResponse.Content.ReadAsStringAsync(cts.Token);
                return new CommandResult(-1, "", $"Failed to create exec instance: {error}");
            }

            var createResult = await createResponse.Content.ReadFromJsonAsync<DockerExecCreateResponse>(cts.Token);
            if (createResult is null || string.IsNullOrEmpty(createResult.Id))
                return new CommandResult(-1, "", "Failed to parse exec create response");

            // Step 2: Start exec instance
            var startBody = new { Detach = false, Tty = false };
            var startResponse = await _httpClient.PostAsJsonAsync(
                $"/exec/{createResult.Id}/start", startBody, cts.Token);

            if (!startResponse.IsSuccessStatusCode)
            {
                var error = await startResponse.Content.ReadAsStringAsync(cts.Token);
                return new CommandResult(-1, "", $"Failed to start exec: {error}");
            }

            // Step 3: Read multiplexed output stream
            var rawOutput = await ReadOutputAsync(startResponse, cts.Token);

            // Step 4: Inspect exec to get exit code
            var inspectResponse = await _httpClient.GetAsync($"/exec/{createResult.Id}/json", cts.Token);
            var exitCode = 0;
            if (inspectResponse.IsSuccessStatusCode)
            {
                var inspectResult = await inspectResponse.Content.ReadFromJsonAsync<DockerExecInspectResponse>(cts.Token);
                exitCode = inspectResult?.ExitCode ?? 0;
            }

            return new CommandResult(exitCode, rawOutput.Stdout, rawOutput.Stderr);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CommandResult(-1, "", "Command timed out");
        }
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
            var sizeResult = await ExecuteCommandAsync($"wc -c < '{escaped}'", ct);
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

        var result = await ExecuteCommandAsync(readCommand, ct);
        if (result.ExitCode != 0)
            throw new FileNotFoundException($"File not found or not readable: {path}. Error: {result.Error}");

        return result.Output;
    }

    public async Task WriteFileAsync(string path, string content, bool append = false, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(path);
        ThrowIfDisposed();

        var escapedPath = EscapePath(path);
        // 确保目录存在
        var dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : ".";
        await ExecuteCommandAsync($"mkdir -p '{EscapePath(dir)}'", ct);

        // 使用 base64 编码写入，避免 printf 格式化注入（% 字符被 printf 解释）
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        var op = append ? ">>" : ">";
        var cmd = $"echo '{base64}' | base64 -d {op} '{escapedPath}'";

        var result = await ExecuteCommandAsync(cmd, ct);
        if (result.ExitCode != 0)
            throw new IOException($"Failed to write file: {path}. Error: {result.Error}");
    }

    public async Task UpdateFileAsync(string path, byte[] content, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(path);
        Check.NotNull(content);
        ThrowIfDisposed();

        var escapedPath = EscapePath(path);
        var dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : ".";
        await ExecuteCommandAsync($"mkdir -p '{EscapePath(dir)}'", ct);

        // Base64 编码写入二进制内容
        var base64 = Convert.ToBase64String(content);
        var result = await ExecuteCommandAsync($"echo '{base64}' | base64 -d > '{escapedPath}'", ct);
        if (result.ExitCode != 0)
            throw new IOException($"Failed to update file: {path}. Error: {result.Error}");
    }

    public async Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, int maxDepth = 2, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(path);
        ThrowIfDisposed();

        // 使用 find 命令列出目录内容
        var result = await ExecuteCommandAsync(
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
            // Stop the container (timeout 10s)
            _logger.LogDebug("Docker sandbox {Id}: stopping container {ContainerId}", Id, _containerId);
            var stopResponse = await _httpClient.PostAsync(
                $"/containers/{_containerId}/stop?t=10", null);

            if (!stopResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Docker sandbox {Id}: failed to stop container {ContainerId}: HTTP {StatusCode}",
                    Id, _containerId, (int)stopResponse.StatusCode);
            }

            // Remove the container
            var removeResponse = await _httpClient.DeleteAsync(
                $"/containers/{_containerId}?force=true");

            if (!removeResponse.IsSuccessStatusCode)
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
    /// Read Docker exec multiplexed output stream into stdout/stderr.
    /// Docker multiplexed stream format: [8-byte header][payload]
    /// Header: [stream type (1 byte)][0,0,0][size (4 bytes big-endian)]
    /// </summary>
    private async Task<(string Stdout, string Stderr)> ReadOutputAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

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

            var text = Encoding.UTF8.GetString(payload, 0, payloadRead);
            var target = streamType == 2 ? stderr : stdout;

            if (target.Length < _maxOutputSize)
                target.Append(text);
        }

        return (stdout.ToString(), stderr.ToString());
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

    private static string EscapePath(string path) => path.Replace("'", "'\\''");

    // Docker API response models
    private sealed class DockerExecCreateResponse
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class DockerExecInspectResponse
    {
        [JsonPropertyName("ExitCode")]
        public int ExitCode { get; set; }
    }
}
