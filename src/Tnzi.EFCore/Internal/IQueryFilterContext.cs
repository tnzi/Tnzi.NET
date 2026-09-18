namespace Tnzi.EFCore.Internal;

/// <summary>
/// 查询过滤器在执行时向 DbContext 实例询问的三个值。
/// </summary>
/// <remarks>
/// <para>
/// 过滤器表达式里对 DbContext 成员的访问会被 EF 改写成「执行查询的那个实例」上的访问
/// （模型缓存后所有实例共用一份表达式，靠这条改写才做到按实例求值）。
/// 改写只认表达式里<b>直接</b>出现的 DbContext 常量，所以共用的过滤器构造器必须通过一个
/// 两个基类都实现的契约去访问这些成员，而不是把委托或闭包捕获进表达式 —— 后者会让每个实例
/// 都读到<b>第一个</b>建模实例的开关与租户。
/// </para>
/// <para>
/// 两个基类以显式实现转发到各自 <c>protected virtual</c> 的同名成员，消费方的覆写照常生效。
/// </para>
/// </remarks>
public interface IQueryFilterContext
{
    /// <summary>软删过滤器当前是否启用（<c>IDataFilterManager</c> 可临时关闭）。</summary>
    bool IsSoftDeleteFilterEnabled { get; }

    /// <summary>租户过滤器当前是否启用（多租户关闭时恒 false）。</summary>
    bool IsMultiTenantFilterEnabled { get; }

    /// <summary>当前租户 Id（查询时求值）。</summary>
    Guid? CurrentTenantId { get; }
}
