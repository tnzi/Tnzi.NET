namespace Tnzi.SignalR.Filters;

/// <summary>
/// 把每次连接 / 断开 / 方法调用的 Hub 激活作用域暴露给 <c>TnziHub</c>。
///
/// ★ 为什么需要它：<c>TnziHub</c> 的可选能力（事件总线、日志、中断表、无参构造时的
/// 连接管理器）此前一律从 <c>Context.GetHttpContext().RequestServices</c> 惰性解析。
/// 那个来源只对 WebSocket / SSE 可靠：SignalR 给**长轮询**连接的是一份克隆的
/// <c>HttpContext</c>，它的请求服务作用域随连接一起释放，而 <c>OnDisconnectedAsync</c>
/// 在释放之后才运行 —— 解析抛 <see cref="ObjectDisposedException"/>、被按 null 处理，
/// 于是断开事件不发（presence 永远不下线）、中断表不注销，且没有任何日志。
///
/// <see cref="HubLifetimeContext.ServiceProvider"/> / <see cref="HubInvocationContext.ServiceProvider"/>
/// 是激活这个 Hub 实例的那个作用域，三种传输都有，且在回调返回前不会被释放。
/// 它只在调用期间有效，所以调用结束后要收回，而不是让连接一直持有一个即将释放的作用域。
///
/// ★ 并行调用（<c>MaximumParallelInvocationsPerClient &gt; 1</c>）的顺序是**交错**而不是嵌套
/// （A 开始、B 开始、A 结束、B 结束）：「恢复进入前的值」在交错时会在 A 结束那一刻把键删掉
/// （B 的 Hub 退回 HttpContext 回退），又在 B 结束时把 A 那个**已释放**的作用域写回去并留到下一次回调。
/// 所以按连接记一份「还在跑的作用域」列表，可见的始终是其中最近开始的那一个，
/// 一次调用结束只摘掉自己那一项；列表空了才把键删掉。
///
/// 没有依赖，模块按实例注册且排在过滤器链最前。
/// </summary>
public sealed class HubInvocationServicesFilter : IHubFilter
{
    /// <summary>
    /// <c>Context.Items</c> 里存放当前调用的激活作用域（<see cref="IServiceProvider"/>）的键。
    /// </summary>
    public const string ItemKey = "Tnzi.SignalR.InvocationServices";

    /// <inheritdoc />
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        Check.NotNull(invocationContext);
        Check.NotNull(next);

        Expose(invocationContext.Context, invocationContext.ServiceProvider);
        try
        {
            return await next(invocationContext);
        }
        finally
        {
            Withdraw(invocationContext.Context, invocationContext.ServiceProvider);
        }
    }

    /// <inheritdoc />
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        Expose(context.Context, context.ServiceProvider);
        try
        {
            await next(context);
        }
        finally
        {
            Withdraw(context.Context, context.ServiceProvider);
        }
    }

    /// <inheritdoc />
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        Expose(context.Context, context.ServiceProvider);
        try
        {
            await next(context, exception);
        }
        finally
        {
            Withdraw(context.Context, context.ServiceProvider);
        }
    }

    /// <summary>
    /// 这条连接上还在跑的激活作用域，按开始顺序。<c>Context.Items</c> 本身不是线程安全的字典，
    /// 而并行调用会同时进出，所以对它的每次写都在这个列表的锁里。
    /// </summary>
    private const string ActiveKey = ItemKey + ".Active";

    private static List<IServiceProvider> ActiveOf(HubCallerContext context)
    {
        lock (context.Items)
        {
            if (context.Items.TryGetValue(ActiveKey, out var existing) && existing is List<IServiceProvider> active)
            {
                return active;
            }

            var created = new List<IServiceProvider>();
            context.Items[ActiveKey] = created;
            return created;
        }
    }

    private static void Expose(HubCallerContext context, IServiceProvider services)
    {
        var active = ActiveOf(context);
        lock (active)
        {
            active.Add(services);
            context.Items[ItemKey] = services;
        }
    }

    private static void Withdraw(HubCallerContext context, IServiceProvider services)
    {
        var active = ActiveOf(context);
        lock (active)
        {
            // 只摘自己那一项（从后往前找：同一个作用域嵌套出现时摘最近的那次）。
            var index = active.LastIndexOf(services);
            if (index >= 0)
            {
                active.RemoveAt(index);
            }

            // 列表本身随连接一直留着（不摘）：摘了之后一次并发的 Expose 可能还握着旧列表，
            // 两份列表各记各的，先结束的那次会把另一次的键删掉。
            if (active.Count == 0)
            {
                context.Items.Remove(ItemKey);
            }
            else
            {
                context.Items[ItemKey] = active[^1];
            }
        }
    }
}
