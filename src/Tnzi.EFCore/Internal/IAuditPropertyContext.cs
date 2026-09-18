namespace Tnzi.EFCore.Internal;

/// <summary>
/// 审计填充在执行时向 DbContext 实例询问的三个协作者。
/// </summary>
/// <remarks>
/// <para>
/// <c>SaveChangesAsync</c> 用它们填创建人 / 创建时间 / 租户 / 并发戳；绕过变更跟踪器的写入路径
/// （Dapper 批量插入与批量更新）拿到的只是一个 <see cref="DbContext"/>，必须能从它身上拿到
/// <b>同一组</b>协作者，两条路径落库的审计值才一致。从应用容器另解析一份做不到这一点：
/// 手工构造的上下文（设计期、直接 <c>new</c> 的测试）没有应用容器，而它的 <c>CurrentUser</c> 是构造时给的。
/// </para>
/// <para>
/// 两个基类以显式实现转发到各自的 <c>protected</c> 同名属性，与 <see cref="IQueryFilterContext"/> 同一惯例。
/// </para>
/// </remarks>
public interface IAuditPropertyContext
{
    /// <summary>当前用户（创建人 / 修改人；租户 Id 的回退来源）。</summary>
    ICurrentUser CurrentUser { get; }

    /// <summary>当前租户；缺席时租户 Id 回退到 <see cref="ICurrentUser.TenantId"/>。</summary>
    ICurrentTenant? CurrentTenant { get; }

    /// <summary>审计时间戳的时钟；缺席时用 <see cref="System.TimeProvider.System"/>。</summary>
    TimeProvider? TimeProvider { get; }
}
