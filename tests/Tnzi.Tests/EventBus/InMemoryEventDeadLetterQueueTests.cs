namespace Tnzi.Tests.EventBus;

/// <summary>
/// 内存死信队列按「事件 + 处理器」记账：同一事件的多个处理器都失败时每个处理器各留一条。
/// 此前以 EventId 为键，第二个失败处理器的记录被静默丢弃，而总线日志声称它已入队，
/// 运维按死信重放时那个处理器的副作用（例如 CRM 同步）就永远缺失。
/// </summary>
public class InMemoryEventDeadLetterQueueTests
{
    private sealed class SampleEvent : EventBase;

    private sealed class WelcomeEmailHandler;

    private sealed class CrmSyncHandler;

    private static InMemoryEventDeadLetterQueue Create(int capacity = 1000)
        => new(Microsoft.Extensions.Options.Options.Create(new EventBusOptions { DeadLetterQueueCapacity = capacity }));

    [Fact]
    public async Task TwoFailingHandlersOfOneEvent_LeaveTwoEntries()
    {
        var queue = Create();
        var @event = new SampleEvent();

        await queue.AddAsync(@event, typeof(WelcomeEmailHandler), new InvalidOperationException("smtp down"));
        await queue.AddAsync(@event, typeof(CrmSyncHandler), new InvalidOperationException("crm down"));

        var entries = await queue.GetAllAsync();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.HandlerTypeName.EndsWith(nameof(WelcomeEmailHandler)) && e.ExceptionMessage == "smtp down");
        Assert.Contains(entries, e => e.HandlerTypeName.EndsWith(nameof(CrmSyncHandler)) && e.ExceptionMessage == "crm down");
    }

    [Fact]
    public async Task RemoveAsync_RemovesEveryEntryOfTheEvent()
    {
        var queue = Create();
        var removed = new SampleEvent();
        var kept = new SampleEvent();
        await queue.AddAsync(removed, typeof(WelcomeEmailHandler), new InvalidOperationException("a"));
        await queue.AddAsync(removed, typeof(CrmSyncHandler), new InvalidOperationException("b"));
        await queue.AddAsync(kept, typeof(CrmSyncHandler), new InvalidOperationException("c"));

        await queue.RemoveAsync(removed.EventId);

        var entry = Assert.Single(await queue.GetAllAsync());
        Assert.Equal(kept.EventId, entry.EventId);
    }

    [Fact]
    public async Task TheSameHandlerFailingAgain_KeepsOneEntry_WithTheLatestFailure()
    {
        var queue = Create();
        var @event = new SampleEvent();

        await queue.AddAsync(@event, typeof(CrmSyncHandler), new InvalidOperationException("first"));
        await queue.AddAsync(@event, typeof(CrmSyncHandler), new InvalidOperationException("second"));

        var entry = Assert.Single(await queue.GetAllAsync());
        Assert.Equal("second", entry.ExceptionMessage);
    }

    [Fact]
    public async Task EventTypeName_IsTheRuntimeType_EvenWhenAddedThroughABaseType()
    {
        var queue = Create();
        IEvent @event = new SampleEvent();

        await queue.AddAsync(@event, typeof(CrmSyncHandler), new InvalidOperationException("x"));

        Assert.Equal(typeof(SampleEvent).FullName, Assert.Single(await queue.GetAllAsync()).EventTypeName);
    }

    [Fact]
    public async Task Capacity_StillBoundsTheQueue()
    {
        var queue = Create(capacity: 3);
        var @event = new SampleEvent();

        await queue.AddAsync(@event, typeof(CrmSyncHandler), new InvalidOperationException("1"));
        await queue.AddAsync(@event, typeof(WelcomeEmailHandler), new InvalidOperationException("2"));
        for (var i = 0; i < 5; i++)
        {
            await queue.AddAsync(new SampleEvent(), typeof(CrmSyncHandler), new InvalidOperationException("n"));
        }

        Assert.Equal(3, (await queue.GetAllAsync()).Count);
    }
}
