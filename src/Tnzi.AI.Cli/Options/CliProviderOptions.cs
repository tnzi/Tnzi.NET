namespace Tnzi.AI.Cli.Options;

/// <summary>
/// 单个 CLI 提供者配置
/// </summary>
public class CliProviderOptions
{
    /// <summary>CLI 可执行文件命令</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>默认模型</summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>默认工作目录</summary>
    public string DefaultWorkingDirectory { get; set; } = string.Empty;

    /// <summary>允许的工具列表</summary>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>额外环境变量</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// Additional host environment variable keys to inherit, ON TOP OF the built-in
    /// safe baseline (<see cref="DefaultSafeEnvironmentKeys"/>). Secure by default:
    /// the child process only ever sees the safe baseline plus the keys listed here —
    /// application secrets (database connection strings, cloud credentials, internal
    /// tokens) are stripped before <see cref="EnvironmentVariables"/> are applied.
    /// To pass a secret the CLI genuinely needs, either list its key here (inherit
    /// from host) or set it explicitly via <see cref="EnvironmentVariables"/>.
    /// To disable filtering entirely, set <see cref="InheritAllHostEnvironment"/> = true.
    /// Matching is case-insensitive on Windows, case-sensitive on Linux/macOS
    /// (matching OS-native env semantics).
    /// </summary>
    public List<string>? EnvironmentWhitelist { get; set; }

    /// <summary>
    /// When <c>true</c>, the child process inherits ALL host environment variables
    /// (legacy behavior). INSECURE — host secrets leak into the untrusted CLI
    /// subprocess. Defaults to <c>false</c> (safe baseline + <see cref="EnvironmentWhitelist"/> only).
    /// </summary>
    public bool InheritAllHostEnvironment { get; set; }

    /// <summary>
    /// OS environment variables a child process needs to run correctly (executable
    /// search path, home/profile dirs, locale, temp dirs, Windows system roots).
    /// Always retained when host-env filtering is active. Deliberately excludes keys
    /// that typically carry application secrets (API keys, connection strings).
    /// </summary>
    public static IReadOnlyCollection<string> DefaultSafeEnvironmentKeys { get; } =
    [
        // Cross-platform
        "PATH", "HOME", "LANG", "LANGUAGE", "LC_ALL", "LC_CTYPE", "TZ", "TERM",
        // Unix
        "USER", "LOGNAME", "SHELL", "TMPDIR", "HOSTNAME",
        "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME", "XDG_RUNTIME_DIR",
        // Windows
        "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA",
        "TEMP", "TMP", "SystemRoot", "SystemDrive", "windir", "ComSpec",
        "PATHEXT", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE",
        "PROCESSOR_IDENTIFIER", "PROCESSOR_LEVEL", "PROCESSOR_REVISION",
        "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
        "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432",
        "PUBLIC", "ALLUSERSPROFILE", "USERNAME", "USERDOMAIN", "COMPUTERNAME", "OS",
    ];
}
