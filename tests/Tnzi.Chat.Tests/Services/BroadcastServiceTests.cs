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

    [Fact]
    public async Task NotifyUsers_Should_Persist_Rich_Fields_And_Record_Source()
    {
        var uid = Guid.NewGuid();
        var r = await Broadcast.NotifyUsersAsync(new[] { uid }, new ChatNotification
        {
            Content = "Your order #1001 has shipped.",
            Title = "Order shipped",
            LinkUrl = "/orders/1001",
            Category = "order",
            Source = "OrderModule"
        });

        r.Succeeded.ShouldBeTrue(r.Message);
        r.Data.ShouldBe(1);

        var key = $"system:{uid:N}";
        var conv = await DbContext.Set<Conversation>().FirstAsync(c => c.DirectKey == key);
        var msg = await DbContext.Set<ChatMessage>().FirstAsync(m => m.ConversationId == conv.Id);
        msg.ContentType.ShouldBe(MessageContentType.System);
        msg.Title.ShouldBe("Order shipped");
        msg.LinkUrl.ShouldBe("/orders/1001");
        msg.Category.ShouldBe("order");

        // Programmatic sends are now audited (gap closed) with the caller-supplied source.
        var log = await DbContext.Set<BroadcastLog>().FirstAsync();
        log.Source.ShouldBe("OrderModule");
        log.Content.ShouldBe("Your order #1001 has shipped.");
        log.TargetType.ShouldBe(BroadcastTargetType.Users);
        log.RecipientCount.ShouldBe(1);
    }

    [Fact]
    public async Task BroadcastToUsers_Should_Now_Record_Audit_Log()
    {
        var uid = Guid.NewGuid();
        await Broadcast.BroadcastToUsersAsync(new[] { uid }, "plain programmatic notice");

        var log = await DbContext.Set<BroadcastLog>().FirstAsync();
        log.Content.ShouldBe("plain programmatic notice");
        log.Source.ShouldBeNull();               // plain overload carries no source
        log.TargetType.ShouldBe(BroadcastTargetType.Users);
        log.RecipientCount.ShouldBe(1);
    }
}
