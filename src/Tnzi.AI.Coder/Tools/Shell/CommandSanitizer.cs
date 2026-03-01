namespace Tnzi.AI.Coder.Shell;

/// <summary>
/// 命令消毒器实现 — 仅负责硬拒绝明确的破坏性命令
/// </summary>
/// <remarks>
/// <para>
/// 安全模型说明：
/// 基于正则模式的命令过滤无法为 `bash -c` 提供可靠的安全保障
/// （字符串拼接、base64 编码、变量展开等都能绕过正则检测）。
/// </para>
/// <para>
/// 因此本实现仅提供两级保护：
/// 1. 硬拒绝：配置中明确列出的 DeniedCommands（子串匹配）
/// 2. 审批流：当 RequireApprovalForDangerousOps=true 时，所有命令均标记为需要审批，
///    由 IToolApprovalHandler 在 AgentExecutor 层面控制执行
/// </para>
/// </remarks>
public class CommandSanitizer : ICommandSanitizer
{
    private readonly CoderOptions _options;
    private readonly ILogger<CommandSanitizer> _logger;

    public CommandSanitizer(IOptions<CoderOptions> options, ILogger<CommandSanitizer> logger)
    {
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public CommandSanitizeResult Sanitize(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return CommandSanitizeResult.Deny("Command cannot be empty");
        }

        var commandLower = command.ToLower().Trim();

        // 1. 硬拒绝：配置中明确列出的破坏性命令（子串匹配）
        foreach (var denied in _options.Sandbox.DeniedCommands)
        {
            if (commandLower.Contains(denied.ToLower()))
            {
                _logger.LogDebug("Command '{Command}' matches denied command '{Denied}'", command, denied);
                return CommandSanitizeResult.Deny($"Command is explicitly denied: '{denied}'");
            }
        }

        // 2. 审批流：所有非硬拒绝的命令均需要审批
        if (_options.Sandbox.RequireApprovalForDangerousOps)
        {
            return CommandSanitizeResult.NeedsApproval("All shell commands require approval");
        }

        return CommandSanitizeResult.Allow();
    }
}
