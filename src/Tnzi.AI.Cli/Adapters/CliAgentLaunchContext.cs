namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// 一次外部执行的全部输入。
/// </summary>
public sealed record CliAgentLaunchContext
{
    /// <summary>provider 描述（协议、被禁参数、记忆文件名等）。</summary>
    public required CliProviderDescriptor Provider { get; init; }

    /// <summary>已解析的可执行文件绝对路径（执行目标上的）。</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>提示词。经 stdin 投递，绝不作为命令行参数。</summary>
    public required string Prompt { get; init; }

    /// <summary>子进程工作目录（执行目标上的路径）。</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>模型覆盖。空 = 用 CLI 自己的默认。</summary>
    public string? Model { get; init; }

    /// <summary>运行时原生的推理强度值，原样往返，不做跨 provider 归一化。</summary>
    public string? ThinkingLevel { get; init; }

    /// <summary>非空则续接既有会话。</summary>
    public string? ResumeSessionId { get; init; }

    /// <summary>
    /// 非空则从 <see cref="ResumeSessionId"/> 的这个条目处截断、分叉成一个新会话续接（丢掉它之后的轮次）。
    /// 只对 <see cref="CliProviderDescriptor.SupportsResumeAtEntry"/> 的 provider 给出。
    /// </summary>
    public string? ResumeAtEntryId { get; init; }

    /// <summary>
    /// 随提示一起投递的附件（已校验过的图片与 PDF）。只对 <see cref="CliProviderDescriptor.AcceptsAttachments"/> 的 provider 给出。
    /// </summary>
    public IReadOnlyList<CliPromptAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// 内联在提示前面的 agent 说明。两种情况才有值：provider 没有原生记忆文件（每一轮都带）；
    /// 工作目录是用户提供的而 provider 不能以文件接收追加系统提示（只在新会话的第一条提示里带，续接的会话里已经有了）。
    /// 其余留空（brief 已写进记忆文件或追加系统提示文件）。
    /// </summary>
    public string? InlineSystemPrompt { get; init; }

    /// <summary>
    /// 受管 MCP 配置文件在执行目标上的绝对路径。null = 继承 provider 在执行位置上的本机配置。
    /// </summary>
    /// <remarks>
    /// 文件由工作区布置器写在运行目录内，于是它的生命周期与工作区一致，由同一套回收逻辑管掉。
    /// 适配器写系统临时目录的话，进程被强杀时就没人删了。把路径交给 CLI 的适配器（stream-json 的
    /// <c>--mcp-config</c>）用它；要读内容的适配器用 <see cref="McpConfigJson"/>，不去读这个文件。
    /// </remarks>
    public string? McpConfigPath { get; init; }

    /// <summary>
    /// 受管 MCP 配置的内容（与 <see cref="McpConfigPath"/> 处的文件逐字相同）。null = 没有受管配置。
    /// </summary>
    /// <remarks>
    /// ACP 在 <c>session/new</c> 里以参数投递 MCP server，需要内容而不是路径。文件在执行目标上，
    /// 远程目标的文件 API 进程读不到，所以内容随启动上下文一起来。
    /// </remarks>
    public string? McpConfigJson { get; init; }

    /// <summary>
    /// 这次运行的权限档位（<see cref="CliLaunchProfile.PermissionMode"/>）。stream-json 落成 <c>--permission-mode</c>，
    /// ACP 落在对权限请求的应答上。
    /// </summary>
    public CliPermissionMode PermissionMode { get; init; } = CliPermissionMode.Full;

    /// <summary>
    /// true = 档位不自动放行的权限请求交给使用方批准：适配器产出 <see cref="CliAgentEventType.PermissionRequest"/> 事件、不应答，
    /// 等执行器经 <see cref="ICliProtocolAdapter.AnswerPermissionAsync"/> 把回答交回来。false = 按档位当场应答（此前的行为）。
    /// </summary>
    /// <remarks>执行器只在 <see cref="CliPermissionMode.Ask"/> 且这一轮有人值守时置 true。</remarks>
    public bool AsksForApproval { get; init; }

    /// <summary>
    /// 有受管 MCP 配置时是否只用它（stream-json 的 <c>--strict-mcp-config</c>）。执行器按启动档案设置；
    /// 缺省 true 只是为直接构造这个上下文的调用方保持旧行为。
    /// </summary>
    public bool StrictMcpConfig { get; init; } = true;

    /// <summary>
    /// 框架为这次运行组出的参数（隔离档位、启动档案、追加系统提示、附加目录），排在 <see cref="ExtraArgs"/> 之前，不经被禁参数过滤。
    /// </summary>
    public IReadOnlyList<string> ManagedArgs { get; init; } = [];

    /// <summary>
    /// 这次运行由启动档案接管的开关：与 provider 的被禁参数一起，从 <see cref="ExtraArgs"/> 与 <see cref="CustomArgs"/> 里丢弃。
    /// </summary>
    public IReadOnlyDictionary<string, BlockedArgMode> OwnedArgs { get; init; }
        = new Dictionary<string, BlockedArgMode>(StringComparer.Ordinal);

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
/// 随提示投递给 CLI 的一个附件。
/// </summary>
/// <param name="MediaType">媒体类型（PNG / JPEG / GIF / WebP 图片或 <c>application/pdf</c>）。</param>
/// <param name="FileName">文件名（用户给的话）。</param>
/// <param name="Content">字节。</param>
public sealed record CliPromptAttachment(string MediaType, string? FileName, byte[] Content)
{
    /// <summary>是否图片（否则是 PDF 文档）。</summary>
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.Ordinal);
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
