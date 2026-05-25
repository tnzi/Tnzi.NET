using Tnzi.AI.Sandbox.Security;
using Tnzi.AI.Security;

namespace Tnzi.AI.Sandbox.Providers.Local;

public class LocalSandbox : ISandbox
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _workspacePath;
    private readonly string _normalizedWorkspacePath;
    private readonly TimeSpan _commandTimeout;
    private readonly long _maxOutputSize;
    private readonly IReadOnlyCollection<string> _deniedCommands;
    private readonly IReadOnlyCollection<string> _deniedCommandPrefixes;
    private readonly IShellCommandAnalyzer? _commandAnalyzer;
    private readonly List<string> _environmentBlacklist;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverrides;
    private readonly object _outputLock = new();

    public string Id { get; }

    public LocalSandbox(string id, string workspacePath, TimeSpan commandTimeout,
        long maxOutputSize, IEnumerable<string>? deniedCommands = null,
        IEnumerable<string>? environmentBlacklist = null,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        IEnumerable<string>? deniedCommandPrefixes = null,
        IShellCommandAnalyzer? commandAnalyzer = null)
    {
        Id = Check.NotNullOrWhiteSpace(id);
        _workspacePath = Check.NotNullOrWhiteSpace(workspacePath);
        _normalizedWorkspacePath = Path.GetFullPath(workspacePath);
        _commandTimeout = commandTimeout;
        _maxOutputSize = maxOutputSize;
        _deniedCommands = (deniedCommands ?? []).ToArray();
        _deniedCommandPrefixes = (deniedCommandPrefixes ?? []).ToArray();
        _commandAnalyzer = commandAnalyzer;
        _environmentBlacklist = (environmentBlacklist ?? []).ToList();
        _environmentOverrides = environmentOverrides;
    }

    public async Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(command);

        if (DeniedCommandMatcher.IsDenied(_commandAnalyzer, command, _deniedCommands, _deniedCommandPrefixes, out var reason))
            return new CommandResult(-1, "", $"Command denied: {reason}");

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            WorkingDirectory = _workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(isWindows ? "/c" : "-c");
        psi.ArgumentList.Add(command);

        foreach (var key in _environmentBlacklist)
        {
            var toRemove = psi.Environment.Keys
                .Where(k => k.Contains(key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var k in toRemove)
                psi.Environment.Remove(k);
        }

        // Apply environment overrides AFTER blacklist filtering — overrides always win.
        if (_environmentOverrides is not null)
        {
            foreach (var (k, v) in _environmentOverrides)
                psi.Environment[k] = v;
        }

        using var process = new Process { StartInfo = psi };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_outputLock)
            {
                if (stdoutBuilder.Length < _maxOutputSize)
                    stdoutBuilder.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_outputLock)
            {
                if (stderrBuilder.Length < _maxOutputSize)
                    stderrBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_commandTimeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new CommandResult(-1, stdoutBuilder.ToString(), "Command timed out");
        }

        return new CommandResult(process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    public async Task<string> ReadFileAsync(string path, CancellationToken ct = default)
    {
        ValidatePathWithinWorkspace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");
        return await File.ReadAllTextAsync(path, ct);
    }

    public async Task WriteFileAsync(string path, string content, bool append = false, CancellationToken ct = default)
    {
        ValidatePathWithinWorkspace(path);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        if (append)
            await File.AppendAllTextAsync(path, content, ct);
        else
            await File.WriteAllTextAsync(path, content, ct);
    }

    public async Task UpdateFileAsync(string path, byte[] content, CancellationToken ct = default)
    {
        ValidatePathWithinWorkspace(path);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(path, content, ct);
    }

    public Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, int maxDepth = 2, CancellationToken ct = default)
    {
        ValidatePathWithinWorkspace(path);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var entries = new List<FileEntry>();
        CollectEntries(path, entries, maxDepth, 0);
        return Task.FromResult<IReadOnlyList<FileEntry>>(entries);
    }

    /// <summary>
    /// Defense-in-depth: ensures the path is within the workspace directory
    /// </summary>
    private void ValidatePathWithinWorkspace(string path)
    {
        Check.NotNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path);
        if (!normalized.StartsWith(_normalizedWorkspacePath + Path.DirectorySeparatorChar, PathComparison)
            && !string.Equals(normalized, _normalizedWorkspacePath, PathComparison))
        {
            throw new SecurityException($"Path is outside sandbox workspace: {path}");
        }
    }

    private static void CollectEntries(string dir, List<FileEntry> entries, int maxDepth, int currentDepth)
    {
        if (currentDepth > maxDepth) return;

        foreach (var d in Directory.GetDirectories(dir))
        {
            var info = new DirectoryInfo(d);
            entries.Add(new FileEntry(info.Name, d, 0, IsDirectory: true));
            CollectEntries(d, entries, maxDepth, currentDepth + 1);
        }
        foreach (var f in Directory.GetFiles(dir))
        {
            var info = new FileInfo(f);
            entries.Add(new FileEntry(info.Name, f, info.Length, IsDirectory: false));
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
