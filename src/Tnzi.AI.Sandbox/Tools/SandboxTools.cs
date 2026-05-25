using Tnzi.AI.Sandbox.Events;
using Tnzi.AI.Sandbox.Quota;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

namespace Tnzi.AI.Sandbox.Tools;

public class SandboxTools
{
    /// <summary>
    /// Output/stderr truncation budget for the audit/event payload — keeps
    /// the audit row bounded while leaving the agent's return value intact.
    /// </summary>
    private const int MaxAuditOutputBytes = 2048;

    private const string DenialPrefix = "Command denied:";

    private readonly IVirtualPathTranslator _translator;
    private readonly ILogger<SandboxTools> _logger;
    private readonly IEventBus? _eventBus;
    private readonly ICurrentUser? _currentUser;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IThreadResourceQuota? _quota;

    public SandboxTools(IVirtualPathTranslator translator, ILogger<SandboxTools> logger,
        IEventBus? eventBus = null,
        ICurrentUser? currentUser = null,
        ICurrentTenant? currentTenant = null,
        IThreadResourceQuota? quota = null)
    {
        _translator = Check.NotNull(translator);
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _quota = quota;
    }

    [Description("Execute a bash command in the sandbox environment")]
    public async Task<object> BashAsync(ISandbox sandbox, Guid threadId, string command)
    {
        Check.NotNullOrWhiteSpace(command);

        // Thread-level resource quota check — pre-flight before reaching the shell.
        // Quota denials are reported back through the same event/audit pipeline as
        // command-blacklist denials so dashboards see a unified "denied" stream.
        if (_quota is not null)
        {
            var quotaCheck = await _quota.CheckAsync(threadId);
            if (!quotaCheck.IsAllowed)
            {
                var denyReason = quotaCheck.Reason ?? "Thread quota exceeded";
                _logger.LogInformation(
                    "Sandbox bash blocked by thread quota for thread {ThreadId}: {Reason}",
                    threadId, denyReason);

                var denyResult = new CommandResult(-1, string.Empty, $"Command denied: {denyReason}");
                await PublishExecutionEventAsync(sandbox, threadId, command, denyResult, DateTime.UtcNow, 0);

                return new { stdout = string.Empty, stderr = denyResult.Error, exit_code = -1, note = (string?)null };
            }
        }

        var translatedCommand = TranslatePathsInCommand(command, threadId);

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var result = await sandbox.ExecuteCommandAsync(translatedCommand);
        stopwatch.Stop();

        // Accumulate usage even on non-zero exits so a busy-looping agent cannot
        // dodge the cap by failing fast. Denials (handled above) skip this path
        // because their cost is intentionally not charged against the cap.
        if (_quota is not null)
        {
            var outputBytes = (result.Output?.Length ?? 0) + (result.Error?.Length ?? 0);
            await _quota.RecordExecutionAsync(threadId, stopwatch.ElapsedMilliseconds, outputBytes);
        }

        var semanticNote = CommandSemantics.InterpretExitCode(command, result.ExitCode);

        await PublishExecutionEventAsync(sandbox, threadId, command, result, startedAt, stopwatch.ElapsedMilliseconds);

        return new { stdout = result.Output, stderr = result.Error, exit_code = result.ExitCode, note = semanticNote };
    }

    private async Task PublishExecutionEventAsync(
        ISandbox sandbox, Guid threadId, string command,
        CommandResult result, DateTime startedAt, long durationMs)
    {
        if (_eventBus is null) return;

        try
        {
            var denied = result.ExitCode == -1
                && !string.IsNullOrEmpty(result.Error)
                && result.Error.StartsWith(DenialPrefix, StringComparison.Ordinal);

            await _eventBus.PublishAsync(new SandboxCommandExecutedEvent
            {
                ThreadId = threadId,
                UserId = _currentUser?.Id,
                TenantId = _currentTenant?.Id,
                SandboxId = sandbox.Id,
                Command = command,
                ExitCode = result.ExitCode,
                Output = Truncate(result.Output),
                Stderr = Truncate(result.Error),
                DurationMs = durationMs,
                Denied = denied,
                DenialReason = denied ? result.Error : null,
                ExecutedAt = startedAt
            });
        }
        catch (Exception ex)
        {
            // Silent catch — observability publication must not break the agent flow.
            _logger.LogWarning(ex, "Failed to publish SandboxCommandExecutedEvent for sandbox {SandboxId}", sandbox.Id);
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= MaxAuditOutputBytes
            ? value
            : string.Concat(value.AsSpan(0, MaxAuditOutputBytes), "...[truncated]");
    }

    [Description("List contents of a directory in the sandbox")]
    public async Task<object> ListDirectoryAsync(ISandbox sandbox, Guid threadId, string path, int maxDepth = 2)
    {
        var physicalPath = _translator.ToPhysical(path, threadId);
        var entries = await sandbox.ListDirectoryAsync(physicalPath, maxDepth);
        return new
        {
            path,
            entries = entries.Select(e => new
            {
                name = e.Name,
                type = e.IsDirectory ? "directory" : "file",
                size = e.Size
            }).ToArray()
        };
    }

    [Description("Read the contents of a file in the sandbox")]
    public async Task<object> ReadFileAsync(ISandbox sandbox, Guid threadId, string path, int? offset = null, int? limit = null)
    {
        var physicalPath = _translator.ToPhysical(path, threadId);
        var content = await sandbox.ReadFileAsync(physicalPath);

        if (offset.HasValue || limit.HasValue)
        {
            var lines = content.Split('\n');
            var start = Math.Max(0, (offset ?? 1) - 1);
            var count = limit ?? lines.Length;
            content = string.Join('\n', lines.Skip(start).Take(count));
        }

        return new { path, content };
    }

    [Description("Write content to a file in the sandbox. Creates directories if needed.")]
    public async Task<object> WriteFileAsync(ISandbox sandbox, Guid threadId, string path, string content, bool append = false)
    {
        var physicalPath = _translator.ToPhysical(path, threadId);
        await sandbox.WriteFileAsync(physicalPath, content, append);
        return new { success = true, path };
    }

    [Description("Replace a string in a file in the sandbox")]
    public async Task<object> StrReplaceAsync(ISandbox sandbox, Guid threadId, string path, string oldString, string newString)
    {
        var physicalPath = _translator.ToPhysical(path, threadId);
        var content = await sandbox.ReadFileAsync(physicalPath);

        if (!content.Contains(oldString))
            return new { success = false, error = $"String '{oldString}' not found in file" };

        var updated = content.Replace(oldString, newString);
        await sandbox.WriteFileAsync(physicalPath, updated);
        return new { success = true, path, replacements = content.Split(oldString).Length - 1 };
    }

    private string TranslatePathsInCommand(string command, Guid threadId)
    {
        var threadDir = _translator.GetThreadDirectory(threadId);
        return command
            .Replace("/mnt/workspace", Path.Combine(threadDir, "workspace"))
            .Replace("/mnt/uploads", Path.Combine(threadDir, "uploads"))
            .Replace("/mnt/outputs", Path.Combine(threadDir, "outputs"))
            .Replace("/mnt/skills", Path.Combine(threadDir, "skills"));
    }
}
