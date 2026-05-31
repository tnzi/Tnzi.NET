namespace Tnzi.AI.Coder.Shell;

/// <summary>
/// Unix Bash 工具组 — 显式暴露 Bash 命令执行入口。
/// </summary>
/// <remarks>
/// 在非 Windows 主机上提供 Bash 专用工具，支持同步执行和流式执行。
/// 通过独立的 <see cref="BashShellAdapter"/> 实例确保始终使用 Bash，
/// 即使平台默认 IShellAdapter 被替换为 PowerShell 也不受影响。
/// 结构上与 <see cref="PowerShellTools"/> 对称。
/// </remarks>
[AIToolGroup("shell", "Bash Execution", "Execute Bash commands on Unix/Linux/macOS hosts")]
public sealed class BashTools : ShellToolsBase, IAIToolProvider
{
    private readonly ICommandSanitizer _commandSanitizer;
    private readonly IPathValidator _pathValidator;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly BashShellAdapter _shellAdapter;
    private readonly CoderOptions _options;
    private readonly ILogger<BashTools> _logger;

    public BashTools(
        ICommandSanitizer commandSanitizer,
        IPathValidator pathValidator,
        BashShellAdapter shellAdapter,
        IOptions<CoderOptions> options,
        ILogger<BashTools> logger,
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
    // 保留具体 BashShellAdapter 字段以保证始终使用 Bash（平台钉死）
    protected override IShellAdapter Adapter => _shellAdapter;
    protected override CoderOptions Options => _options;
    protected override ILogger Logger => _logger;
    protected override string ShellLogLabel => "Bash command";

    /// <summary>
    /// 执行 Bash 命令
    /// </summary>
    [AIFunction("bash_unix",
        "Execute a Bash command on Unix/Linux/macOS",
        SearchHint = "bash unix linux execute command shell",
        InterruptBehavior = ToolInterruptBehavior.GracefulShutdown)]
    public async Task<object> ExecuteBashAsync(
        [AIParameter("command", "Bash command to execute")] string command,
        [AIParameter("working_directory", "Working directory", false)] string? workingDirectory = null,
        [AIParameter("timeout_ms", "Timeout in milliseconds", false)] int? timeoutMs = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return new { error = "Bash tool is only available on Unix/Linux/macOS hosts." };
        }

        try
        {
            // 1. 命令消毒 + 审批流
            var (sanitizeError, approvedCommand) = await SanitizeAndApproveAsync(command, "bash_unix", "Execute a Bash command (bash_unix)", workingDirectory);
            if (sanitizeError != null) return sanitizeError;
            command = approvedCommand;

            // 2. 验证工作目录
            var (dirError, resolvedWorkDir) = await ValidateWorkingDirectoryAsync(workingDirectory);
            if (dirError != null) return dirError;

            var timeout = Math.Min(timeoutMs ?? _options.Sandbox.DefaultCommandTimeoutMs, 600_000);

            _logger.LogDebug("Executing Bash command: {Command} in {WorkDir} (timeout: {Timeout}ms, adapter: {Adapter})",
                command, resolvedWorkDir, timeout, _shellAdapter.Name);

            return await ExecuteProcessAsync(command, resolvedWorkDir!, timeout);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute Bash command '{Command}'", command);
            return new { error = $"Failed to execute command: {ex.Message}" };
        }
    }

    /// <summary>
    /// 流式执行 Bash 命令 — 返回初始输出，长时间运行的命令在后台继续执行
    /// </summary>
    [AIFunction("bash_unix_streaming",
        "Execute a Bash command and return initial output, with background process for long-running commands",
        SearchHint = "bash unix streaming background long-running")]
    public async Task<object> ExecuteStreamingAsync(
        [AIParameter("command", "Bash command to execute")] string command,
        [AIParameter("working_directory", "Working directory", false)] string? workingDirectory = null,
        [AIParameter("initial_wait_ms", "Time to wait for initial output (default 5000)", false)] int? initialWaitMs = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return new { error = "Bash streaming tool is only available on Unix/Linux/macOS hosts." };
        }

        try
        {
            // 1. 命令消毒 + 审批流
            var (sanitizeError, approvedCommand) = await SanitizeAndApproveAsync(command, "bash_unix_streaming", "Execute a Bash command (bash_unix_streaming)", workingDirectory);
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

            _logger.LogDebug("Executing streaming Bash command: {Command} in {WorkDir} (initial wait: {Wait}ms)",
                command, resolvedWorkDir, waitMs);

            return await LaunchAndCollectInitialOutputAsync(command, resolvedWorkDir!, waitMs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute streaming Bash command '{Command}'", command);
            return new { error = $"Failed to execute command: {ex.Message}" };
        }
    }
}
