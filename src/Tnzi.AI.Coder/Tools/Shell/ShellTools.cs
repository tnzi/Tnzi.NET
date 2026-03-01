namespace Tnzi.AI.Coder.Shell;

/// <summary>
/// Shell 执行工具组 — 执行本地 shell 命令
/// </summary>
/// <remarks>
/// 安全模型：DeniedCommands 硬拒绝 → IToolApprovalHandler 审批 → 执行。
/// 当 RequireApprovalForDangerousOps=true 时，所有命令需经过审批处理器确认后才会执行。
/// </remarks>
[AIToolGroup("shell", "Shell Execution", "Execute shell commands")]
public class ShellTools : IAIToolProvider
{
    private readonly ICommandSanitizer _commandSanitizer;
    private readonly IPathValidator _pathValidator;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly CoderOptions _options;
    private readonly ILogger<ShellTools> _logger;

    public ShellTools(
        ICommandSanitizer commandSanitizer,
        IPathValidator pathValidator,
        IOptions<CoderOptions> options,
        ILogger<ShellTools> logger,
        IToolApprovalHandler? approvalHandler = null)
    {
        _commandSanitizer = Check.NotNull(commandSanitizer);
        _pathValidator = Check.NotNull(pathValidator);
        _approvalHandler = approvalHandler;
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 执行 shell 命令
    /// </summary>
    [AIFunction("bash", "Execute a shell command")]
    public async Task<object> ExecuteAsync(
        [AIParameter("command", "Shell command to execute")] string command,
        [AIParameter("working_directory", "Working directory", false)] string? workingDirectory = null,
        [AIParameter("timeout_ms", "Timeout in milliseconds", false)] int? timeoutMs = null)
    {
        try
        {
            // 1. 硬拒绝检查（DeniedCommands 子串匹配）
            var sanitizeResult = _commandSanitizer.Sanitize(command);
            if (!sanitizeResult.IsAllowed)
            {
                return new { error = $"Command denied: {sanitizeResult.Reason}" };
            }

            // 2. 审批流：通过 IToolApprovalHandler 请求用户确认
            if (sanitizeResult.RequiresApproval && _approvalHandler != null)
            {
                var approvalRequest = new ToolApprovalRequest
                {
                    ToolName = "bash",
                    ToolGroup = "shell",
                    ToolDescription = "Execute a shell command",
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

                // 审批人可能修改了命令参数
                if (approvalResult.ModifiedArguments?.TryGetValue("command", out var modified) == true
                    && modified is string modifiedCommand)
                {
                    command = modifiedCommand;
                }
            }

            // 解析工作目录
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

            var timeout = timeoutMs ?? _options.Sandbox.DefaultCommandTimeoutMs;
            timeout = Math.Min(timeout, 600_000); // 最大 10 分钟

            _logger.LogDebug("Executing command: {Command} in {WorkDir} (timeout: {Timeout}ms)",
                command, resolvedWorkDir, timeout);

            // 检测平台选择 shell，使用 ArgumentList 避免命令注入
            var shellPath = OperatingSystem.IsWindows() ? "bash" : "/bin/bash";

            var psi = new ProcessStartInfo
            {
                FileName = shellPath,
                WorkingDirectory = resolvedWorkDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // ArgumentList 会正确转义参数，避免 shell 注入
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);

            // 应用环境变量过滤
            EnvironmentFilter.ApplyEnvironmentFilter(psi, _options.Sandbox);

            using var process = new Process { StartInfo = psi };
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null && stdoutBuilder.Length < _options.Sandbox.MaxOutputSize)
                {
                    stdoutBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null && stderrBuilder.Length < _options.Sandbox.MaxOutputSize)
                {
                    stderrBuilder.AppendLine(e.Data);
                }
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

            var stdout = TruncateOutput(stdoutBuilder.ToString());
            var stderr = TruncateOutput(stderrBuilder.ToString());

            _logger.LogDebug("Command completed with exit code {ExitCode} in {Duration}ms",
                process.ExitCode, sw.ElapsedMilliseconds);

            return new
            {
                stdout,
                stderr,
                exit_code = process.ExitCode,
                duration_ms = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute command '{Command}'", command);
            return new { error = $"Failed to execute command: {ex.Message}" };
        }
    }

    /// <summary>
    /// 执行命令并返回初始输出，长时间运行的命令可通过 process_output 读取后续输出
    /// </summary>
    [AIFunction("bash_streaming", "Execute a command and return initial output, with background process for long-running commands")]
    public async Task<object> ExecuteStreamingAsync(
        [AIParameter("command", "Shell command to execute")] string command,
        [AIParameter("working_directory", "Working directory", false)] string? workingDirectory = null,
        [AIParameter("initial_wait_ms", "Time to wait for initial output (default 5000)", false)] int? initialWaitMs = null)
    {
        try
        {
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
                    ToolName = "bash_streaming",
                    ToolGroup = "shell",
                    ToolDescription = "Execute a command and return initial output",
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

            // 检查后台进程数限制
            ProcessRegistry.CleanupExited();
            if (ProcessRegistry.RunningCount >= _options.Sandbox.MaxBackgroundProcesses)
            {
                return new { error = $"Maximum background processes ({_options.Sandbox.MaxBackgroundProcesses}) reached" };
            }

            var waitMs = initialWaitMs ?? 5000;
            waitMs = Math.Clamp(waitMs, 100, 30_000);

            _logger.LogDebug("Executing streaming command: {Command} in {WorkDir} (initial wait: {Wait}ms)",
                command, resolvedWorkDir, waitMs);

            var shellPath = OperatingSystem.IsWindows() ? "bash" : "/bin/bash";
            var psi = new ProcessStartInfo
            {
                FileName = shellPath,
                WorkingDirectory = resolvedWorkDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);

            // 应用环境变量过滤
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

                // 读取当前已收集的输出
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
                    // 进程已完成，清理资源
                    managed.Process.Dispose();

                    _logger.LogDebug("Streaming command completed with exit code {ExitCode}", exitCode);

                    return new
                    {
                        stdout = TruncateOutput(stdout),
                        stderr = TruncateOutput(stderr),
                        running = false,
                        exit_code = exitCode
                    };
                }

                // 进程仍在运行，注册到 ProcessRegistry 以便后续通过 process_output 读取
                var processId = ProcessRegistry.Register(managed);
                managed = null; // 已注册，不在 catch 中清理

                _logger.LogDebug("Streaming command still running, registered as {ProcessId}", processId);

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
                // 进程已启动但未注册时，需要清理
                try { if (managed?.Process != null && !managed.Process.HasExited) managed.Process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
                try { managed?.Process?.Dispose(); }
                catch { /* best effort */ }
                return new { error = $"Failed to execute command: {ex.Message}" };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute streaming command '{Command}'", command);
            return new { error = $"Failed to execute command: {ex.Message}" };
        }
    }

    /// <summary>
    /// 截断输出到最大大小
    /// </summary>
    private string TruncateOutput(string output)
    {
        if (output.Length <= _options.Sandbox.MaxOutputSize)
        {
            return output;
        }

        var truncated = output[..(int)_options.Sandbox.MaxOutputSize];
        return truncated + $"\n... (truncated, {output.Length} total chars)";
    }
}
