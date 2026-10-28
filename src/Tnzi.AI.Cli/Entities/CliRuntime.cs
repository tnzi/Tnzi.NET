namespace Tnzi.AI.Cli.Entities;

/// <summary>
/// 一台宿主上一个可用的外部 agent CLI。
/// </summary>
/// <remarks>
/// <para>
/// 字段按<b>远程 daemon</b> 建模，尽管首期只有进程内实现：<see cref="HostId"/> /
/// <see cref="Mode"/> / <see cref="LastSeenAt"/> 在单机场景下看着冗余，但等到真的要接
/// 远程 daemon 时，缺了它们就得改表、改契约、改前端。先立对契约、后补实现的代价，
/// 远小于反过来。
/// </para>
/// <para>
/// <see cref="CliVersion"/> <b>不选行为分支</b>：一旦有代码按版本号走不同的协议路径，CLI 的每次
/// 小版本升级都会变成一次线上事故排查。协议漂移靠打标签的真机冒烟测试发现，不靠版本号猜。
/// 它只用来做一件事：部署为 provider 声明了支持范围（<c>MinimumVersion</c> / <c>MaximumVersion</c>）时，范围外的不跑。
/// </para>
/// <para>
/// <b>宿主级资源，刻意不实现 <c>IMultiTenant</c></b>：它描述的是某台机器上的一个可执行文件，
/// 机器不属于任何租户。唯一写入者是无租户的后台探测服务，若按租户过滤，多租户开启后租户侧
/// 绑定/派发全都看不见这行（404 / 409）；而管理端在租户上下文里探测又会以该租户身份再插一份，
/// 同一台机器同一个 CLI 变成宿主一份 + 每租户一份、彼此不同步。租户的选择体现在按租户隔离的
/// <see cref="CliAgentBinding"/> 上（哪个租户的哪个 Agent 用哪个运行时），不在这张表上。
/// </para>
/// </remarks>
public class CliRuntime : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 执行位置的标识：本机运行时（<see cref="CliRuntimeMode.InProcess"/>）是探测它的那台机器名；
    /// 远程运行时（<see cref="CliRuntimeMode.RemoteDaemon"/>）是执行目标的标识（例如某个用户的工作区），由使用方登记。
    /// 与 <see cref="ProviderKey"/> 一起唯一：一个执行目标上的一个 CLI 一行，版本随登记更新。
    /// </summary>
    public string HostId { get; set; } = string.Empty;

    /// <summary>provider 键，对应描述表，如 "claude"。</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>展示名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>探测到的可执行文件绝对路径。</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>探测到的 CLI 版本。仅供观测，绝不用于选择行为分支。</summary>
    public string? CliVersion { get; set; }

    /// <summary>执行位置。</summary>
    public CliRuntimeMode Mode { get; set; } = CliRuntimeMode.InProcess;

    /// <summary>可用状态。</summary>
    public CliRuntimeStatus Status { get; set; } = CliRuntimeStatus.Offline;

    /// <summary>最近心跳。远程 daemon 自报；进程内 runtime 由探测服务刷新。</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>宿主信息 JSON（OS、架构、机器名等），仅供展示。</summary>
    public string? HostInfoJson { get; set; }

    /// <summary>
    /// 本 runtime 同时处于已派出 / 执行中的运行数上限。认领时执行（见 <c>CliRunQueueProcessor.TryClaimAsync</c>），
    /// 与进程级的 <c>AI:Cli:MaxConcurrentRuns</c> 同时生效、取较小者。探测登记新运行时时取进程级的值。
    /// </summary>
    public int MaxConcurrentRuns { get; set; } = 2;

    /// <summary>
    /// 远程运行时：最近一次请使用方唤起它的执行目标的时间。多个副本按它去重（<c>AI:Cli:Activation:RetryInterval</c> 内只问一次），上线时清空。
    /// </summary>
    public DateTime? ActivationRequestedAt { get; set; }

    /// <summary>
    /// 远程运行时：这一轮「等它上线」从什么时候开始（第一次请求唤起的时刻），上线时清空。
    /// 过了 <c>AI:Cli:Activation:Timeout</c> 还没上线，排在它上面的运行以 <see cref="CliRunFailureReason.TargetUnavailable"/> 结束。
    /// </summary>
    public DateTime? ActivationStartedAt { get; set; }
}
