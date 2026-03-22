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
}
