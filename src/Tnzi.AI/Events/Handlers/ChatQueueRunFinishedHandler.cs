namespace Tnzi.AI.Events.Handlers;

/// <summary>
/// 线程上的一轮外部运行结束：等它的排队消息往前走（合成一轮发出，或者这一轮被停下时扣住）。
/// </summary>
/// <remarks>
/// <para>
/// 后台处理器：发布方是运行的终态转换（执行器在清理工作区、结算配额之前），发出下一轮不该拖住它。
/// 后台没有当前用户，消息以排队的用户的身份发出（见 <see cref="ChatQueueDelivery"/>）；租户由总线按事件上的 TenantId 恢复。
/// </para>
/// <para>
/// 事件只在记下终态的那个副本上派发，提交之后、发布之前进程死掉，这一次就没有了：定时扫描（<see cref="ChatQueueSweepHostedService"/>）补上。
/// 异常照常冒泡给总线（重试与死信）；重试安全，消息逐行以「还在等待」为条件认领。
/// </para>
/// </remarks>
[BackgroundEventHandler]
public class ChatQueueRunFinishedHandler : IEventHandler<CliRunFinishedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ChatQueueRunFinishedHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
    }

    public async Task HandleAsync(CliRunFinishedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);
        if (@event.ThreadId is not { } threadId)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var delivery = scope.ServiceProvider.GetRequiredService<IChatQueueDelivery>();
        await delivery.AdvanceAsync(threadId, @event.RunId, stopped: @event.Status == CliRunStatus.Cancelled, cancellationToken);
    }
}
