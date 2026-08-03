
namespace Tnzi.Chat.Tests.Services;

public class ConversationServiceDirectTests : Integration.IntegrationTestBase
{
    private IConversationService Service => ServiceProvider.GetRequiredService<IConversationService>();

    /// <summary>
    /// Regression guard for the FK-ordering bug: GetOrCreateDirectAsync inserts Conversation then
    /// ConversationMembers in one UoW. Under the deferred-save path (real UnitOfWorkManager), both
    /// must survive the commit in the correct FK order (members reference the conversation Id).
    /// </summary>
    [Fact]
    public async Task GetOrCreateDirect_UnitOfWork_Should_Persist_Conversation_And_Both_Members_With_Correct_FK()
    {
        var other = Guid.NewGuid();

        var result = await Service.GetOrCreateDirectAsync(other);

        result.Succeeded.ShouldBeTrue(result.Message);
        var convId = result.Data!.Id;

        // Conversation row must exist
        var conv = await DbContext.Set<Conversation>().FindAsync(convId);
        conv.ShouldNotBeNull();

        // Exactly 2 ConversationMember rows, both FK'd to the conversation
        var members = await DbContext.Set<ConversationMember>()
            .Where(m => m.ConversationId == convId)
            .ToListAsync();
        members.Count.ShouldBe(2);
        members.ShouldAllBe(m => m.ConversationId == convId);

        // One member for each participant
        members.Select(m => m.UserId).ShouldContain(CurrentUserId);
        members.Select(m => m.UserId).ShouldContain(other);
    }

    /// <summary>
    /// Regression guard for HasMore correctness: with 3 messages and limit=2, page 1 must return
    /// the 2 newest with HasMore=true, and page 2 (Before=id of 2nd newest) must return the oldest
    /// with HasMore=false. Verifies the cursor walks backwards and HasMore is accurate.
    /// </summary>
    [Fact]
    public async Task GetMessages_MultiPage_Cursor_Should_Walk_Back_With_Correct_HasMore()
    {
        var conv = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;

        // Insert 3 messages with distinct, strictly increasing SentAt for determinism
        var base_ = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var m1 = new ChatMessage { ConversationId = conv.Id, SenderId = CurrentUserId, SentAt = base_,             ContentType = MessageContentType.Text, Content = "m1" };
        var m2 = new ChatMessage { ConversationId = conv.Id, SenderId = CurrentUserId, SentAt = base_.AddSeconds(1), ContentType = MessageContentType.Text, Content = "m2" };
        var m3 = new ChatMessage { ConversationId = conv.Id, SenderId = CurrentUserId, SentAt = base_.AddSeconds(2), ContentType = MessageContentType.Text, Content = "m3" };
        DbContext.Set<ChatMessage>().AddRange(m1, m2, m3);
        await DbContext.SaveChangesAsync();

        // Page 1: no cursor, limit=2 → 2 newest (m2, m3 in ascending order), HasMore=true
        var page1 = await Service.GetMessagesAsync(conv.Id, new MessageThreadQueryDto { Limit = 2 });
        page1.Succeeded.ShouldBeTrue(page1.Message);
        page1.Data!.HasMore.ShouldBeTrue("there is still m1 behind page 1");
        page1.Data.Messages.Count.ShouldBe(2);
        page1.Data.Messages[0].Content.ShouldBe("m2");
        page1.Data.Messages[1].Content.ShouldBe("m3");

        // Page 2: Before = m2 (oldest message on page 1), limit=2 → only m1, HasMore=false
        var page2 = await Service.GetMessagesAsync(conv.Id, new MessageThreadQueryDto { Before = m2.Id, Limit = 2 });
        page2.Succeeded.ShouldBeTrue(page2.Message);
        page2.Data!.HasMore.ShouldBeFalse("m1 is the last message");
        page2.Data.Messages.Count.ShouldBe(1);
        page2.Data.Messages[0].Content.ShouldBe("m1");
    }

    [Fact]
    public async Task GetOrCreateDirect_Should_Be_Idempotent()
    {
        var other = Guid.NewGuid();
        var r1 = await Service.GetOrCreateDirectAsync(other);
        var r2 = await Service.GetOrCreateDirectAsync(other);

        r1.Succeeded.ShouldBeTrue(r1.Message);
        r2.Succeeded.ShouldBeTrue();
        r2.Data!.Id.ShouldBe(r1.Data!.Id);
        (await DbContext.Set<Conversation>().CountAsync(c => c.Type == ConversationType.Direct)).ShouldBe(1);
    }

    [Fact]
    public async Task SendMessage_Should_Persist_And_Bump_Other_Member_Unread()
    {
        var other = Guid.NewGuid();
        var conv = (await Service.GetOrCreateDirectAsync(other)).Data!;

        var sent = await Service.SendMessageAsync(conv.Id, new SendMessageDto { ContentType = MessageContentType.Text, Content = "hi there" });

        sent.Succeeded.ShouldBeTrue(sent.Message);
        (await DbContext.Set<ChatMessage>().CountAsync(m => m.ConversationId == conv.Id)).ShouldBe(1);

        var otherMember = await DbContext.Set<ConversationMember>()
            .FirstAsync(m => m.ConversationId == conv.Id && m.UserId == other);
        otherMember.UnreadCount.ShouldBe(1);

        var updated = await DbContext.Set<Conversation>().FindAsync(conv.Id);
        updated!.LastMessagePreview.ShouldBe("hi there");
        updated.LastMessageAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetMessages_Should_Return_Thread_Ascending()
    {
        var conv = (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!;
        await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "m1" });
        await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "m2" });

        var thread = await Service.GetMessagesAsync(conv.Id, new MessageThreadQueryDto { Limit = 30 });

        thread.Succeeded.ShouldBeTrue();
        thread.Data!.Messages.Count.ShouldBe(2);
        thread.Data.Messages[0].Content.ShouldBe("m1");
        thread.Data.Messages[1].Content.ShouldBe("m2");
    }

    [Fact]
    public async Task SendMessage_By_NonMember_Should_Fail_403()
    {
        var conv = new Conversation { Type = ConversationType.Direct, DirectKey = "x:y", MemberCount = 2 };
        DbContext.Set<Conversation>().Add(conv);
        await DbContext.SaveChangesAsync();

        var r = await Service.SendMessageAsync(conv.Id, new SendMessageDto { Content = "intrude" });
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }
}
