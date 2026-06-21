using Tnzi.AI.Infrastructure;
using Tnzi.AI.Sandbox.Events;
using Tnzi.AI.Sandbox.Quota;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

namespace Tnzi.AI.Sandbox.Tools;

/// <summary>
/// 沙箱工具组 — bash / ls / read_file / write_file / str_replace 五个工具。
/// </summary>
/// <remarks>
/// <para>
/// 工具方法签名只包含 LLM 可见参数（进入 JSON schema）。运行环境
/// （<see cref="ISandbox"/> + threadId）不在签名中，而是在调用时从
/// <see cref="IAgentExecutionContextAccessor"/> 解析 — 由
/// <c>SandboxMiddleware</c> 在每次 Agent 运行期间发布
/// <see cref="SandboxToolEnvironment"/>（finally 移除）。
/// </para>
/// <para>
/// 环境缺失（中间件未运行 / 请求无线程 / 子代理裸执行器路径）时，
/// 工具返回结构化英文错误对象，绝不抛出异常。
/// </para>
/// </remarks>
[AIToolGroup("sandbox", "Sandbox", "Execute commands and manage files in isolated sandbox environment")]
public class SandboxTools : IAIToolProvider
{
    /// <summary>
    /// Output/stderr truncation budget for the audit/event payload — keeps
    /// the audit row bounded while leaving the agent's return value intact.
    /// </summary>
    private const int MaxAuditOutputBytes = 2048;

    private const string DenialPrefix = "Command denied:";

    private const string SandboxUnavailableMessage =
        "Sandbox is not available in this execution context. Sandbox tools require an active sandbox " +
        "provisioned per agent run by the sandbox middleware — ensure the sandbox module is enabled " +
        "(AI:Sandbox:Enabled) and the request carries a thread id.";

    private readonly IVirtualPathTranslator _translator;
    private readonly ILogger<SandboxTools> _logger;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly IEventBus? _eventBus;
    private readonly ICurrentUser? _currentUser;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IThreadResourceQuota? _quota;

    public SandboxTools(IVirtualPathTranslator translator, ILogger<SandboxTools> logger,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        IEventBus? eventBus = null,
        ICurrentUser? currentUser = null,
        ICurrentTenant? currentTenant = null,
        IThreadResourceQuota? quota = null)
    {
        _translator = Check.NotNull(translator);
        _logger = Check.NotNull(logger);
        _executionContextAccessor = executionContextAccessor;
        _eventBus = eventBus;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _quota = quota;
    }

    [AIFunction("bash",
        "Execute a bash command in the sandbox environment. Under the Local provider " +
        "the shell is NOT filesystem-jailed: it is protected only by the command blacklist " +
        "and a best-effort path check on translated /mnt paths. Use the Docker provider for " +
        "strong isolation.",
        SearchHint = "shell command execute terminal run script")]
    public async Task<object> BashAsync(
        [Description("The shell command to execute")] string command,
        CancellationToken ct = default)
    {
        if (ResolveEnvironment() is not { } env) return SandboxUnavailable();
        if (string.IsNullOrWhiteSpace(command))
            return new { error = "Command cannot be empty" };

        try
        {
            return await ExecuteBashCoreAsync(env.Sandbox, env.ThreadId, command, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sandbox tool 'bash' failed for thread {ThreadId}", env.ThreadId);
            return new { error = $"bash failed: {ex.Message}" };
        }
    }

    private async Task<object> ExecuteBashCoreAsync(ISandbox sandbox, Guid threadId, string command, CancellationToken ct)
    {
        var translatedCommand = TranslatePathsInCommand(command, threadId);

        // Best-effort jail check for the Local provider: the translated command is
        // handed verbatim to a host shell, so if a translated /mnt path resolves
        // outside the thread directory (via `..` segments embedded in the virtual
        // path) refuse the command rather than let it escape. This is NOT a true
        // jail — it cannot reason about shell-constructed paths — but it closes the
        // obvious `/mnt/workspace/../../../etc/passwd` style bypass. Runs before the
        // quota reservation so an escaping command does not consume a quota slot.
        if (!IsTranslatedCommandWithinThreadDir(translatedCommand, threadId))
        {
            const string escapeReason = "Command references a path outside the sandbox thread directory";
            _logger.LogWarning(
                "Sandbox bash blocked by path-escape guard for thread {ThreadId}", threadId);

            var escapeResult = new CommandResult(-1, string.Empty, $"Command denied: {escapeReason}");
            await PublishExecutionEventAsync(sandbox, threadId, command, escapeResult, DateTime.UtcNow, 0);

            return new { stdout = string.Empty, stderr = escapeResult.Error, exit_code = -1, note = (string?)null };
        }

        // Thread-level resource quota check — pre-flight before reaching the shell.
        // CheckAsync atomically reserves a command slot (hard cap); quota denials are
        // reported back through the same event/audit pipeline as command-blacklist
        // denials so dashboards see a unified "denied" stream.
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

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var result = await sandbox.ExecuteCommandAsync(translatedCommand, ct);
        stopwatch.Stop();

        // Record the soft-cap cost (duration + output bytes) even on non-zero exits
        // so a busy-looping agent cannot dodge the caps by failing fast. The command
        // count is already reserved by CheckAsync above (hard cap); RecordExecutionAsync
        // deliberately does not touch it. Denials (handled above) return early and
        // therefore never reach this accounting.
        if (_quota is not null)
        {
            var outputBytes = (result.Output?.Length ?? 0) + (result.Error?.Length ?? 0);
            await _quota.RecordExecutionAsync(threadId, stopwatch.ElapsedMilliseconds, outputBytes);
        }

        var semanticNote = CommandSemantics.InterpretExitCode(command, result.ExitCode);

        await PublishExecutionEventAsync(sandbox, threadId, command, result, startedAt, stopwatch.ElapsedMilliseconds);

        return new { stdout = result.Output, stderr = result.Error, exit_code = result.ExitCode, note = semanticNote };
    }

    [AIFunction("ls", "List contents of a directory in the sandbox",
        IsReadOnly = true, IsConcurrencySafe = true,
        SearchHint = "list directory files folder")]
    public async Task<object> ListDirectoryAsync(
        [Description("Virtual directory path, e.g. /mnt/workspace")] string path,
        [Description("Maximum recursion depth")] int maxDepth = 2,
        CancellationToken ct = default)
    {
        if (ResolveEnvironment() is not { } env) return SandboxUnavailable();

        try
        {
            var physicalPath = _translator.ToPhysical(path, env.ThreadId);
            var entries = await env.Sandbox.ListDirectoryAsync(physicalPath, maxDepth, ct);
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sandbox tool 'ls' failed for path {Path}", path);
            return new { error = $"ls failed: {ex.Message}" };
        }
    }

    [AIFunction("read_file", "Read the contents of a file in the sandbox",
        IsReadOnly = true, IsConcurrencySafe = true,
        SearchHint = "read file content view cat")]
    public async Task<object> ReadFileAsync(
        [Description("Virtual file path, e.g. /mnt/workspace/file.txt")] string path,
        [Description("1-based line number to start reading from")] int? offset = null,
        [Description("Maximum number of lines to read")] int? limit = null,
        CancellationToken ct = default)
    {
        if (ResolveEnvironment() is not { } env) return SandboxUnavailable();

        try
        {
            var physicalPath = _translator.ToPhysical(path, env.ThreadId);
            // Slicing is pushed down into the sandbox so large files are streamed
            // line-by-line rather than read fully into memory just to be sliced.
            var content = await env.Sandbox.ReadFileAsync(physicalPath, offset, limit, ct);
            return new { path, content };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sandbox tool 'read_file' failed for path {Path}", path);
            return new { error = $"read_file failed: {ex.Message}" };
        }
    }

    [AIFunction("write_file", "Write content to a file in the sandbox. Creates directories if needed.",
        SearchHint = "write create save file")]
    public async Task<object> WriteFileAsync(
        [Description("Virtual file path, e.g. /mnt/workspace/file.txt")] string path,
        [Description("Content to write")] string content,
        [Description("Append to the file instead of overwriting")] bool append = false,
        CancellationToken ct = default)
    {
        if (ResolveEnvironment() is not { } env) return SandboxUnavailable();

        try
        {
            var physicalPath = _translator.ToPhysical(path, env.ThreadId);
            await env.Sandbox.WriteFileAsync(physicalPath, content, append, ct);
            return new { success = true, path };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sandbox tool 'write_file' failed for path {Path}", path);
            return new { error = $"write_file failed: {ex.Message}" };
        }
    }

    [AIFunction("str_replace", "Replace a string in a file in the sandbox",
        SearchHint = "replace edit modify substitute file")]
    public async Task<object> StrReplaceAsync(
        [Description("Virtual file path, e.g. /mnt/workspace/file.txt")] string path,
        [Description("Exact string to find")] string oldString,
        [Description("Replacement string")] string newString,
        CancellationToken ct = default)
    {
        if (ResolveEnvironment() is not { } env) return SandboxUnavailable();

        try
        {
            var physicalPath = _translator.ToPhysical(path, env.ThreadId);
            var content = await env.Sandbox.ReadFileAsync(physicalPath, ct: ct);

            if (!content.Contains(oldString))
                return new { success = false, error = $"String '{oldString}' not found in file" };

            var updated = content.Replace(oldString, newString);
            await env.Sandbox.WriteFileAsync(physicalPath, updated, append: false, ct);
            return new { success = true, path, replacements = content.Split(oldString).Length - 1 };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sandbox tool 'str_replace' failed for path {Path}", path);
            return new { error = $"str_replace failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// 从执行上下文（AsyncLocal）解析当前运行的沙箱环境。
    /// SandboxMiddleware 在 next() 期间发布，工具调用必然发生在 next() 内部。
    /// </summary>
    private SandboxToolEnvironment? ResolveEnvironment()
    {
        var properties = _executionContextAccessor?.Properties;
        if (properties is not null
            && properties.TryGetValue(SandboxPropertyKeys.ToolEnvironment, out var value)
            && value is SandboxToolEnvironment environment)
        {
            return environment;
        }

        return null;
    }

    /// <summary>
    /// 环境缺失时的结构化错误 — 返回给 LLM，绝不抛异常。
    /// </summary>
    private object SandboxUnavailable()
    {
        _logger.LogDebug("Sandbox tool invoked without an active sandbox environment");
        return new { error = SandboxUnavailableMessage };
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

    private string TranslatePathsInCommand(string command, Guid threadId)
    {
        var threadDir = _translator.GetThreadDirectory(threadId);
        return command
            .Replace("/mnt/workspace", Path.Combine(threadDir, "workspace"))
            .Replace("/mnt/uploads", Path.Combine(threadDir, "uploads"))
            .Replace("/mnt/outputs", Path.Combine(threadDir, "outputs"))
            .Replace("/mnt/skills", Path.Combine(threadDir, "skills"));
    }

    /// <summary>
    /// Best-effort guard: after naive /mnt → physical translation, scan the command
    /// for path tokens rooted at the thread directory and confirm each still
    /// resolves inside that directory once <c>..</c> segments collapse. Returns
    /// <c>true</c> when no escaping token is found. This is intentionally
    /// conservative (it inspects literal tokens only and cannot follow shell
    /// constructs) — under the Local provider it is a hardening layer, not a jail.
    /// </summary>
    private bool IsTranslatedCommandWithinThreadDir(string translatedCommand, Guid threadId)
    {
        // Search for the thread dir exactly as TranslatePathsInCommand emitted it
        // (it does NOT call GetFullPath, so the substring may be relative); resolve
        // both sides through GetFullPath only for the containment comparison.
        var rawThreadDir = _translator.GetThreadDirectory(threadId);
        var normalizedThreadDir = Path.GetFullPath(rawThreadDir);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var searchStart = 0;
        while (true)
        {
            var idx = translatedCommand.IndexOf(rawThreadDir, searchStart, comparison);
            if (idx < 0)
                break;

            // Slice from this occurrence to the next shell-token boundary so a
            // `..` escape embedded in the path participates in normalization.
            var end = idx;
            while (end < translatedCommand.Length && !IsShellTokenBoundary(translatedCommand[end]))
                end++;

            var token = translatedCommand[idx..end];
            string resolved;
            try
            {
                resolved = Path.GetFullPath(token);
            }
            catch
            {
                // An unparseable token is suspicious — fail closed.
                return false;
            }

            if (!resolved.Equals(normalizedThreadDir, comparison)
                && !resolved.StartsWith(normalizedThreadDir + Path.DirectorySeparatorChar, comparison)
                && !resolved.StartsWith(normalizedThreadDir + Path.AltDirectorySeparatorChar, comparison))
            {
                return false;
            }

            searchStart = end;
        }

        return true;
    }

    private static bool IsShellTokenBoundary(char c)
        => c is ' ' or '\t' or '\n' or '\r' or '"' or '\'' or '|' or '&' or ';' or '<' or '>' or '`';
}
