namespace Tnzi.AI.Cli.Options;

/// <summary>
/// CLI Agent 全局配置
/// </summary>
public class CliOptions
{
    /// <summary>默认 CLI 提供者名称</summary>
    public string DefaultProvider { get; set; } = string.Empty;

    /// <summary>默认超时秒数</summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>允许的工作目录列表（支持通配符，空列表 = 不限制）</summary>
    public List<string> AllowedDirectories { get; set; } = [];

    /// <summary>CLI 提供者配置（key = provider 名称）</summary>
    public Dictionary<string, CliProviderOptions> Providers { get; set; } = new();
}
