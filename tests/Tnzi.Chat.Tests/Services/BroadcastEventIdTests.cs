using Mapster;
using MapsterMapper;
using Tnzi.Chat.Events;
using Tnzi.Chat.Mappings;
using Tnzi.EventBus;
using Tnzi.Mapster;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// Capturing IEventBus that records every published event for assertion.
/// </summary>
internal sealed class CapturingEventBus : IEventBus
{
    public List<object> Published { get; } = new();

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        Published.Add(@event);
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

/// <summary>
/// Focused tests proving BroadcastService publishes ConversationMessageSentEvent
/// with real (non-empty) IDs after the UoW commit.
/// </summary>
public class BroadcastEventIdTests : Integration.IntegrationTestBase
{
    private readonly CapturingEventBus _eventBus = new();

    public BroadcastEventIdTests()
    {
        var config = new TypeAdapterConfig();
        new ChatMappingConfig().Configure(config);
        MapperExtensions.SetMapper(new Mapper(config));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // Register the capturing bus so ApplicationService.EventBus resolves it.
        services.AddScoped<IEventBus>(_ => _eventBus);
    }

    [Fact]
    public async Task BroadcastToUsers_PublishesEvent_With_Real_NonEmpty_Ids()
    {
        var uid = Guid.NewGuid();

        var broadcastService = ServiceProvider.GetRequiredService<IBroadcastService>();
        var result = await broadcastService.BroadcastToUsersAsync(new[] { uid }, "hello event test");

        result.Succeeded.ShouldBeTrue(result.Message);

        // Exactly one event must have been published.
        _eventBus.Published.Count.ShouldBe(1);
        var evt = _eventBus.Published[0].ShouldBeOfType<ConversationMessageSentEvent>();

        // IDs must be non-empty - old code captured Guid.Empty before SaveChangesAsync.
        evt.ConversationId.ShouldNotBe(Guid.Empty);
        evt.MessageId.ShouldNotBe(Guid.Empty);

        // Recipient must be the target user.
        evt.RecipientUserIds.ShouldContain(uid);

        // ConversationId must match the System conversation persisted to DB.
        var key = $"system:{uid:N}";
        var conv = await DbContext.Set<Conversation>().FirstAsync(c => c.DirectKey == key);
        evt.ConversationId.ShouldBe(conv.Id);

        // MessageId must match the ChatMessage persisted to DB.
        var msg = await DbContext.Set<ChatMessage>()
            .FirstAsync(m => m.ConversationId == conv.Id && m.ContentType == MessageContentType.System);
        evt.MessageId.ShouldBe(msg.Id);
    }
}
