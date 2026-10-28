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
    /// <summary>可执行文件在目标上的绝对路径。</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>命令行参数（已过滤掉协议契约参数）。</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>子进程工作目录（目标自己的路径）。null = 宿主的缺省目录（版本探测这类不属于任何工作区的短命令）。</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>这个进程是什么：一次 agent 会话，还是一条读完输出就结束的探测命令。</summary>
    /// <remarks>远程宿主可据此选择执行通道；本机宿主只用它决定日志级别（探测按周期反复执行）。</remarks>
    public CliProcessPurpose Purpose { get; init; } = CliProcessPurpose.AgentSession;

    /// <summary>显式设置的环境变量（provider 凭据等）。</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否透传执行位置的全部环境变量（本机宿主 = API 进程的环境）。</summary>
    public bool InheritAllHostEnvironment { get; init; }

    /// <summary>额外透传的执行位置环境变量名。</summary>
    public IReadOnlyList<string> EnvironmentWhitelist { get; init; } = [];

    /// <summary>整树终止的宽限期。</summary>
    public TimeSpan TerminateGrace { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// 一个外部进程的用途。
/// </summary>
public enum CliProcessPurpose
{
    /// <summary>一次 agent 会话：长时间运行，stdin 保持打开，流式读 stdout。</summary>
    AgentSession = 0,

    /// <summary>探测命令（版本号、登录态）：stdin 立即关闭，读完输出即结束。</summary>
    Probe = 1
}
