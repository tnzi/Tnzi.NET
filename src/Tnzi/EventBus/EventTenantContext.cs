namespace Tnzi.EventBus;

/// <summary>
/// <see cref="EventBase.TenantId"/> 契约的两半：发布时<b>捕获</b>环境租户、处理器执行前<b>恢复</b>它。
/// </summary>
/// <remarks>
/// <para>
/// <b>被修复的缺陷</b>：这条契约此前只有 <see cref="LocalEventBus"/> 兑现。集成事件从
/// <c>ApplicationService.PublishEventAsync</c> 出发时<b>绕过</b>本地总线（Outbox 落库 / 直接交给
/// <see cref="IDistributedEventBus"/>），三条路径都原样序列化 —— 线上 JSON 的 <c>TenantId</c> 恒为 null；
/// 两个传输的消费侧又只 <c>CreateScope()</c> 就跑处理器，中间没有任何 <c>ICurrentTenant.Change</c>。
/// 多租户开启时，租户 A 的请求发出的事件在另一个实例上跑在 <b>null 租户</b>作用域里：
/// 全局租户过滤器是严格等值，处理器读到的是空结果集（表现为「记录不存在」），写出的行 TenantId 为 null，
/// 落在任何租户都查不到的位置。全程不抛异常、两侧都返回成功。
/// </para>
/// <para>
/// 两半都收口在这里，本地总线与两个分布式传输<b>调同一段代码</b>：共用一个类就没有再分叉的余地。
/// 捕获只在 <c>TenantId</c> 为 null 时发生（业务代码显式设置的值优先）；恢复只在有值时发生
/// （无租户的事件不去 <c>Change(null)</c> 覆盖处理器所在环境）。
/// </para>
/// </remarks>
[StableApi(Since = "0.1.0")]
public static class EventTenantContext
{
    /// <summary>
    /// 这个事件还需要捕获租户吗（是 <see cref="EventBase"/> 且 <c>TenantId</c> 尚未设置）。
    /// </summary>
    public static bool NeedsCapture(IEvent @event)
    {
        Check.NotNull(@event);
        return @event is EventBase { TenantId: null };
    }

    /// <summary>
    /// 从给定（作用域内的）服务提供者解析 <see cref="ICurrentTenant"/>，把环境租户写进事件。
    /// </summary>
    /// <remarks>
    /// 首选传调用方自己的作用域。<c>Change()</c> 的临时覆盖存在 <c>CurrentTenant</c> 的静态 AsyncLocal 上
    /// （同一异步流里的新作用域也看得见），但调用方作用域是最不依赖这条实现细节的读法。
    /// </remarks>
    public static void Capture(IEvent @event, IServiceProvider scopedServiceProvider)
    {
        Check.NotNull(scopedServiceProvider);

        if (!NeedsCapture(@event))
            return;

        var currentTenant = scopedServiceProvider.GetService<ICurrentTenant>();
        ((EventBase)@event).TenantId = currentTenant?.Id;
    }

    /// <summary>
    /// 单例（没有调用方作用域可用）的捕获入口：只在确实需要时才开一个作用域去读环境租户。
    /// </summary>
    /// <remarks>
    /// 这是<b>兜底</b>不是首选：新作用域里的 <see cref="ICurrentTenant"/> 看得见请求身份带来的租户，也看得见
    /// 同一异步流里 <c>Change()</c> 的临时覆盖（覆盖存在静态 AsyncLocal 上），但看不见另一条异步流上的覆盖。
    /// 首选是在路由收口处（<c>PublishEventAsync</c>）用 <see cref="Capture"/> 从调用方作用域捕获；
    /// 本方法服务的是直接注入分布式总线发布的调用方。
    /// </remarks>
    public static void CaptureInNewScope(IEvent @event, IServiceProvider serviceProvider)
    {
        Check.NotNull(serviceProvider);

        if (!NeedsCapture(@event))
            return;

        using var scope = serviceProvider.CreateScope();
        Capture(@event, scope.ServiceProvider);
    }

    /// <summary>
    /// 按事件携带的 <c>TenantId</c> 切换给定作用域的当前租户；处理器跑完后释放返回值即恢复。
    /// </summary>
    /// <returns>租户切换范围；事件没有租户或环境里没有 <see cref="ICurrentTenant"/> 时为 null。</returns>
    public static IDisposable? Restore(IEvent @event, IServiceProvider scopedServiceProvider)
    {
        Check.NotNull(@event);
        Check.NotNull(scopedServiceProvider);

        if (@event is not EventBase { TenantId: { } tenantId })
            return null;

        return scopedServiceProvider.GetService<ICurrentTenant>()?.Change(tenantId);
    }
}
