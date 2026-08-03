
namespace Tnzi.Chat.Tests.Services;

public class ConversationServiceListTests : Integration.IntegrationTestBase
{
    private IConversationService Service => ServiceProvider.GetRequiredService<IConversationService>();

    [Fact]
    public async Task GetMyConversations_Should_List_With_Unread_For_Receiver()
    {
        // 当前用户作为接收方：用 GetOrCreateDirect 建会话，再由对方发消息
        var other = Guid.NewGuid();
        var conv = (await Service.GetOrCreateDirectAsync(other)).Data!;

        // 模拟对方发来一条：直接给当前用户成员 +1 未读 + 写消息
        var msg = new ChatMessage { ConversationId = conv.Id, SenderId = other, ContentType = MessageContentType.Text, Content = "yo", SentAt = DateTime.UtcNow };
        DbContext.Set<ChatMessage>().Add(msg);
        var myMember = await DbContext.Set<ConversationMember>().FirstAsync(m => m.ConversationId == conv.Id && m.UserId == CurrentUserId);
        myMember.UnreadCount = 1;
        var c = await DbContext.Set<Conversation>().FindAsync(conv.Id);
        c!.LastMessageAt = DateTime.UtcNow; c.LastMessagePreview = "yo";
        await DbContext.SaveChangesAsync();

        var list = await Service.GetMyConversationsAsync();
        list.Succeeded.ShouldBeTrue();
        var item = list.Data!.First(x => x.Id == conv.Id);
        item.UnreadCount.ShouldBe(1);
        item.LastMessagePreview.ShouldBe("yo");
    }

    [Fact]
    public async Task MarkRead_Should_Clear_Unread()
    {
        var conv = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        var myMember = await DbContext.Set<ConversationMember>().FirstAsync(m => m.ConversationId == conv.Id && m.UserId == CurrentUserId);
        myMember.UnreadCount = 3;
        await DbContext.SaveChangesAsync();

        var r = await Service.MarkReadAsync(conv.Id);
        r.Succeeded.ShouldBeTrue();

        var reloaded = await DbContext.Set<ConversationMember>().FirstAsync(m => m.ConversationId == conv.Id && m.UserId == CurrentUserId);
        reloaded.UnreadCount.ShouldBe(0);
        reloaded.LastReadAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetTotalUnread_Should_Sum_Across_Conversations()
    {
        var a = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        var b = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        foreach (var id in new[] { a.Id, b.Id })
        {
            var mm = await DbContext.Set<ConversationMember>().FirstAsync(m => m.ConversationId == id && m.UserId == CurrentUserId);
            mm.UnreadCount = 2;
        }
        await DbContext.SaveChangesAsync();

        var total = await Service.GetTotalUnreadAsync();
        total.Succeeded.ShouldBeTrue();
        total.Data.ShouldBe(4);
    }

    [Fact]
    public async Task Mute_Then_DeleteOwnMessage()
    {
        var conv = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        var sent = (await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "del me" })).Data!;

        (await Service.MuteAsync(conv.Id, true)).Succeeded.ShouldBeTrue();
        var mm = await DbContext.Set<ConversationMember>().FirstAsync(m => m.ConversationId == conv.Id && m.UserId == CurrentUserId);
        mm.IsMuted.ShouldBeTrue();

        (await Service.DeleteMessageAsync(sent.Id)).Succeeded.ShouldBeTrue();
        var msg = await DbContext.Set<ChatMessage>().IgnoreQueryFilters().FirstAsync(m => m.Id == sent.Id);
        msg.IsDeleted.ShouldBeTrue();
    }
}
