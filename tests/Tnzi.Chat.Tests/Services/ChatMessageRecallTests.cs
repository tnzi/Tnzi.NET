using Tnzi.EventBus;
using Tnzi.Chat.Events;
using Tnzi.Chat.Tests.Integration;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 撤回（本人撤回与管理端强制撤回）不只是软删消息：撤回的是最后一条时会话预览要换掉，
/// 开着会话的成员要收到撤回事件：否则被撤回的文字照样留在每个成员的会话列表与打开着的窗口里。
/// </summary>
public class ChatMessageRecallTests : IntegrationTestBase
{
    private static readonly Guid Peer = Guid.Parse("00000000-0000-0000-0000-0000000000d0");
    private readonly CapturingEventBus _eventBus = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IEventBus>(_ => _eventBus);
    }

    [Fact]
    public async Task RecallingTheLatestMessage_ReplacesThePreviewAndNotifiesMembers()
    {
        var (convId, _, latest) = await SeedConversationAsync();

        var result = await ServiceProvider.GetRequiredService<IConversationService>().DeleteMessageAsync(latest);
        result.Succeeded.ShouldBeTrue(result.Message);

        var conv = await ReadConversationAsync(convId);
        conv.LastMessagePreview.ShouldBe("[Message recalled]");
        conv.LastMessageAt.ShouldBe(LatestAt, "the conversation must not jump in the list");

        var evt = _eventBus.Published.OfType<ConversationMessageRecalledEvent>().ShouldHaveSingleItem();
        evt.ConversationId.ShouldBe(convId);
        evt.MessageId.ShouldBe(latest);
        evt.Preview.ShouldBe("[Message recalled]");
        evt.RecipientUserIds.OrderBy(id => id).ShouldBe(new[] { CurrentUserId, Peer }.OrderBy(id => id));
    }

    [Fact]
    public async Task RecallingAnOlderMessage_KeepsThePreview()
    {
        var (convId, older, _) = await SeedConversationAsync();

        (await ServiceProvider.GetRequiredService<IConversationService>().DeleteMessageAsync(older)).Succeeded.ShouldBeTrue();

        (await ReadConversationAsync(convId)).LastMessagePreview.ShouldBe("latest secret");
        var evt = _eventBus.Published.OfType<ConversationMessageRecalledEvent>().ShouldHaveSingleItem();
        evt.MessageId.ShouldBe(older);
        evt.Preview.ShouldBeNull();
    }

    [Fact]
    public async Task AdminForcedRecall_ReplacesThePreviewAndNotifiesMembers()
    {
        var (convId, _, latest) = await SeedConversationAsync(sender: Peer);

        var result = await ServiceProvider.GetRequiredService<IChatAdminService>().DeleteMessageAsync(latest);
        result.Succeeded.ShouldBeTrue(result.Message);

        (await ReadConversationAsync(convId)).LastMessagePreview.ShouldBe("[Message recalled]");
        var evt = _eventBus.Published.OfType<ConversationMessageRecalledEvent>().ShouldHaveSingleItem();
        evt.RecipientUserIds.ShouldContain(Peer);
        evt.RecipientUserIds.ShouldContain(CurrentUserId);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static readonly DateTime LatestAt = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);

    private async Task<(Guid ConversationId, Guid Older, Guid Latest)> SeedConversationAsync(Guid? sender = null)
    {
        var from = sender ?? CurrentUserId;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();

        var conv = new Conversation
        {
            Id = Guid.NewGuid(),
            Type = ConversationType.Group,
            Title = "g",
            OwnerId = CurrentUserId,
            MemberCount = 2,
            LastMessageAt = LatestAt,
            LastMessagePreview = "latest secret"
        };
        db.Set<Conversation>().Add(conv);
        db.Set<ConversationMember>().Add(new ConversationMember { Id = Guid.NewGuid(), ConversationId = conv.Id, UserId = CurrentUserId, Role = MemberRole.Owner });
        db.Set<ConversationMember>().Add(new ConversationMember { Id = Guid.NewGuid(), ConversationId = conv.Id, UserId = Peer, Role = MemberRole.Member });

        var older = new ChatMessage { Id = Guid.NewGuid(), ConversationId = conv.Id, SenderId = from, SentAt = LatestAt.AddMinutes(-5), Content = "older" };
        var latest = new ChatMessage { Id = Guid.NewGuid(), ConversationId = conv.Id, SenderId = from, SentAt = LatestAt, Content = "latest secret" };
        db.Set<ChatMessage>().AddRange(older, latest);

        await db.SaveChangesAsync();
        return (conv.Id, older.Id, latest.Id);
    }

    private async Task<Conversation> ReadConversationAsync(Guid id)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatTestDbContext>();
        return await db.Set<Conversation>().AsNoTracking().FirstAsync(c => c.Id == id);
    }
}
