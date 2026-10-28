namespace Tnzi.AI.Cli.Entities;

/// <summary>
/// 外部运行的一个权限请求：CLI 要做一件「先问我」档位不自动放行的事，等使用方回答。
/// </summary>
/// <remarks>
/// <para>
/// 请求本身是事件流里的一条 <c>PermissionRequest</c> 事件（<see cref="Sequence"/>），这一行记的是它的生命周期：
/// 回答接口（可能落在任何一个服务端副本上）按「还在等」为条件写下回答，执行这次运行的副本轮询到之后写给 CLI，
/// 并把写出那一刻的事件序号记在 <see cref="ResolvedSequence"/>。
/// </para>
/// <para>
/// ★ <see cref="ResolvedSequence"/> 是重新附着的依据：重放时适配器从进程第一行输出重来，执行器在重放到这个序号时把同一个回答再交给适配器一次
/// （那一行 stdin 进程早已收到，被丢掉），事件序号与 stdin 行数因此都与停机前逐个对得上。停机时还没写给 CLI 的回答没有这一列，
/// 重放结束之后照常写出。
/// </para>
/// </remarks>
public class CliRunPermissionRequest : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户。</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属运行。</summary>
    public Guid RunId { get; set; }

    /// <summary>请求事件在运行里的序号。</summary>
    public int Sequence { get; set; }

    /// <summary>适配器认这个请求用的键（CLI 的请求 id），只在这一个进程里唯一。</summary>
    public string ProtocolKey { get; set; } = string.Empty;

    /// <summary>工具名。</summary>
    public string? Tool { get; set; }

    /// <summary>可选的回答（<c>CliPermissionOption</c> 的 JSON 数组）。</summary>
    public string OptionsJson { get; set; } = "[]";

    /// <summary>状态：等回答，或结局。</summary>
    public CliPermissionStatus Status { get; set; } = CliPermissionStatus.Pending;

    /// <summary>选中的选项 id（允许 / 拒绝时）。</summary>
    public string? OptionId { get; set; }

    /// <summary>拒绝时交给 agent 的说明。</summary>
    public string? Message { get; set; }

    /// <summary>回答的人。</summary>
    public Guid? AnsweredById { get; set; }

    /// <summary>回答（或超时、随运行结束）的时间。</summary>
    public DateTime? AnsweredAt { get; set; }

    /// <summary>过了这个时刻（UTC）没人回答就按拒绝交给 CLI。从请求出现时算起，服务端重启不重新计时。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>结局写给 CLI 的那一刻的事件序号（<c>PermissionResolved</c> 事件）；还没写给 CLI 时为 null。</summary>
    public int? ResolvedSequence { get; set; }
}
