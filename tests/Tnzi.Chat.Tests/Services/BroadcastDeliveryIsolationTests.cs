using Tnzi.EventBus;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 广播扇出时，一个收件人失败不掀掉整轮。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>后果是重复投递。</b><c>DeliverAsync</c> 给每个收件人各起一个
/// <c>ExecuteInUnitOfWorkAsync</c> 单独提交，所以第 500 个抛异常时前 499 条<b>已经落库、
/// 已经推给对方了</b>。异常一路出栈的话 <c>RecordBroadcastAsync</c> 也不会执行，
/// 管理员看到一个 500，于是他重发一次 —— 前 499 个人收到两条。
/// </para>
/// <para>
/// ★ 取舍与 <c>RecordBroadcastAsync</c> 已有的那条一致（辅助操作失败不拖累主流程）：
/// 记下来、数出来、接着发，最后把失败数报回去 —— <b>报回去</b>是关键，
/// 静默吞掉会让「全部送到」与「少送了一百个」长得一样。
/// </para>
/// </remarks>
public class BroadcastDeliveryIsolationTests : Integration.IntegrationTestBase
{
    private readonly FlakyEventBus _eventBus = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IEventBus>(_ => _eventBus);
    }

    private IBroadcastService Broadcasts => ServiceProvider.GetRequiredService<IBroadcastService>();

    [Fact]
    public async Task One_failing_recipient_does_not_abort_the_rest_of_the_fan_out()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _eventBus.FailOnPublishNumber = 2;

        var result = await Broadcasts.BroadcastToUsersAsync(ids, "Maintenance at 21:00 UTC");

        // 没有异常出栈，调用方拿到的是一个结果而不是一个 500。
        result.Succeeded.ShouldBeTrue(result.Message);

        // 第 3 个人仍然收到了 —— 循环没有在第 2 个人那里断掉。
        _eventBus.PublishCount.ShouldBe(3);
    }

    /// <summary>失败的那一个必须<b>从成功数里减掉并说出来</b>，不能静默算作已投递。</summary>
    [Fact]
    public async Task The_failed_recipient_is_reported_instead_of_counted_as_delivered()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _eventBus.FailOnPublishNumber = 2;

        var result = await Broadcasts.BroadcastToUsersAsync(ids, "Maintenance at 21:00 UTC");

        result.Data.ShouldBe(2);
        result.Message!.ShouldContain("could not be delivered");
    }

    /// <summary>
    /// ★★ 审计行必须照样落库。它不落的话管理员看到的是一次「什么都没发生」的失败，
    /// 而实际上大多数人已经收到了 —— 那正是他会去重发的理由。
    /// </summary>
    [Fact]
    public async Task The_broadcast_is_still_recorded_when_one_recipient_fails()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _eventBus.FailOnPublishNumber = 2;

        await Broadcasts.BroadcastToUsersAsync(ids, "Maintenance at 21:00 UTC");

        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<BroadcastLog>().AsNoTracking().CountAsync()).ShouldBe(1);
    }

    /// <summary>对照：没人失败时不该冒出「有几个没送到」这句话。</summary>
    [Fact]
    public async Task A_clean_broadcast_says_nothing_about_failures()
    {
        var result = await Broadcasts.BroadcastToUsersAsync([Guid.NewGuid(), Guid.NewGuid()], "All good");

        result.Data.ShouldBe(2);
        (result.Message ?? string.Empty).ShouldNotContain("could not be delivered");
    }

    /// <summary>指定第 N 次发布时抛异常的事件总线。</summary>
    private sealed class FlakyEventBus : IEventBus
    {
        public int PublishCount { get; private set; }

        /// <summary>第几次 <c>PublishAsync</c> 抛异常（0 = 从不）。</summary>
        public int FailOnPublishNumber { get; set; }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
        {
            PublishCount++;
            if (PublishCount == FailOnPublishNumber)
                throw new InvalidOperationException("realtime push failed for this recipient");

            return Task.CompletedTask;
        }

        public Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent => Task.CompletedTask;

        public bool HasHandlers<TEvent>() where TEvent : class, IEvent => false;
        public int GetHandlerCount<TEvent>() where TEvent : class, IEvent => 0;

        public void Subscribe<TEvent, THandler>()
            where TEvent : class, IEvent
            where THandler : class, IEventHandler<TEvent> { }

        public void Unsubscribe<TEvent, THandler>()
            where TEvent : class, IEvent
            where THandler : class, IEventHandler<TEvent> { }

        public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent { }
    }
}
