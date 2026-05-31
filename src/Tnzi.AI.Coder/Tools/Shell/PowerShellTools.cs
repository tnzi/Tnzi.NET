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
public sealed class PowerShellTools : ShellToolsBase, IAIToolProvider
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

    protected override ICommandSanitizer CommandSanitizer => _commandSanitizer;
    protected override IPathValidator PathValidator => _pathValidator;
    protected override IToolApprovalHandler? ApprovalHandler => _approvalHandler;
    // 保留具体 PowerShellShellAdapter 字段以保证始终使用 PowerShell（平台钉死）
    protected override IShellAdapter Adapter => _shellAdapter;
    protected override CoderOptions Options => _options;
    protected override ILogger Logger => _logger;
    protected override string ShellLogLabel => "PowerShell command";

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
            var (sanitizeError, approvedCommand) = await SanitizeAndApproveAsync(command, "powershell", "Execute a PowerShell command (powershell)", workingDirectory);
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
            var (sanitizeError, approvedCommand) = await SanitizeAndApproveAsync(command, "powershell_streaming", "Execute a PowerShell command (powershell_streaming)", workingDirectory);
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
}
