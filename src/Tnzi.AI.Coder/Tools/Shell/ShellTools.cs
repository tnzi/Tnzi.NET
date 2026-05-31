namespace Tnzi.AI.Coder.Shell;

/// <summary>
/// Shell 执行工具组 — 执行本地 shell 命令
/// </summary>
/// <remarks>
/// 安全模型：DeniedCommands 硬拒绝 → IToolApprovalHandler 审批 → 执行。
/// 当 RequireApprovalForDangerousOps=true 时，所有命令需经过审批处理器确认后才会执行。
/// </remarks>
[AIToolGroup("shell", "Shell Execution", "Execute shell commands")]
public class ShellTools : ShellToolsBase, IAIToolProvider
{
    private readonly ICommandSanitizer _commandSanitizer;
    private readonly IPathValidator _pathValidator;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly IShellAdapter _shellAdapter;
    private readonly CoderOptions _options;
    private readonly ILogger<ShellTools> _logger;

    public ShellTools(
        ICommandSanitizer commandSanitizer,
        IPathValidator pathValidator,
        IOptions<CoderOptions> options,
        ILogger<ShellTools> logger,
        IToolApprovalHandler? approvalHandler = null,
        IShellAdapter? shellAdapter = null)
    {
        _commandSanitizer = Check.NotNull(commandSanitizer);
        _pathValidator = Check.NotNull(pathValidator);
        _approvalHandler = approvalHandler;
        _shellAdapter = shellAdapter ?? CreateDefaultShellAdapter();
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    protected override ICommandSanitizer CommandSanitizer => _commandSanitizer;
    protected override IPathValidator PathValidator => _pathValidator;
    protected override IToolApprovalHandler? ApprovalHandler => _approvalHandler;
    protected override IShellAdapter Adapter => _shellAdapter;
    protected override CoderOptions Options => _options;
    protected override ILogger Logger => _logger;
    protected override string ShellLogLabel => "command";

    /// <summary>
    /// 执行 shell 命令
    /// </summary>
    [AIFunction("bash", "Execute a shell command",
        Aliases = "shell,exec", SearchHint = "execute run command shell bash",
        InterruptBehavior = ToolInterruptBehavior.GracefulShutdown)]
    public async Task<object> ExecuteAsync(
        [AIParameter("command", "Shell command to execute")] string command,
        [AIParameter("working_directory", "Working directory", false)] string? workingDirectory = null,
        [AIParameter("timeout_ms", "Timeout in milliseconds", false)] int? timeoutMs = null)
    {
        try
        {
            // 1. 硬拒绝检查 + 审批流（fail-closed）
            // 保留原审批负载：toolName="bash"，description="Execute a shell command"
            var (sanitizeError, approvedCommand) = await SanitizeAndApproveAsync(command, "bash", "Execute a shell command", workingDirectory);
            if (sanitizeError != null) return sanitizeError;
            command = approvedCommand;

            // 2. 验证工作目录
            var (dirError, resolvedWorkDir) = await ValidateWorkingDirectoryAsync(workingDirectory);
            if (dirError != null) return dirError;

            var timeout = timeoutMs ?? _options.Sandbox.DefaultCommandTimeoutMs;
            timeout = Math.Min(timeout, 600_000); // 最大 10 分钟

            _logger.LogDebug("Executing command: {Command} in {WorkDir} (timeout: {Timeout}ms)",
                command, resolvedWorkDir, timeout);

            return await ExecuteProcessAsync(command, resolvedWorkDir!, timeout);
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
            // 1. 硬拒绝检查 + 审批流
            var (sanitizeError, approvedCommand) = await SanitizeAndApproveAsync(command, "bash_streaming", "Execute a command and return initial output", workingDirectory);
            if (sanitizeError != null) return sanitizeError;
            command = approvedCommand;

            // 2. 验证工作目录
            var (dirError, resolvedWorkDir) = await ValidateWorkingDirectoryAsync(workingDirectory);
            if (dirError != null) return dirError;

            // 3. 检查后台进程数限制
            ProcessRegistry.CleanupExited();
            if (ProcessRegistry.RunningCount >= _options.Sandbox.MaxBackgroundProcesses)
            {
                return new { error = $"Maximum background processes ({_options.Sandbox.MaxBackgroundProcesses}) reached" };
            }

            var waitMs = Math.Clamp(initialWaitMs ?? 5000, 100, 30_000);

            _logger.LogDebug("Executing streaming command: {Command} in {WorkDir} (initial wait: {Wait}ms)",
                command, resolvedWorkDir, waitMs);

            // 4. 启动进程并收集初始输出
            return await LaunchAndCollectInitialOutputAsync(command, resolvedWorkDir!, waitMs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute streaming command '{Command}'", command);
            return new { error = $"Failed to execute command: {ex.Message}" };
        }
    }

    public static string GetShellPath()
    {
        return OperatingSystem.IsWindows()
            ? PowerShellShellAdapter.DefaultExecutablePath
            : BashShellAdapter.DefaultExecutablePath;
    }

    internal static IShellAdapter CreateDefaultShellAdapter()
    {
        return OperatingSystem.IsWindows()
            ? new PowerShellShellAdapter()
            : new BashShellAdapter();
    }
}
