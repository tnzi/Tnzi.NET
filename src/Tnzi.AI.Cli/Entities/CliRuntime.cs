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
/// </remarks>
public class CliRuntime : MultiTenantAuditedEntity<Guid>
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
