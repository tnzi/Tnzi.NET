namespace Tnzi.AI.Coder.Shell;

/// <summary>
/// Shell 工具组共享基类 — 抽取 BashTools / PowerShellTools / ShellTools 之间字节级重复的
/// 进程执行、命令消毒+审批、工作目录校验、输出截断等私有辅助逻辑。
/// </summary>
/// <remarks>
/// 安全模型完全保留：DeniedCommands 硬拒绝 → IToolApprovalHandler 审批（fail-closed）→ 执行。
/// 派生类各自持有其具体 adapter 字段（BashShellAdapter / PowerShellShellAdapter / IShellAdapter），
/// 通过 <see cref="Adapter"/> 暴露给基类辅助方法，从而保留平台钉死（platform-pinning）保证。
/// 日志措辞与审批描述前缀通过 <see cref="ShellLogLabel"/> / <see cref="ApprovalDescription"/> 参数化，
/// 确保各派生类的可观测行为与重构前完全一致。
/// </remarks>
public abstract class ShellToolsBase
{
    /// <summary>
    /// 命令消毒器。
    /// </summary>
    protected abstract ICommandSanitizer CommandSanitizer { get; }

    /// <summary>
    /// 路径校验器。
    /// </summary>
    protected abstract IPathValidator PathValidator { get; }

    /// <summary>
    /// 审批处理器（可空 — 为空时需审批的命令一律 fail-closed 拒绝）。
    /// </summary>
    protected abstract IToolApprovalHandler? ApprovalHandler { get; }

    /// <summary>
    /// Shell 适配器（派生类返回其具体 adapter 字段以保留平台钉死保证）。
    /// </summary>
    protected abstract IShellAdapter Adapter { get; }

    /// <summary>
    /// Coder 配置选项。
    /// </summary>
    protected abstract CoderOptions Options { get; }

    /// <summary>
    /// 日志记录器。
    /// </summary>
    protected abstract ILogger Logger { get; }

    /// <summary>
    /// 进程执行日志措辞标签（如 "Bash command" / "PowerShell command" / "Command"）— 保留各派生类原有日志措辞。
    /// </summary>
    protected abstract string ShellLogLabel { get; }

    /// <summary>
    /// 同步执行进程并等待完成
    /// </summary>
    protected async Task<object> ExecuteProcessAsync(string command, string workDir, int timeout)
    {
        var psi = Adapter.CreateProcessStartInfo(command, workDir);
        EnvironmentFilter.ApplyEnvironmentFilter(psi, Options.Sandbox);

        using var process = new Process { StartInfo = psi };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var maxOutput = Options.Sandbox.MaxOutputSize;

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

        Logger.LogDebug("{ShellLabel} completed with exit code {ExitCode} in {Duration}ms",
            ShellLogLabel, process.ExitCode, sw.ElapsedMilliseconds);

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
    protected async Task<object> LaunchAndCollectInitialOutputAsync(string command, string workDir, int waitMs)
    {
        var psi = Adapter.CreateProcessStartInfo(command, workDir);
        EnvironmentFilter.ApplyEnvironmentFilter(psi, Options.Sandbox);

        ManagedProcess? managed = null;
        try
        {
            managed = ProcessRegistry.CreateManagedProcess(command, psi, Options.Sandbox.MaxOutputSize);
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

                Logger.LogDebug("Streaming {ShellLabel} completed with exit code {ExitCode}", ShellLogLabel, exitCode);

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

            Logger.LogDebug("Streaming {ShellLabel} still running, registered as {ProcessId}", ShellLogLabel, processId);

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
    /// 命令消毒 + 审批流（共享逻辑）
    /// </summary>
    /// <returns>如果被拒绝返回 (errorObject, command)，否则返回 (null, approvedCommand)</returns>
    protected async Task<(object? error, string command)> SanitizeAndApproveAsync(
        string command, string toolName, string toolDescription, string? workingDirectory)
    {
        var sanitizeResult = CommandSanitizer.Sanitize(command);
        if (!sanitizeResult.IsAllowed)
        {
            return (new { error = $"Command denied: {sanitizeResult.Reason}" }, command);
        }

        if (sanitizeResult.RequiresApproval)
        {
            // fail-closed: if approval is required but no handler is wired, deny execution
            if (ApprovalHandler == null)
            {
                return (new { error = "Command requires approval but no approval handler is configured." }, command);
            }

            var approvalRequest = new ToolApprovalRequest
            {
                ToolName = toolName,
                ToolGroup = "shell",
                ToolDescription = toolDescription,
                Arguments = new Dictionary<string, object?>
                {
                    ["command"] = command,
                    ["working_directory"] = workingDirectory
                },
                Reason = sanitizeResult.Reason
            };

            var approvalResult = await ApprovalHandler.RequestApprovalAsync(approvalRequest);
            if (!approvalResult.Approved)
            {
                Logger.LogDebug("{ShellLabel} '{Command}' rejected by approval handler: {Reason}",
                    ShellLogLabel, command, approvalResult.RejectionReason);
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
    /// <returns>如果无效返回 (errorObject, null)，否则返回 (null, resolvedPath)</returns>
    protected async Task<(object? error, string? resolvedPath)> ValidateWorkingDirectoryAsync(string? workingDirectory)
    {
        var workDir = workingDirectory ?? Options.ProjectRoot;
        var dirValidation = await PathValidator.ValidateAsync(workDir);
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

    /// <summary>
    /// 按沙箱配置截断输出
    /// </summary>
    protected string TruncateOutput(string output) => OutputHelper.Truncate(output, Options.Sandbox.MaxOutputSize);
}
