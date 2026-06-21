namespace Tnzi.Chat.Tests.Services;

public class BroadcastServiceTests : Integration.IntegrationTestBase
{
    private IBroadcastService Broadcast => ServiceProvider.GetRequiredService<IBroadcastService>();

    [Fact]
    public async Task BroadcastToUsers_Should_Create_System_Conversation_And_Unread()
    {
        var u1 = Guid.NewGuid();
        var r = await Broadcast.BroadcastToUsersAsync(new[] { u1 }, "Server maintenance tonight");

        r.Succeeded.ShouldBeTrue(r.Message);
        r.Data.ShouldBe(1);

        var key = $"system:{u1:N}";
        var conv = await DbContext.Set<Conversation>().FirstAsync(c => c.DirectKey == key);
        conv.Type.ShouldBe(ConversationType.System);

        var member = await DbContext.Set<ConversationMember>().FirstAsync(m => m.ConversationId == conv.Id && m.UserId == u1);
        member.UnreadCount.ShouldBe(1);

        (await DbContext.Set<ChatMessage>().CountAsync(m => m.ConversationId == conv.Id && m.ContentType == MessageContentType.System)).ShouldBe(1);
    }

    [Fact]
    public async Task BroadcastToUsers_Twice_Reuses_System_Conversation()
    {
        var u1 = Guid.NewGuid();
        await Broadcast.BroadcastToUsersAsync(new[] { u1 }, "first");
        await Broadcast.BroadcastToUsersAsync(new[] { u1 }, "second");

        var key = $"system:{u1:N}";
        (await DbContext.Set<Conversation>().CountAsync(c => c.DirectKey == key)).ShouldBe(1);
        var member = await DbContext.Set<ConversationMember>().FirstAsync(m => m.UserId == u1);
        member.UnreadCount.ShouldBe(2);
    }
}
