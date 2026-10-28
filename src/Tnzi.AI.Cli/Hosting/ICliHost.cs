namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 外部运行在哪里发生：本机，或者消费方提供的远程位置（每用户一个的容器、一台工作机）。
/// </summary>
/// <remarks>
/// <para>
/// 这是本模块唯一的「执行位置」扩展点。框架自带 <see cref="LocalProcessHost"/>（API 进程所在的机器），
/// 未注册别的实现时用它。消费方在自己模块的 <c>ConfigureServicesAsync</c> 里注册一个实现即可整体替换：
/// 本机默认在 <c>AICliModule.PostConfigureServicesAsync</c> 里以 <c>TryAdd</c> 登记，晚于所有模块的 Configure 阶段。
/// </para>
/// <para>
/// 宿主按<b>作用域</b>交出一个 <see cref="ICliHostTarget"/>：一次运行的全部动作（布置工作区、找可执行文件、
/// 查登录态、起进程、写回写通道条目、清理）都在同一个目标上完成。远程实现据作用域里的用户 / 租户 / 线程
/// 选出该去哪个容器，以及文件与进程以哪个身份落地；本机实现对任何作用域都交出同一个目标。
/// </para>
/// <para>
/// 协议适配器、事件落库、续接、计量都不知道运行在哪里：它们只拿到 <see cref="ICliAgentTransport"/> 与路径字符串。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "The execution-location seam is new; per-workspace runtimes and connection resilience will build on it")]
public interface ICliHost
{
    /// <summary>
    /// 为一个作用域取执行目标。
    /// </summary>
    /// <param name="scope">要在哪个身份下执行。<see cref="CliHostScope.HostLevel"/> 用于运行时探测（不属于任何运行）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="CliHostUnavailableException">
    /// 这个作用域眼下没有可用的目标（对应的远程端没连上、容器没起来），或宿主不服务这种作用域
    /// （例如不支持宿主级探测）。运行以 <see cref="CliRunFailureReason.LaunchFailed"/> 结束，探测跳过这一轮。
    /// </exception>
    Task<ICliHostTarget> GetTargetAsync(CliHostScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// 这个服务端副本此刻能执行哪些运行时上的运行。认领只看这里：副本不服务的执行目标上的运行，它不认领。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 缺省 <see cref="CliHostCoverage.InProcess"/>：本机运行时（<see cref="CliRuntimeMode.InProcess"/>）上的运行，任一副本都可以认领（与此前相同）。
    /// 远程执行目标（<see cref="CliRuntimeMode.RemoteDaemon"/>）只由列在 <see cref="CliHostCoverage.RemoteTargets"/> 里的副本认领，
    /// 典型实现是「此刻连在这个副本上的那些工作区代理」。服务端副本每轮还会把这些目标标成在线（刷新 <c>LastSeenAt</c>），
    /// 不在任何副本覆盖范围里的目标上排着运行时，框架请使用方唤起它（<see cref="ICliTargetActivator"/>）。
    /// </para>
    /// <para>每轮认领调用一次，要快、不做 I/O（读内存里的连接表）。</para>
    /// </remarks>
    CliHostCoverage GetCoverage() => CliHostCoverage.InProcess;
}

/// <summary>
/// 一个服务端副本此刻能执行的运行时范围。
/// </summary>
/// <param name="InProcessRuntimes">是否执行本机运行时（<see cref="CliRuntimeMode.InProcess"/>）上的运行。</param>
/// <param name="RemoteTargets">能执行的远程执行目标标识（远程运行时的 <c>HostId</c>，按序数比较）。</param>
public sealed record CliHostCoverage(bool InProcessRuntimes, IReadOnlyCollection<string> RemoteTargets)
{
    /// <summary>只执行本机运行时：未覆写 <see cref="ICliHost.GetCoverage"/> 的宿主（本机宿主）。</summary>
    public static CliHostCoverage InProcess { get; } = new(true, []);

    /// <summary>执行本机运行时，外加给定的远程执行目标。</summary>
    public static CliHostCoverage InProcessAnd(IEnumerable<string> remoteTargets) => new(true, [.. remoteTargets]);

    /// <summary>只执行给定的远程执行目标（这个副本上没有可用的本机 CLI）。</summary>
    public static CliHostCoverage RemoteOnly(IEnumerable<string> remoteTargets) => new(false, [.. remoteTargets]);

    /// <summary>这个副本是否执行某个运行时上的运行。</summary>
    public bool Covers(CliRuntimeMode mode, string targetId)
        => mode == CliRuntimeMode.InProcess ? InProcessRuntimes : RemoteTargets.Contains(targetId, StringComparer.Ordinal);
}

/// <summary>
/// 一个具体的执行位置：在那里起进程、读写文件、找可执行文件，并决定 CLI 怎样连回平台的回写通道。
/// </summary>
/// <remarks>
/// <para>
/// ★ 路径一律是<b>目标自己</b>的路径（远程容器里的 <c>/home/...</c>），由 <see cref="ICliHostFileSystem.Paths"/>
/// 解释，框架不拿它们去碰 API 进程所在机器的磁盘。工作区布置、配置目录、MCP 配置文件、工作目录都按这个规矩来。
/// </para>
/// <para>
/// 起进程与写文件是两个独立的操作：远程实现可能以不同的身份执行它们（文件由用户身份的助手写、进程由代理以用户 uid 起）。
/// 框架从不假设「写文件的那个进程」就是「起 CLI 的那个进程」。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "The execution-location seam is new; per-workspace runtimes and connection resilience will build on it")]
public interface ICliHostTarget : ICliProcessHost
{
    /// <summary>这个目标的文件系统（工作区、记忆文件、技能、MCP 配置、配置目录都写在这里）。</summary>
    ICliHostFileSystem FileSystem { get; }

    /// <summary>
    /// 受管工作区的根目录（目标自己的路径）。每线程 / 每运行目录、按用户分区与专用配置目录都建在它下面。
    /// </summary>
    string WorkspacesRoot { get; }

    /// <summary>
    /// 在这个目标上找 provider 的可执行文件。找不到返回 null。
    /// </summary>
    /// <remarks>
    /// 版本号与登录态探测不在这里：它们都是「起一个短命令读输出」，由框架经 <see cref="ICliProcessHost.StartAsync"/> 完成，
    /// 实现方只需要会起进程。
    /// </remarks>
    Task<string?> FindExecutableAsync(CliProviderDescriptor provider, CancellationToken cancellationToken);

    /// <summary>
    /// 平台回写通道在 CLI 的 MCP 配置里写成哪一条（Claude 风格的 <c>mcpServers</c> 条目）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 缺省是直连平台端点的 Streamable HTTP 条目（<see cref="CliWriteBackChannel.ToHttpEntry"/>），本机目标就用它。
    /// CLI 所在的位置连不到平台地址时（只能由驻留代理向外拨号的容器），实现方改写成代理提供的 stdio 命令，由代理把
    /// MCP 流量中继回平台，例如：
    /// </para>
    /// <code>
    /// { "type": "stdio", "command": "/opt/agent/bin/agent", "args": ["mcp-relay"], "env": { "RELAY_TOKEN": "…" } }
    /// </code>
    /// <para>
    /// 凭据（<see cref="CliWriteBackChannel.Token"/>）怎么交给中继由实现决定；它是运行范围的，运行结束即失效。
    /// 两种适配器都认这两种形状：stream-json 把整份配置交给 <c>--mcp-config</c>，ACP 翻译成 <c>mcpServers</c>
    /// （stdio 是 ACP 的基础能力，不需要运行时声明）。
    /// </para>
    /// </remarks>
    JsonObject CreateWriteBackServerEntry(CliWriteBackChannel channel) => channel.ToHttpEntry();

    /// <summary>
    /// 这次运行在这个位置上怎样启动 CLI（权限、内置工具、MCP 合并、设置来源、配置目录与项目目录名）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 缺省是 <see cref="CliLaunchProfile.Default"/>：全权，平台的 MCP 与 CLI 自己找到的合并（不加 <c>--strict-mcp-config</c>）。
    /// 本机宿主给 <see cref="CliLaunchProfile.Local"/>，与此前的行为相同。之后再经消费方注册的 <see cref="ICliLaunchProfileContributor"/> 调整。
    /// </para>
    /// <para>
    /// 每次执行问一次（<see cref="CliLaunchProfilePurpose.Launch"/>）；删除线程时为找会话记录再问一次
    /// （<see cref="CliLaunchProfilePurpose.SessionCleanup"/>，只读配置目录与项目目录名）。路径都是这个目标上的。
    /// </para>
    /// </remarks>
    Task<CliLaunchProfile> GetLaunchProfileAsync(CliLaunchProfileRequest request, CancellationToken cancellationToken)
        => Task.FromResult(CliLaunchProfile.Default);

    /// <summary>
    /// 凭起进程时交出的句柄（<see cref="ICliProcess.ReattachHandle"/>）接回一个进程。服务端重启后对每条仍在运行的这类运行调用一次。
    /// </summary>
    /// <remarks>
    /// <para>三种回答，处置各不相同：</para>
    /// <list type="bullet">
    /// <item><see cref="CliReattachResult.Attached"/>：进程还在，或者已经退出但它的输出都还在。框架从第一行起重放它的输出
    /// （已经落库的事件不再写一遍），然后接着读实时输出，这一轮照常结束。</item>
    /// <item><see cref="CliReattachResult.Gone"/>：进程不在了、输出也没了（被杀掉、容器重启、超出保留上限）。这一轮以
    /// <see cref="CliRunFailureReason.Interrupted"/> 结束，原因原样写进运行记录，应当是给使用者看的英文。</item>
    /// <item>抛 <see cref="CliHostUnavailableException"/>：眼下连不上（代理还没连回来）。框架隔
    /// <c>AI:Cli:Reattach:RetryInterval</c> 再试，到 <c>AI:Cli:Reattach:Timeout</c> 仍接不上就以 Interrupted 结束。</item>
    /// </list>
    /// <para>
    /// 缺省回答 Gone：不交句柄的宿主永远不会被问到；交了句柄却没实现这一个方法，接不回来就如实说。
    /// </para>
    /// </remarks>
    Task<CliReattachResult> ReattachAsync(CliReattachRequest request, CancellationToken cancellationToken)
        => Task.FromResult(CliReattachResult.Gone("This execution host cannot reattach to a running process."));

    /// <summary>
    /// 这个目标是哪个远程执行目标（与远程运行时登记的 <c>HostId</c> 相同）；本机目标为 null。
    /// </summary>
    /// <remarks>
    /// 运行在远程运行时上时，框架核对宿主交出的目标就是运行时登记的那一个，对不上就不起进程（<c>LaunchFailed</c>）：
    /// 宿主按作用域选错了容器，一个用户的运行会跑进另一个用户的工作区。
    /// </remarks>
    string? TargetId => null;

    /// <summary>
    /// 使用方按运行指定的工作目录（<c>CliRunPlacement.WorkDirectory</c>）允许是哪些根目录本身或落在其下（这个目标上的绝对路径）。
    /// </summary>
    /// <remarks>
    /// 缺省空：不接受按运行指定的工作目录，那样的运行以 <c>LaunchFailed</c> 结束。远程目标通常给出用户工作区里的几个根
    /// （例如 <c>/home/u/worktrees</c>、<c>/home/u/chat</c>）。根本身也接受。判定是按规范化路径的字面相等或包含，不解析符号链接：
    /// 目标的文件系统隔离（容器、OS 账号）才是权限边界。根不能与 <see cref="WorkspacesRoot"/> 重叠，框架会拒绝落在受管工作区根下的目录。
    /// </remarks>
    IReadOnlyList<string> WorkDirectoryRoots => [];

    /// <summary>
    /// 把使用方指定的、还不存在的工作目录建出来（只在 <c>CliRunPlacement.CreateWorkDirectory</c> 为 true 时调用）。
    /// </summary>
    /// <remarks>
    /// 缺省建一个空目录（连上级一起建）。要在这里做别的（例如 <c>git worktree add</c>），覆写它：请求里带着这次运行的身份。
    /// 建出来的目录按用户的目录对待，框架永不删除它。
    /// </remarks>
    Task CreateWorkDirectoryAsync(CliWorkDirectoryRequest request, CancellationToken cancellationToken)
        => FileSystem.CreateDirectoryAsync(request.Path, cancellationToken);
}

/// <summary>要在执行目标上建出来的、使用方为一次运行指定的工作目录。</summary>
/// <param name="Scope">这次运行的身份。</param>
/// <param name="Path">工作目录（目标上的规范化绝对路径）。</param>
public sealed record CliWorkDirectoryRequest(CliHostScope Scope, string Path);

/// <summary>
/// 一次取目标的身份：这次执行属于谁。
/// </summary>
/// <remarks>
/// 运行的身份一律取自运行记录（认领在没有 HTTP 请求的后台作用域里发生，环境上下文是空的）。
/// 字段都可空：宿主级探测不属于任何运行，没有登录用户的运行没有 <see cref="UserId"/>。
/// </remarks>
public sealed record CliHostScope
{
    /// <summary>宿主级作用域：运行时探测用，不属于任何租户、用户或运行。</summary>
    public static CliHostScope HostLevel { get; } = new();

    /// <summary>运行。</summary>
    public Guid? RunId { get; init; }

    /// <summary>Agent。</summary>
    public Guid? AgentId { get; init; }

    /// <summary>租户。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>派出这次运行的用户（运行记录的 <c>CreatorId</c>）。</summary>
    public Guid? UserId { get; init; }

    /// <summary>会话线程。</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>绑定的运行时记录。</summary>
    public Guid? RuntimeId { get; init; }

    /// <summary>provider 键。</summary>
    public string? ProviderKey { get; init; }

    /// <summary>
    /// 运行时所在的执行位置（运行时记录的 <c>HostId</c>）：本机运行时是探测它的机器名，远程运行时是执行目标的标识。
    /// 同时服务本机与远程的宿主据它（与 <see cref="RuntimeMode"/>）决定交出哪个目标。
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>运行时的执行位置类型。</summary>
    public CliRuntimeMode? RuntimeMode { get; init; }

    /// <summary>是否宿主级（不属于任何运行）。</summary>
    public bool IsHostLevel => RunId is null;
}

/// <summary>
/// 一次运行的平台回写通道：CLI 要连到哪里、凭什么连。
/// </summary>
/// <param name="Endpoint">平台 MCP 端点的绝对地址（<c>AI:Cli:WriteBack:McpEndpoint</c>）。</param>
/// <param name="Token">这次运行的回写凭据原文（运行范围，终态即失效）。</param>
/// <param name="ToolCallTimeout">单次回写调用的时长上限；null = 交给 CLI 的缺省。</param>
public sealed record CliWriteBackChannel(string Endpoint, string Token, TimeSpan? ToolCallTimeout)
{
    /// <summary>
    /// 直连 <see cref="Endpoint"/> 的 Streamable HTTP 条目。
    /// </summary>
    /// <remarks>
    /// <c>Tnzi.AI.Mcp</c> 只提供 Streamable HTTP（不映射旧版 SSE 端点），在 Claude 风格的配置里它叫 <c>"http"</c>。
    /// 写成 <c>"sse"</c> 时 CLI 按旧协议去连一个不说旧协议的端点，回写通道一个工具都拿不到。
    /// </remarks>
    public JsonObject ToHttpEntry()
    {
        var entry = new JsonObject
        {
            ["type"] = "http",
            ["url"] = Endpoint,
            ["headers"] = new JsonObject
            {
                // MCP server 同时接受 X-Api-Key 与 Authorization: Bearer；
                // 用 Bearer 是因为它更不容易被中间代理当成可缓存的自定义头。
                ["Authorization"] = $"Bearer {Token}"
            }
        };

        // 没配就不写：CLI 退回它自己的缺省。
        if (ToolCallTimeout is { } timeout)
        {
            entry["timeout"] = (long)Math.Ceiling(timeout.TotalMilliseconds);
        }

        return entry;
    }

    /// <summary>不带凭据：记录的缺省 <c>ToString</c> 会把它原样写进日志。</summary>
    public override string ToString() => $"{nameof(CliWriteBackChannel)} {{ Endpoint = {Endpoint} }}";
}
