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
/// <see cref="CliVersion"/> <b>仅供观测</b>：一旦有代码按版本号选行为分支，CLI 的每次
/// 小版本升级都会变成一次线上事故排查。协议漂移靠打标签的真机冒烟测试发现，不靠版本号猜。
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
    /// <summary>宿主标识（进程内 runtime = 机器名；远程 daemon = daemon 自报 ID）。</summary>
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

    /// <summary>本 runtime 最大并发运行数。</summary>
    public int MaxConcurrentRuns { get; set; } = 2;
}
