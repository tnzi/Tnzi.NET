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
    /// Environment variables inherited from the host process. When null (default),
    /// ALL host env vars are inherited — INSECURE in production because secrets
    /// (database connection strings, cloud credentials, internal service tokens)
    /// leak into the untrusted CLI subprocess. Set to a non-null list (even empty)
    /// to enable host-env filtering: only listed keys are inherited, everything
    /// else is stripped before EnvironmentVariables are applied.
    /// Matching is case-insensitive on Windows, case-sensitive on Linux/macOS
    /// (matching OS-native env semantics).
    /// </summary>
    public List<string>? EnvironmentWhitelist { get; set; }
}
