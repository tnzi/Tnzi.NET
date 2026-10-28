namespace Tnzi.Payment.Subscriptions.Middleware;

/// <summary>
/// 服务暂停时的 SignalR 门禁：已暂停账户的用户建不了连接、调不了方法（<see cref="HubException"/> 的消息是 <c>SUBSCRIPTION_SUSPENDED</c>）。
/// </summary>
/// <remarks>
/// <para>
/// HTTP 中间件只看得见协商与建连那几个请求。一条在暂停之前就连上的 WebSocket 连接，此后每一次方法调用都不经过任何 HTTP 中间件：
/// 只靠中间件，停服前打开的标签页会一直能发消息。所以连接与调用两个点都在这里再判一次（判定经同一个
/// <see cref="ISubscriptionAccessGate"/>，有缓存，不是每次调用都查库）。
/// </para>
/// <para>
/// 作为全局过滤器追加在 <c>HubOptions</c> 上，排在 SignalR 模块自己的作用域、异常、授权过滤器之后：未认证、无权限的调用先被那几个拒掉。
/// 没有加载 SignalR 时这条配置什么都不做。
/// </para>
/// </remarks>
public class SubscriptionAccessGateHubFilter : IHubFilter
{
    private readonly IOptionsMonitor<SubscriptionAccessGateOptions> _options;

    /// <summary>DI 构造函数。门禁服务是 scoped 的，从每次调用自己的作用域解析。</summary>
    public SubscriptionAccessGateHubFilter(IOptionsMonitor<SubscriptionAccessGateOptions> options)
    {
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        await EnsureAllowedAsync(context.Context, context.ServiceProvider);
        await next(context);
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        Check.NotNull(invocationContext);
        Check.NotNull(next);

        await EnsureAllowedAsync(invocationContext.Context, invocationContext.ServiceProvider);
        return await next(invocationContext);
    }

    private async Task EnsureAllowedAsync(HubCallerContext caller, IServiceProvider services)
    {
        if (!_options.CurrentValue.Enabled)
            return;

        var userId = SubscriptionAccessGateUser.IdOf(caller.User);
        if (!userId.HasValue)
            return;

        var gate = services.GetRequiredService<ISubscriptionAccessGate>();
        var decision = await gate.EvaluateAsync(userId.Value, caller.ConnectionAborted);
        if (!decision.Allowed)
            throw new HubException(ErrorCodes.SubscriptionSuspended);
    }
}
