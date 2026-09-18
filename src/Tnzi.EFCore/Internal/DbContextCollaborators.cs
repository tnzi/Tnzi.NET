namespace Tnzi.EFCore.Internal;

/// <summary>
/// 两个 DbContext 基类共用的可选协作者解析：<see cref="ICurrentTenant"/>、<see cref="IDataFilterManager"/>、
/// <see cref="TimeProvider"/>。
/// </summary>
/// <remarks>
/// <para>
/// 优先级：消费方显式转发的构造实参 &gt; options 携带的应用容器
/// （<see cref="CoreOptionsExtension.ApplicationServiceProvider"/>，<c>AddDbContext</c> 记下的请求作用域容器）&gt; <c>null</c>。
/// </para>
/// <para>
/// ★ 与 <see cref="MultiTenancySwitch"/> 同一个根因：这三个协作者此前只能经<b>可选</b>构造形参到达基类，
/// 而脚手架 / 文档 / 消费方 DbContext 一律只声明 <c>(DbContextOptions, ICurrentUser)</c>，DI 填不了没声明的形参。
/// 于是开关开了、过滤器加了，它过滤的租户却只剩 JWT claim：凡经 <c>ICurrentTenant.Change()</c> 建立的租户
/// （请求头解析、按租户轮转的后台作业、注册登录流程、事件处理器）对过滤器与 <c>TenantId</c> 赋值一律不可见，
/// <c>IDataFilterManager.Disable&lt;IMultiTenantFilter&gt;()</c> 是空操作，换掉的 <c>TimeProvider</c> 不影响审计时间戳。
/// 全程接口 200、零日志。开关搬到 options 那次只搬了开关，这三个漏了。
/// </para>
/// <para>
/// 手工 <c>new</c> 出来的上下文（设计期工厂、直接构造的测试）没有应用容器，答案仍是 <c>null</c>，
/// 与既往行为逐字相同。解析发生在构造时：三者的默认实现都把状态放在静态 <c>AsyncLocal</c> 上
/// （<c>CurrentTenant</c> / <c>DataFilterManager</c>），任一实例都读得到 <c>Change()</c> / <c>Disable()</c> 的覆盖。
/// </para>
/// </remarks>
public static class DbContextCollaborators
{
    /// <summary>注入值缺席时从 options 携带的应用容器解析；没有容器时返回 <c>null</c>。</summary>
    public static TService? Resolve<TService>(TService? injected, IDbContextOptions options) where TService : class
    {
        Check.NotNull(options);
        if (injected != null)
        {
            return injected;
        }

        var applicationServiceProvider = options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
        return applicationServiceProvider?.GetService<TService>();
    }
}
