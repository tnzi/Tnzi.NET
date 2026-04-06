namespace Tnzi.AI.Coder.Shell;

/// <summary>
/// Windows PowerShell 工具组 — 显式暴露 PowerShell 命令执行入口。
/// </summary>
/// <remarks>
/// 在 Windows 主机上提供 PowerShell 专用工具，支持同步执行和流式执行。
/// 通过独立的 <see cref="PowerShellShellAdapter"/> 实例确保始终使用 PowerShell，
/// 即使平台默认 IShellAdapter 被替换为 Bash 也不受影响。
/// </remarks>
[AIToolGroup("shell", "PowerShell Execution", "Execute PowerShell commands on Windows hosts")]
public sealed class PowerShellTools : IAIToolProvider
{
    private readonly ICommandSanitizer _commandSanitizer;
    private readonly IPathValidator _pathValidator;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly PowerShellShellAdapter _shellAdapter;
    private readonly CoderOptions _options;
    private readonly ILogger<PowerShellTools> _logger;

    public PowerShellTools(
        ICommandSanitizer commandSanitizer,
        IPathValidator pathValidator,
        PowerShellShellAdapter shellAdapter,
        IOptions<CoderOptions> options,
        ILogger<PowerShellTools> logger,
        IToolApprovalHandler? approvalHandler = null)
    {
        _commandSanitizer = Check.NotNull(commandSanitizer);
        _pathValidator = Check.NotNull(pathValidator);
        _shellAdapter = Check.NotNull(shellAdapter);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
        _approvalHandler = approvalHandler;
    }

    /// <summary>
    /// 执行 PowerShell 命令
    /// </summary>
    [AIFunction("powershell",
        "Execute a PowerShell command on Windows",
        Aliases = "pwsh",
        SearchHint = "powershell pwsh execute command shell",
        InterruptBehavior = ToolInterruptBehavior.GracefulShutdown)]
    public async Task<object> ExecutePowerShellAsync(
        [AIParameter("command", "PowerShell command to execute")] string command,
        [AIParameter("working_directory", "Working directory", false)] string? workingDirectory = null,
        [AIParameter("timeout_ms", "Timeout in milliseconds", false)] int? timeoutMs = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new { error = "PowerShell tool is only available on Windows hosts." };
        }

        try
        {
            // 1. 命令消毒 + 审批流
            var (sanitizeError, approvedCommand) = await SanitizeAndApproveAsync(command, "powershell", workingDirectory);
            if (sanitizeError != null) return sanitizeError;
            command = approvedCommand;

            // 2. 验证工作目录
            var (dirError, resolvedWorkDir) = await ValidateWorkingDirectoryAsync(workingDirectory);
            if (dirError != null) return dirError;

            var timeout = Math.Min(timeoutMs ?? _options.Sandbox.DefaultCommandTimeoutMs, 600_000);

            _logger.LogDebug("Executing PowerShell command: {Command} in {WorkDir} (timeout: {Timeout}ms, adapter: {Adapter})",
                command, resolvedWorkDir, timeout, _shellAdapter.Name);

            return await ExecuteProcessAsync(command, resolvedWorkDir!, timeout);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute PowerShell command '{Command}'", command);
            return new { error = $"Failed to execute command: {ex.Message}" };
        }
    }

    /// <summary>
    /// 流式执行 PowerShell 命令 — 返回初始输出，长时间运行的命令在后台继续执行
    /// </summary>
    [AIFunction("powershell_streaming",
        "Execute a PowerShell command and return initial output, with background process for long-running commands",
        SearchHint = "powershell pwsh streaming background long-running")]
    public async Task<object> ExecuteStreamingAsync(
        [AIParameter("command", "PowerShell command to execute")] string command,
        [AIParameter("working_directory", "Working directory", false)] string? workingDirectory = null,
        [AIParameter("initial_wait_ms", "Time to wait for initial output (default 5000)", false)] int? initialWaitMs = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new { error = "PowerShell streaming tool is only available on Windows hosts." };
        }

        try
        {
            // 1. 命令消毒 + 审批流
            var (sanitizeError, approvedCommand) = await SanitizeAndApproveAsync(command, "powershell_streaming", workingDirectory);
            if (sanitizeError != null) return sanitizeError;
            command = approvedCommand;

            // 2. 验证工作目录
            var (dirError, resolvedWorkDir) = await ValidateWorkingDirectoryAsync(workingDirectory);
            if (dirError != null) return dirError;

            // 3. 后台进程数限制
            ProcessRegistry.CleanupExited();
            if (ProcessRegistry.RunningCount >= _options.Sandbox.MaxBackgroundProcesses)
            {
                return new { error = $"Maximum background processes ({_options.Sandbox.MaxBackgroundProcesses}) reached" };
            }

            var waitMs = Math.Clamp(initialWaitMs ?? 5000, 100, 30_000);

            _logger.LogDebug("Executing streaming PowerShell command: {Command} in {WorkDir} (initial wait: {Wait}ms)",
                command, resolvedWorkDir, waitMs);

            return await LaunchAndCollectInitialOutputAsync(command, resolvedWorkDir!, waitMs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute streaming PowerShell command '{Command}'", command);
            return new { error = $"Failed to execute command: {ex.Message}" };
        }
    }

    /// <summary>
    /// 同步执行进程并等待完成
    /// </summary>
    private async Task<object> ExecuteProcessAsync(string command, string workDir, int timeout)
    {
        var psi = _shellAdapter.CreateProcessStartInfo(command, workDir);
        EnvironmentFilter.ApplyEnvironmentFilter(psi, _options.Sandbox);

        using var process = new Process { StartInfo = psi };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var maxOutput = _options.Sandbox.MaxOutputSize;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null && stdoutBuilder.Length < maxOutput)
                stdoutBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null && stderrBuilder.Length < maxOutput)
                stderrBuilder.AppendLine(e.Data);
        };

        var sw = Stopwatch.StartNew();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* 尽力终止 */ }

            return new
            {
                error = $"Command timed out after {timeout}ms",
                stdout = TruncateOutput(stdoutBuilder.ToString()),
                stderr = TruncateOutput(stderrBuilder.ToString()),
                timed_out = true
            };
        }

        sw.Stop();

        _logger.LogDebug("PowerShell command completed with exit code {ExitCode} in {Duration}ms",
            process.ExitCode, sw.ElapsedMilliseconds);

        return new
        {
            stdout = TruncateOutput(stdoutBuilder.ToString()),
            stderr = TruncateOutput(stderrBuilder.ToString()),
            exit_code = process.ExitCode,
            duration_ms = sw.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// 启动进程并收集初始输出，未完成则注册到 ProcessRegistry
    /// </summary>
    private async Task<object> LaunchAndCollectInitialOutputAsync(string command, string workDir, int waitMs)
    {
        var psi = _shellAdapter.CreateProcessStartInfo(command, workDir);
        EnvironmentFilter.ApplyEnvironmentFilter(psi, _options.Sandbox);

        ManagedProcess? managed = null;
        try
        {
            managed = ProcessRegistry.CreateManagedProcess(command, psi, _options.Sandbox.MaxOutputSize);
            managed.Process.Start();
            managed.Process.BeginOutputReadLine();
            managed.Process.BeginErrorReadLine();

            // 等待初始输出
            using var cts = new CancellationTokenSource(waitMs);
            try
            {
                await managed.Process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 进程仍在运行，这是正常的
            }

            string stdout, stderr;
            lock (managed.Stdout) { stdout = managed.Stdout.ToString(); }
            lock (managed.Stderr) { stderr = managed.Stderr.ToString(); }

            var hasExited = false;
            int? exitCode = null;
            try
            {
                hasExited = managed.Process.HasExited;
                if (hasExited) exitCode = managed.Process.ExitCode;
            }
            catch
            {
                hasExited = true;
            }

            if (hasExited)
            {
                managed.Process.Dispose();

                _logger.LogDebug("Streaming PowerShell command completed with exit code {ExitCode}", exitCode);

                return new
                {
                    stdout = TruncateOutput(stdout),
                    stderr = TruncateOutput(stderr),
                    running = false,
                    exit_code = exitCode
                };
            }

            // 进程仍在运行，注册到 ProcessRegistry
            var processId = ProcessRegistry.Register(managed);
            managed = null; // 已注册，不在 catch 中清理

            _logger.LogDebug("Streaming PowerShell command still running, registered as {ProcessId}", processId);

            return new
            {
                stdout = TruncateOutput(stdout),
                stderr = TruncateOutput(stderr),
                running = true,
                process_id = processId
            };
        }
        catch (Exception ex)
        {
            try { if (managed?.Process != null && !managed.Process.HasExited) managed.Process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
            try { managed?.Process?.Dispose(); }
            catch { /* best effort */ }
            return new { error = $"Failed to execute command: {ex.Message}" };
        }
    }

    /// <summary>
    /// 命令消毒 + 审批流
    /// </summary>
    private async Task<(object? error, string command)> SanitizeAndApproveAsync(
        string command, string toolName, string? workingDirectory)
    {
        var sanitizeResult = _commandSanitizer.Sanitize(command);
        if (!sanitizeResult.IsAllowed)
        {
            return (new { error = $"Command denied: {sanitizeResult.Reason}" }, command);
        }

        if (sanitizeResult.RequiresApproval && _approvalHandler != null)
        {
            var approvalRequest = new ToolApprovalRequest
            {
                ToolName = toolName,
                ToolGroup = "shell",
                ToolDescription = $"Execute a PowerShell command ({toolName})",
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
                _logger.LogDebug("PowerShell command '{Command}' rejected by approval handler: {Reason}",
                    command, approvalResult.RejectionReason);
                return (new
                {
                    error = $"Command rejected: {approvalResult.RejectionReason ?? "Not approved"}",
                    status = approvalResult.Status.ToString()
                }, command);
            }

            if (approvalResult.ModifiedArguments?.TryGetValue("command", out var modified) == true
                && modified is string modifiedCommand)
            {
                command = modifiedCommand;
            }
        }

        return (null, command);
    }

    /// <summary>
    /// 验证工作目录
    /// </summary>
    private async Task<(object? error, string? resolvedPath)> ValidateWorkingDirectoryAsync(string? workingDirectory)
    {
        var workDir = workingDirectory ?? _options.ProjectRoot;
        var dirValidation = await _pathValidator.ValidateAsync(workDir);
        if (!dirValidation.IsValid)
        {
            return (new { error = $"Invalid working directory: {dirValidation.Error}" }, null);
        }

        var resolvedWorkDir = dirValidation.ResolvedPath!;
        if (!Directory.Exists(resolvedWorkDir))
        {
            return (new { error = $"Working directory not found: {workDir}" }, null);
        }

        return (null, resolvedWorkDir);
    }

    private string TruncateOutput(string output) => OutputHelper.Truncate(output, _options.Sandbox.MaxOutputSize);
}
