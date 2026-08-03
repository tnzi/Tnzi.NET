namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 启动一个外部 agent 子进程所需的全部信息。
/// </summary>
/// <remarks>
/// <b>提示词不在这里</b>：它经 stdin 投递，绝不作为命令行参数 ——
/// 参数会出现在进程列表里（对同机其他用户可见），且长提示会撞上命令行长度上限。
/// </remarks>
public sealed record CliProcessSpec
{
    /// <summary>可执行文件绝对路径。</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>命令行参数（已过滤掉协议契约参数）。</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>子进程工作目录。</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>显式设置的环境变量（provider 凭据等）。</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否透传宿主全部环境变量。</summary>
    public bool InheritAllHostEnvironment { get; init; }

    /// <summary>额外透传的宿主环境变量名。</summary>
    public IReadOnlyList<string> EnvironmentWhitelist { get; init; } = [];

    /// <summary>整树终止的宽限期。</summary>
    public TimeSpan TerminateGrace { get; init; } = TimeSpan.FromSeconds(5);
}
