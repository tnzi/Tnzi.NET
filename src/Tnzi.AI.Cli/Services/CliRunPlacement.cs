namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 由使用方决定一轮外部运行在哪里、用什么跑：运行时（执行目标 × CLI）、模型与工作目录。
/// </summary>
/// <remarks>
/// <para>
/// 绑定（<c>CliAgentBinding</c>）决定「这个 Agent 走外部执行」并给出缺省；解析器按这一轮的身份覆盖其中三件事。
/// 典型用法：按用户的档案选 CLI 与模型、让会话自己的选择优先，运行时落在这个用户自己的工作区，
/// 工作目录按会话（Code 模式一个 worktree）或按对话（Chat 模式一个目录）给出。
/// </para>
/// <para>
/// 每条运行在入队时调用一次（聊天入口与直接入队都经它），结果随运行记录保存：认领、重新执行、服务端重启后的重新附着都沿用它，不再问第二次。
/// 在消费方模块的 <c>ConfigureServicesAsync</c> 里注册为 Scoped；没有注册时一切按绑定。
/// </para>
/// <para>
/// 要拒绝这一轮（例如这个用户没有工作区、也不允许开），抛一个 <see cref="BusinessException"/>：它的状态码与消息原样到达调用方，不留任何运行记录。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Per-run placement is new; per-workspace runtimes build on it")]
public interface ICliRunPlacementResolver
{
    /// <summary>返回这一轮的落点；null（或全部字段为空）= 按绑定。</summary>
    Task<CliRunPlacement?> ResolveAsync(CliRunPlacementRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 解析一轮落点时交给使用方的信息。线程与用户消息的归属在调用之前已经核过。
/// </summary>
public sealed record CliRunPlacementRequest
{
    /// <summary>Agent。</summary>
    public required Guid AgentId { get; init; }

    /// <summary>会话线程；一次性任务为 null。</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>这一轮回答的用户消息（聊天场景）。</summary>
    public Guid? UserMessageId { get; init; }

    /// <summary>代谁执行（请求的 <c>UserId</c>，否则当前用户）；后台派发为 null。</summary>
    public Guid? UserId { get; init; }

    /// <summary>租户。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>绑定的运行时。</summary>
    public required Guid BindingRuntimeId { get; init; }

    /// <summary>绑定的运行时的 provider 键。</summary>
    public required string BindingProviderKey { get; init; }

    /// <summary>
    /// 调用方为这一轮要的模型（聊天请求的 <c>model</c>、直接入队的 <c>Model</c>），原样，还没按允许列表校验。
    /// </summary>
    /// <remarks>
    /// 落点不给模型时，框架照常按允许列表校验它；落点给了模型就以落点为准（使用方的决定，不再校验）。
    /// </remarks>
    public string? RequestedModel { get; init; }
}

/// <summary>
/// 一轮外部运行的落点。每一项为空都表示「按绑定」。
/// </summary>
/// <remarks>
/// <para>
/// 运行时二选一：<see cref="RuntimeId"/>（一个已登记的运行时，本机的或远程的），或 <see cref="TargetId"/> + <see cref="ProviderKey"/>
/// （一个远程执行目标上的一个 CLI；那一行还没登记时框架以离线状态建出来，等目标上线时由
/// <see cref="ICliRemoteRuntimeRegistry.RegisterAsync"/> 补上版本与路径）。两种都给是使用方的错误，入队以 500 失败。
/// </para>
/// <para>
/// 运行时的 CLI 与绑定的不同时，绑定上按 CLI 解释的设置（模型、推理强度、自定义参数）这一轮不用；说明、技能与受管 MCP 配置照常。
/// </para>
/// </remarks>
public sealed record CliRunPlacement
{
    /// <summary>一个已登记的运行时（<c>AI_CliRuntime</c> 的 id）。</summary>
    public Guid? RuntimeId { get; init; }

    /// <summary>远程执行目标的标识（例如 <c>ws-{userId}</c>）。与 <see cref="ProviderKey"/> 一起使用。</summary>
    public string? TargetId { get; init; }

    /// <summary>在 <see cref="TargetId"/> 上用哪家 CLI；空 = 绑定的那家。</summary>
    public string? ProviderKey { get; init; }

    /// <summary>这一轮交给 CLI 的模型（<c>--model</c>）；空 = 调用方请求的（经允许列表校验）或绑定的。</summary>
    public string? Model { get; init; }

    /// <summary>
    /// 这一轮的工作目录（执行目标上的绝对路径）；空 = 按绑定的工作目录策略。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 按用户提供的目录对待：框架不往里写任何受管文件（说明、技能、MCP 配置都在运行根下），永不删除它，占用标记按目录记。
    /// 同一线程的后续轮次给出同一个目录，CLI 就能在那里 <c>--resume</c> 续接；换了目录，续接被拒、开新会话并在这一轮里说明。
    /// </para>
    /// <para>执行时它必须是执行目标声明的某个根本身或落在其下（<see cref="ICliHostTarget.WorkDirectoryRoots"/>），否则这一轮以 <c>LaunchFailed</c> 结束。</para>
    /// </remarks>
    public string? WorkDirectory { get; init; }

    /// <summary><see cref="WorkDirectory"/> 在目标上还不存在时建出来（经 <see cref="ICliHostTarget.CreateWorkDirectoryAsync"/>）；false = 不存在就失败。</summary>
    public bool CreateWorkDirectory { get; init; }
}
