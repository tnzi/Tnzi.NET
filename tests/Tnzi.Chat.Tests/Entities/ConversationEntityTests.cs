namespace Tnzi.Chat.Tests.Entities;

public class ConversationEntityTests : Integration.IntegrationTestBase
{
    [Fact]
    public async Task Should_Persist_Conversation_With_Members_And_Message()
    {
        var conv = new Conversation
        {
            Type = ConversationType.Direct,
            DirectKey = "a:b",
            MemberCount = 2,
            LastMessagePreview = "hi",
            LastMessageAt = DateTime.UtcNow
        };
        DbContext.Set<Conversation>().Add(conv);

        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        DbContext.Set<ConversationMember>().Add(new ConversationMember { ConversationId = conv.Id, UserId = u1, Role = MemberRole.Member });
        DbContext.Set<ConversationMember>().Add(new ConversationMember { ConversationId = conv.Id, UserId = u2, Role = MemberRole.Member });
        DbContext.Set<ChatMessage>().Add(new ChatMessage { ConversationId = conv.Id, SenderId = u1, ContentType = MessageContentType.Text, Content = "hi", SentAt = DateTime.UtcNow });

        await DbContext.SaveChangesAsync();

        var loaded = await DbContext.Set<Conversation>().FindAsync(conv.Id);
        loaded.ShouldNotBeNull();
        (await DbContext.Set<ConversationMember>().CountAsync(m => m.ConversationId == conv.Id)).ShouldBe(2);
        (await DbContext.Set<ChatMessage>().CountAsync(m => m.ConversationId == conv.Id)).ShouldBe(1);
    }
}
