namespace Tnzi.AI.Coder.Options;

/// <summary>
/// 沙箱安全配置选项
/// </summary>
public class SandboxOptions
{
    /// <summary>
    /// 允许访问的目录列表（相对于 ProjectRoot）
    /// </summary>
    public List<string> AllowedDirectories { get; set; } = ["."];

    /// <summary>
    /// 拒绝访问的文件模式列表
    /// </summary>
    public List<string> DeniedPatterns { get; set; } =
    [
        "**/.env",
        "**/.env.*",
        "**/*.key",
        "**/*.pem",
        "**/credentials*"
    ];

    /// <summary>
    /// 拒绝执行的命令列表
    /// </summary>
    public List<string> DeniedCommands { get; set; } =
    [
        "rm -rf /", "rm -rf /*", "format c:", "mkfs",
        "dd if=", "chmod 777", "shutdown", "reboot",
        "curl | sh", "curl | bash", "wget | sh", "wget | bash"
    ];

    /// <summary>
    /// 命令默认超时（毫秒）
    /// </summary>
    public int DefaultCommandTimeoutMs { get; set; } = 120_000;

    /// <summary>
    /// 文件最大读取大小（字节，默认 50MB）
    /// </summary>
    public long MaxFileReadSize { get; set; } = 52_428_800;

    /// <summary>
    /// 输出最大大小（字节，默认 512KB）
    /// </summary>
    public long MaxOutputSize { get; set; } = 524_288;

    /// <summary>
    /// 危险操作是否需要审批
    /// </summary>
    public bool RequireApprovalForDangerousOps { get; set; } = true;

    /// <summary>
    /// 环境变量白名单（仅这些环境变量传递给子进程，为空表示传递所有）
    /// </summary>
    public List<string> EnvironmentWhitelist { get; set; } = [];

    /// <summary>
    /// 环境变量黑名单（这些环境变量不传递给子进程）
    /// </summary>
    public List<string> EnvironmentBlacklist { get; set; } =
    [
        "API_KEY", "SECRET_KEY", "ACCESS_TOKEN", "PRIVATE_KEY",
        "AWS_SECRET_ACCESS_KEY", "AZURE_CLIENT_SECRET",
        "OPENAI_API_KEY", "ANTHROPIC_API_KEY"
    ];

    /// <summary>
    /// 最大同时后台进程数
    /// </summary>
    public int MaxBackgroundProcesses { get; set; } = 10;
}
