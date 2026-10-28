using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

namespace Tnzi.AI.Sandbox.Tools;

/// <summary>
/// 沙箱工具组 - bash / ls / read_file / write_file / str_replace 五个工具。
/// </summary>
/// <remarks>
/// <para>
/// 工具方法签名只包含 LLM 可见参数（进入 JSON schema）。运行环境
/// （<see cref="ISandbox"/> + threadId）不在签名中，而是在调用时从
/// <see cref="IAgentExecutionContextAccessor"/> 解析 - 由
/// <c>SandboxMiddleware</c> 在每次 Agent 运行期间发布
/// <see cref="SandboxToolEnvironment"/>（finally 移除）。
/// </para>
/// <para>
/// 环境缺失（中间件未运行 / 请求无线程 / 子代理裸执行器路径）时，
/// 工具返回结构化英文错误对象，绝不抛出异常。
/// </para>
/// <para>
/// ★ 沙箱在环境里<b>按需创建</b>：每个工具先经 <see cref="SandboxToolEnvironment.GetSandboxAsync"/>
/// 取沙箱，第一次调用才布置线程目录、复制技能资源、向 provider 要实例。这一步刻意放在 try 之外：
/// provider 的拒绝（Production 下的 Local 守卫、Docker 不可达）是基础设施 / 配置问题，
/// 不该被折成一条 Warning 级的「bash failed」，让它抛给执行器按工具失败记 Error 日志并回给模型。
/// </para>
/// </remarks>
[AIToolGroup("sandbox", "Sandbox", "Execute commands and manage files in isolated sandbox environment", RequiredPermissions = AIToolPermissions.Sandbox)]
public class SandboxTools : IAIToolProvider
{
    /// <summary>
    /// Output/stderr truncation budget for the audit/event payload - keeps
    /// the audit row bounded while leaving the agent's return value intact.
    /// </summary>
    private const int MaxAuditOutputBytes = 2048;

    private const string DenialPrefix = "Command denied:";

    /// <summary>
    /// <c>ls</c> 的递归深度上限。深度是模型给的参数：不封顶时一次 <c>ls /mnt</c> 带上大深度就把技能副本、
    /// <c>node_modules</c> 之类整棵树塞进一条工具结果。
    /// </summary>
    internal const int MaxListDepth = 5;

    /// <summary><c>ls</c> 一次最多返回的条目数，超出的部分明说省略了多少。</summary>
    internal const int MaxListEntries = 1000;

    private const string SandboxUnavailableMessage =
        "Sandbox is not available in this execution context. Sandbox tools require an active sandbox " +
        "provisioned per agent run by the sandbox middleware. Ensure the sandbox module is enabled " +
        "(AI:Sandbox:Enabled) and the request carries a thread id.";

    private readonly IVirtualPathTranslator _translator;
    private readonly ILogger<SandboxTools> _logger;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly IEventBus? _eventBus;
    private readonly ICurrentUser? _currentUser;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IThreadResourceQuota? _quota;
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;

    public SandboxTools(IVirtualPathTranslator translator, ILogger<SandboxTools> logger,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        IEventBus? eventBus = null,
        ICurrentUser? currentUser = null,
        ICurrentTenant? currentTenant = null,
        IThreadResourceQuota? quota = null,
        IOptionsMonitor<AIOptions>? aiOptions = null)
    {
        _translator = Check.NotNull(translator);
        _logger = Check.NotNull(logger);
        _executionContextAccessor = executionContextAccessor;
        _eventBus = eventBus;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _quota = quota;
        _aiOptions = aiOptions;
    }

    /// <summary>
    /// 一次整文件读取最多交给模型多少字符：取 <c>AI:ToolResultBudget:MaxResultChars</c>（「任何单个工具结果超过它就截断」），
    /// 预算关闭或设为 0 时不限。没有选项（直接构造的测试）时用同一个默认值。
    /// </summary>
    private int? ReadFileBudget()
    {
        var budget = _aiOptions?.CurrentValue.ToolResultBudget ?? new ToolResultBudgetOptions();
        return budget.Enabled && budget.MaxResultChars > 0 ? budget.MaxResultChars : null;
    }

    [AIFunction("bash",
        "Execute a bash command in the sandbox environment. Under the Local provider " +
        "the shell is NOT filesystem-jailed: it is protected only by the command blacklist " +
        "and a best-effort path check on translated /mnt paths. Use the Docker provider for " +
        "strong isolation. Under the Local provider /mnt paths are translated on this command line " +
        "only, not inside scripts or files, so pass paths to a script as arguments or use paths " +
        "relative to the working directory. The working directory is the thread root, which holds " +
        "workspace/, uploads/, outputs/ and skills/ (the folders /mnt/workspace, /mnt/uploads, " +
        "/mnt/outputs and /mnt/skills): write files under workspace/ or outputs/ so ls and read_file can see them.",
        SearchHint = "shell command execute terminal run script")]
    public async Task<object> BashAsync(
        [Description("The shell command to execute")] string command,
        CancellationToken ct = default)
    {
        if (ResolveEnvironment() is not { } env) return SandboxUnavailable();
        if (string.IsNullOrWhiteSpace(command))
            return new { error = "Command cannot be empty" };

        var sandbox = await env.GetSandboxAsync(ct);
        try
        {
            return await ExecuteBashCoreAsync(sandbox, env.ThreadId, command, ct);
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
        var threadDir = _translator.GetThreadDirectory(threadId);
        var mountTokens = SandboxCommandPaths.Find(command, sandbox.ShellDialect);

        // Best-effort jail check for the Local provider: the rewritten command is
        // handed verbatim to a host shell, so if a /mnt path resolves outside the
        // thread directory (via `..` segments in the same shell word) refuse the
        // command rather than let it escape. This is NOT a true jail - it cannot
        // reason about shell-constructed paths - but it closes the obvious
        // `/mnt/workspace/../../../etc/passwd` style bypass. Runs before the quota
        // reservation so an escaping command does not consume a quota slot.
        if (!SandboxCommandPaths.AllWithin(mountTokens, threadDir))
        {
            const string escapeReason = "Command references a path outside the sandbox thread directory";
            _logger.LogWarning(
                "Sandbox bash blocked by path-escape guard for thread {ThreadId}", threadId);

            var escapeResult = new CommandResult(-1, string.Empty, $"Command denied: {escapeReason}");
            await PublishExecutionEventAsync(sandbox, threadId, command, escapeResult, DateTime.UtcNow, 0);

            return new { stdout = string.Empty, stderr = escapeResult.Error, exit_code = -1, note = (string?)null };
        }

        // Thread-level resource quota check - pre-flight before reaching the shell.
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

        // 守卫过了才换根：每个虚拟根换成沙箱自己寻址的形态（Docker 是容器里同名的 /mnt/{mount}，即原样不变；
        // Local 是宿主物理路径），并按所在引号上下文加引号，含空格的根也作为一个词交给 shell。
        // 换根不放宽任何判定 —— 判定已经在上面做完了。
        var sandboxCommand = SandboxCommandPaths.Rewrite(
            command, mountTokens, sandbox.ShellDialect, mount => sandbox.MapPath(Path.Combine(threadDir, mount)));

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var result = await sandbox.ExecuteCommandAsync(sandboxCommand, ct);
        stopwatch.Stop();

        // Record the soft-cap cost (duration + output bytes) even on non-zero exits
        // so a busy-looping agent cannot dodge the caps by failing fast. The command
        // count is already reserved by CheckAsync above (hard cap); RecordExecutionAsync
        // deliberately does not touch it. Path-escape and quota denials (handled above)
        // return early and never reach this accounting; a command the provider's
        // blacklist rejects does reach it, having already used its slot.
        if (_quota is not null)
        {
            var outputBytes = (result.Output?.Length ?? 0) + (result.Error?.Length ?? 0);
            await _quota.RecordExecutionAsync(threadId, stopwatch.ElapsedMilliseconds, outputBytes);
        }

        var semanticNote = CommandSemantics.InterpretExitCode(command, result.ExitCode);

        await PublishExecutionEventAsync(sandbox, threadId, command, result, startedAt, stopwatch.ElapsedMilliseconds);

        return new { stdout = result.Output, stderr = result.Error, exit_code = result.ExitCode, note = semanticNote };
    }

    [AIFunction("ls", "List contents of a directory in the sandbox. Recursion is capped at 5 levels and 1000 entries per call.",
        IsReadOnly = true, IsConcurrencySafe = true,
        SearchHint = "list directory files folder")]
    public async Task<object> ListDirectoryAsync(
        [Description("Virtual directory path, e.g. /mnt/workspace")] string path,
        [Description("Maximum recursion depth (at most 5)")] int maxDepth = 2,
        CancellationToken ct = default)
    {
        if (ResolveEnvironment() is not { } env) return SandboxUnavailable();

        var sandbox = await env.GetSandboxAsync(ct);
        try
        {
            var physicalPath = sandbox.MapPath(_translator.ToPhysical(path, env.ThreadId));
            var entries = await sandbox.ListDirectoryAsync(physicalPath, Math.Clamp(maxDepth, 0, MaxListDepth), ct);
            var listed = entries.Take(MaxListEntries).Select(e => new
            {
                name = e.Name,
                type = e.IsDirectory ? "directory" : "file",
                size = e.Size
            }).ToArray();

            if (entries.Count <= MaxListEntries)
                return new { path, entries = listed };

            return new
            {
                path,
                entries = listed,
                truncated = true,
                total_entries = entries.Count,
                note = $"Only the first {MaxListEntries} of {entries.Count} entries are listed. List a subdirectory or use a smaller maxDepth."
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

    [AIFunction("read_file", "Read the contents of a file in the sandbox. A whole-file read of a large file returns its " +
        "first part with truncated=true and next_offset; read the rest with offset and limit, and never write a truncated " +
        "read back as the whole file.",
        IsReadOnly = true, IsConcurrencySafe = true,
        SearchHint = "read file content view cat")]
    public async Task<object> ReadFileAsync(
        [Description("Virtual file path, e.g. /mnt/workspace/file.txt")] string path,
        [Description("1-based line number to start reading from")] int? offset = null,
        [Description("Maximum number of lines to read")] int? limit = null,
        CancellationToken ct = default)
    {
        if (ResolveEnvironment() is not { } env) return SandboxUnavailable();

        var sandbox = await env.GetSandboxAsync(ct);
        try
        {
            var physicalPath = sandbox.MapPath(_translator.ToPhysical(path, env.ThreadId));
            // Slicing is pushed down into the sandbox so large files are streamed
            // line-by-line rather than read fully into memory just to be sliced.
            var content = await sandbox.ReadFileAsync(physicalPath, offset, limit, ct);

            // 整文件读取按工具结果预算交出开头一段，并明说截断了、下一段从哪行开始。★ 此前原样返回，Local 下上限只有
            // MaxFileSizeBytes（50 MiB）：一个几十 MB 的 CSV 整个进了这一轮的模型上下文。显式给了行窗口的读取不截断，
            // str_replace 直接读沙箱也不经过这里。
            if (offset is null && limit is null && ReadFileBudget() is { } budget && content.Length > budget)
                return TruncatedRead(path, content, budget);

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

        var sandbox = await env.GetSandboxAsync(ct);
        try
        {
            var physicalPath = sandbox.MapPath(_translator.ToPhysical(path, env.ThreadId));
            await sandbox.WriteFileAsync(physicalPath, content, append, ct);
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

        var sandbox = await env.GetSandboxAsync(ct);
        try
        {
            var physicalPath = sandbox.MapPath(_translator.ToPhysical(path, env.ThreadId));
            var content = await sandbox.ReadFileAsync(physicalPath, ct: ct);

            if (!content.Contains(oldString))
                return new { success = false, error = $"String '{oldString}' not found in file" };

            var updated = content.Replace(oldString, newString);
            await sandbox.WriteFileAsync(physicalPath, updated, append: false, ct);
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
    /// 整文件读取超出预算时的结果：在预算内的最后一个换行处截断（只交整行），给出下一段的起始行号。
    /// </summary>
    private static object TruncatedRead(string path, string content, int budget)
    {
        var cut = content.LastIndexOf('\n', budget - 1);
        // 第一行就超出预算时按码元硬切：切点不落在代理对中间（半个 emoji 进了工具结果，序列化进模型请求时被换成替换字符）
        var head = cut >= 0 ? content[..(cut + 1)] : content.FitToLength(budget);
        var linesReturned = head.Count(c => c == '\n');
        int? nextOffset = cut >= 0 ? linesReturned + 1 : null;

        return new
        {
            path,
            content = head,
            truncated = true,
            total_chars = content.Length,
            next_offset = nextOffset,
            note = nextOffset is { } next
                ? $"Only the first {linesReturned} lines ({head.Length} of {content.Length} characters) were returned. " +
                  $"Read the rest with offset={next} and a limit. Do not write this content back as the whole file."
                : $"Only the first {head.Length} of {content.Length} characters were returned (the first line alone exceeds the budget). " +
                  "Do not write this content back as the whole file."
        };
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
    /// 环境缺失时的结构化错误 - 返回给 LLM，绝不抛异常。
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
            // Silent catch - observability publication must not break the agent flow.
            _logger.LogWarning(ex, "Failed to publish SandboxCommandExecutedEvent for sandbox {SandboxId}", sandbox.Id);
        }
    }

    private static string Truncate(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Truncate(MaxAuditOutputBytes, "...[truncated]");
}
