namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// 一次外部执行的全部输入。
/// </summary>
public sealed record CliAgentLaunchContext
{
    /// <summary>provider 描述（协议、被禁参数、记忆文件名等）。</summary>
    public required CliProviderDescriptor Provider { get; init; }

    /// <summary>已解析的可执行文件绝对路径。</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>提示词。经 stdin 投递，绝不作为命令行参数。</summary>
    public required string Prompt { get; init; }

    /// <summary>子进程工作目录。</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>模型覆盖。空 = 用 CLI 自己的默认。</summary>
    public string? Model { get; init; }

    /// <summary>运行时原生的推理强度值，原样往返，不做跨 provider 归一化。</summary>
    public string? ThinkingLevel { get; init; }

    /// <summary>非空则续接既有会话。</summary>
    public string? ResumeSessionId { get; init; }

    /// <summary>
    /// 本次<b>意图</b>续接（即使 <see cref="ResumeSessionId"/> 被重试清空）。
    /// 用于在开新会话时向用户披露「上一轮的上下文已丢失」—— 不说的话，
    /// agent 会自然地假装连续性（"如我之前所说…"），而用户无从察觉。
    /// </summary>
    public bool ResumeExpected { get; init; }

    /// <summary>无原生记忆文件的 provider 才用；其余留空（brief 已写进工作目录）。</summary>
    public string? InlineSystemPrompt { get; init; }

    /// <summary>
    /// 受管 MCP 配置文件的绝对路径。null = 继承 provider 的本机配置。
    /// </summary>
    /// <remarks>
    /// 刻意传<b>路径</b>而不是 JSON：文件由工作区布置器写在运行目录内，
    /// 于是它的生命周期与工作区一致，由同一套回收逻辑管掉。适配器写系统临时目录的话，
    /// 进程被强杀时就没人删了。
    /// </remarks>
    public string? McpConfigPath { get; init; }

    /// <summary>部署级默认参数，先于 <see cref="CustomArgs"/> 追加。</summary>
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];

    /// <summary>每 agent 自定义参数，最后追加。</summary>
    public IReadOnlyList<string> CustomArgs { get; init; } = [];

    /// <summary>显式设置的环境变量。</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否透传宿主全部环境变量。</summary>
    public bool InheritAllHostEnvironment { get; init; }

    /// <summary>额外透传的宿主环境变量名。</summary>
    public IReadOnlyList<string> EnvironmentWhitelist { get; init; } = [];

    /// <summary>握手阶段（ACP initialize / 会话建立）超时。</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>整树终止的宽限期。</summary>
    public TimeSpan TerminateGrace { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// 一次会话结束时，进程层面的观测结果。
/// </summary>
/// <remarks>
/// 适配器只看得见协议流；退出码、stderr 尾部、是否被取消这些只有宿主知道。
/// 把它们显式传给 <see cref="ICliProtocolAdapter.GetResult"/>，好过让适配器去持有进程句柄 ——
/// 后者会让适配器同时依赖协议和进程模型，P4 换成沙箱执行时就得连适配器一起改。
/// </remarks>
public sealed record CliSessionOutcome
{
    /// <summary>进程退出码。未退出为 null。</summary>
    public int? ExitCode { get; init; }

    /// <summary>stderr 尾部。</summary>
    public string StderrTail { get; init; } = string.Empty;

    /// <summary>会话耗时。</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>是否因外部取消而结束。</summary>
    public bool Cancelled { get; init; }

    /// <summary>看门狗判定的超时类型；没有超时则为 null。</summary>
    public CliRunFailureReason? WatchdogFailure { get; init; }
}
