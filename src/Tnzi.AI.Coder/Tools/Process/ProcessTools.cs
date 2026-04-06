namespace Tnzi.AI.Coder.ProcessManagement;

/// <summary>
/// 后台进程管理工具组 — 启动、列出、读取输出、终止后台进程
/// </summary>
[AIToolGroup("process", "Process Management", "Manage background processes")]
public class ProcessTools : IAIToolProvider
{
    private readonly ICommandSanitizer _commandSanitizer;
    private readonly IPathValidator _pathValidator;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly IShellAdapter _shellAdapter;
    private readonly CoderOptions _options;
    private readonly ILogger<ProcessTools> _logger;

    public ProcessTools(
        ICommandSanitizer commandSanitizer,
        IPathValidator pathValidator,
        IOptions<CoderOptions> options,
        ILogger<ProcessTools> logger,
        IToolApprovalHandler? approvalHandler = null,
        IShellAdapter? shellAdapter = null)
    {
        _commandSanitizer = Check.NotNull(commandSanitizer);
        _pathValidator = Check.NotNull(pathValidator);
        _approvalHandler = approvalHandler;
        _shellAdapter = shellAdapter ?? ShellTools.CreateDefaultShellAdapter();
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 启动后台进程
    /// </summary>
    [AIFunction("process_start", "Start a command in background", InterruptBehavior = ToolInterruptBehavior.GracefulShutdown, SearchHint = "start background process")]
    public async Task<object> StartAsync(
        [AIParameter("command", "Shell command to execute")] string command,
        [AIParameter("working_directory", "Working directory", false)] string? workingDirectory = null)
    {
        try
        {
            // 清理已退出的旧进程
            ProcessRegistry.CleanupExited();

            // 检查后台进程数限制
            if (ProcessRegistry.RunningCount >= _options.Sandbox.MaxBackgroundProcesses)
            {
                return new { error = $"Maximum background processes ({_options.Sandbox.MaxBackgroundProcesses}) reached" };
            }

            // 1. 硬拒绝检查
            var sanitizeResult = _commandSanitizer.Sanitize(command);
            if (!sanitizeResult.IsAllowed)
            {
                return new { error = $"Command denied: {sanitizeResult.Reason}" };
            }

            // 2. 审批流
            if (sanitizeResult.RequiresApproval && _approvalHandler != null)
            {
                var approvalRequest = new ToolApprovalRequest
                {
                    ToolName = "process_start",
                    ToolGroup = "process",
                    ToolDescription = "Start a command in background",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["command"] = command,
                        ["working_directory"] = workingDirectory
                    },
                    Reason = sanitizeResult.Reason
                };

                var approvalResult = await _approvalHandler.RequestApprovalAsync(approvalRequest);
                if (!approvalResult.Approved)
                {
                    _logger.LogDebug("Command '{Command}' rejected by approval handler: {Reason}",
                        command, approvalResult.RejectionReason);
                    return new
                    {
                        error = $"Command rejected: {approvalResult.RejectionReason ?? "Not approved"}",
                        status = approvalResult.Status.ToString()
                    };
                }

                if (approvalResult.ModifiedArguments?.TryGetValue("command", out var modified) == true
                    && modified is string modifiedCommand)
                {
                    command = modifiedCommand;
                }
            }

            // 3. 验证工作目录
            var workDir = workingDirectory ?? _options.ProjectRoot;
            var dirValidation = await _pathValidator.ValidateAsync(workDir);
            if (!dirValidation.IsValid)
            {
                return new { error = $"Invalid working directory: {dirValidation.Error}" };
            }

            var resolvedWorkDir = dirValidation.ResolvedPath!;
            if (!Directory.Exists(resolvedWorkDir))
            {
                return new { error = $"Working directory not found: {workDir}" };
            }

            // 4. 启动进程
            var psi = _shellAdapter.CreateProcessStartInfo(command, resolvedWorkDir);

            // 应用环境变量过滤
            EnvironmentFilter.ApplyEnvironmentFilter(psi, _options.Sandbox);

            var managed = ProcessRegistry.CreateManagedProcess(command, psi, _options.Sandbox.MaxOutputSize);
            managed.Process.Start();
            managed.Process.BeginOutputReadLine();
            managed.Process.BeginErrorReadLine();

            var processId = ProcessRegistry.Register(managed);

            _logger.LogDebug("Started background process {ProcessId}: {Command}", processId, command);

            return new
            {
                success = true,
                process_id = processId,
                command,
                pid = managed.Process.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to start background process '{Command}'", command);
            return new { error = $"Failed to start process: {ex.Message}" };
        }
    }

    /// <summary>
    /// 列出所有管理的后台进程
    /// </summary>
    [AIFunction("process_list", "List all managed background processes", IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<object> ListAsync()
    {
        ProcessRegistry.CleanupExited();

        var processes = ProcessRegistry.All.Select(mp =>
        {
            var hasExited = false;
            int? exitCode = null;

            try
            {
                hasExited = mp.Process.HasExited;
                if (hasExited) exitCode = mp.Process.ExitCode;
            }
            catch
            {
                hasExited = true;
            }

            var duration = DateTime.UtcNow - mp.StartTime;

            int outputSize;
            lock (mp.Stdout) { outputSize = mp.Stdout.Length; }
            lock (mp.Stderr) { outputSize += mp.Stderr.Length; }

            return new
            {
                id = mp.Id,
                command = mp.Command,
                status = hasExited ? "exited" : "running",
                exit_code = exitCode,
                duration_seconds = (int)duration.TotalSeconds,
                output_size = outputSize
            };
        }).ToList();

        return Task.FromResult<object>(new { processes, count = processes.Count });
    }

    /// <summary>
    /// 读取后台进程的输出
    /// </summary>
    [AIFunction("process_output", "Read stdout/stderr of a background process", IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<object> OutputAsync(
        [AIParameter("process_id", "Process ID (e.g., proc-1)")] string processId,
        [AIParameter("tail", "Only return last N lines", false)] int? tail = null)
    {
        var managed = ProcessRegistry.Get(processId);
        if (managed == null)
        {
            return Task.FromResult<object>(new { error = $"Process not found: {processId}" });
        }

        string stdout, stderr;
        bool hasExited;
        int? exitCode = null;

        // 读取并清空输出（避免内存膨胀）
        lock (managed.Stdout)
        {
            stdout = managed.Stdout.ToString();
            managed.Stdout.Clear();
        }

        lock (managed.Stderr)
        {
            stderr = managed.Stderr.ToString();
            managed.Stderr.Clear();
        }

        try
        {
            hasExited = managed.Process.HasExited;
            if (hasExited) exitCode = managed.Process.ExitCode;
        }
        catch
        {
            hasExited = true;
        }

        // 可选 tail 截取
        if (tail.HasValue && tail.Value > 0)
        {
            stdout = TailLines(stdout, tail.Value);
            stderr = TailLines(stderr, tail.Value);
        }

        return Task.FromResult<object>(new
        {
            process_id = processId,
            stdout,
            stderr,
            running = !hasExited,
            exit_code = exitCode
        });
    }

    /// <summary>
    /// 终止后台进程
    /// </summary>
    [AIFunction("process_kill", "Kill a background process", IsDestructive = true, InterruptBehavior = ToolInterruptBehavior.GracefulShutdown)]
    public Task<object> KillAsync(
        [AIParameter("process_id", "Process ID (e.g., proc-1)")] string processId)
    {
        var managed = ProcessRegistry.Get(processId);
        if (managed == null)
        {
            return Task.FromResult<object>(new { error = $"Process not found: {processId}" });
        }

        try
        {
            if (!managed.Process.HasExited)
            {
                managed.Process.Kill(entireProcessTree: true);
                _logger.LogDebug("Killed background process {ProcessId}", processId);
            }

            // 清理资源
            managed.Process.Dispose();
            ProcessRegistry.Remove(processId, out _);

            return Task.FromResult<object>(new { success = true, process_id = processId });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to kill process {ProcessId}", processId);
            return Task.FromResult<object>(new { error = $"Failed to kill process: {ex.Message}" });
        }
    }

    /// <summary>
    /// 截取最后 N 行
    /// </summary>
    private static string TailLines(string text, int count)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var lines = text.Split('\n');
        if (lines.Length <= count) return text;

        return string.Join('\n', lines[^count..]);
    }
}

/// <summary>
/// 托管进程状态
/// </summary>
internal class ManagedProcess
{
    public System.Diagnostics.Process Process { get; set; } = null!;
    public StringBuilder Stdout { get; } = new();
    public StringBuilder Stderr { get; } = new();
    public string Command { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public string Id { get; set; } = string.Empty;
}
