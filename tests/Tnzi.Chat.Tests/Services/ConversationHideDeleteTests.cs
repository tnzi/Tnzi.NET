namespace Tnzi.Chat.Tests.Services;

/// <summary>隐藏会话（Hide）与按用户删除（Delete for me）语义。</summary>
public class ConversationHideDeleteTests : Integration.IntegrationTestBase
{
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    private async Task<Guid> CreateDirectWithMessageAsync(Guid other)
    {
        var conv = (await Conversations.GetOrCreateDirectAsync(other)).Data!;
        (await Conversations.SendMessageAsync(conv.Id, new SendMessageDto { Content = "hello" })).Succeeded.ShouldBeTrue();
        return conv.Id;
    }

    [Fact]
    public async Task Hide_Should_Remove_From_List_And_Unread_Badge()
    {
        var convId = await CreateDirectWithMessageAsync(Guid.NewGuid());

        (await Conversations.UpdateMemberSettingsAsync(convId, new ConversationMemberSettingsDto { IsHidden = true }))
            .Succeeded.ShouldBeTrue();

        var list = (await Conversations.GetMyConversationsAsync()).Data!;
        list.ShouldNotContain(i => i.Id == convId);
    }

    [Fact]
    public async Task Incoming_Message_Should_Unhide_For_Recipient()
    {
        var other = Guid.NewGuid();
        var convId = await CreateDirectWithMessageAsync(other);

        // The OTHER member hides the conversation; then the current user sends a
        // new message - the other member's row must flip back to visible.
        var otherRow = await DbContext.Set<ConversationMember>()
            .FirstAsync(m => m.ConversationId == convId && m.UserId == other);
        otherRow.IsHidden = true;
        await DbContext.SaveChangesAsync();

        (await Conversations.SendMessageAsync(convId, new SendMessageDto { Content = "ping" })).Succeeded.ShouldBeTrue();

        var refreshed = await DbContext.Set<ConversationMember>().AsNoTracking()
            .FirstAsync(m => m.ConversationId == convId && m.UserId == other);
        refreshed.IsHidden.ShouldBeFalse();
        refreshed.UnreadCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task DeleteForMe_Should_Hide_And_Wipe_My_History()
    {
        var convId = await CreateDirectWithMessageAsync(Guid.NewGuid());

        (await Conversations.DeleteForMeAsync(convId)).Succeeded.ShouldBeTrue();

        // Gone from my list...
        (await Conversations.GetMyConversationsAsync()).Data!.ShouldNotContain(i => i.Id == convId);

        // ...and my history view is empty (ClearedAt watermark), unread reset.
        var thread = (await Conversations.GetMessagesAsync(convId, new MessageThreadQueryDto())).Data!;
        thread.Messages.ShouldBeEmpty();

        var mine = await DbContext.Set<ConversationMember>().AsNoTracking()
            .FirstAsync(m => m.ConversationId == convId && m.UserId == CurrentUserId);
        mine.IsHidden.ShouldBeTrue();
        mine.UnreadCount.ShouldBe(0);
        mine.ClearedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DeleteForMe_Should_Not_Affect_Other_Members()
    {
        var other = Guid.NewGuid();
        var convId = await CreateDirectWithMessageAsync(other);

        (await Conversations.DeleteForMeAsync(convId)).Succeeded.ShouldBeTrue();

        var otherRow = await DbContext.Set<ConversationMember>().AsNoTracking()
            .FirstAsync(m => m.ConversationId == convId && m.UserId == other);
        otherRow.IsHidden.ShouldBeFalse();
        otherRow.ClearedAt.ShouldBeNull();

        // Messages themselves are untouched (shared data, never hard-deleted).
        (await DbContext.Set<ChatMessage>().CountAsync(m => m.ConversationId == convId)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task DeleteForMe_NonMember_Should_Fail_403()
    {
        var r = await Conversations.DeleteForMeAsync(Guid.NewGuid());
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }

    [Fact]
    public async Task Sending_Into_My_Hidden_Conversation_Should_Unhide_For_Me()
    {
        var convId = await CreateDirectWithMessageAsync(Guid.NewGuid());
        (await Conversations.UpdateMemberSettingsAsync(convId, new ConversationMemberSettingsDto { IsHidden = true }))
            .Succeeded.ShouldBeTrue();

        (await Conversations.SendMessageAsync(convId, new SendMessageDto { Content = "back" })).Succeeded.ShouldBeTrue();

        (await Conversations.GetMyConversationsAsync()).Data!.ShouldContain(i => i.Id == convId);
    }

    [Fact]
    public async Task GetOrCreateDirect_Should_Unhide_Existing_Hidden_Conversation()
    {
        var other = Guid.NewGuid();
        var convId = await CreateDirectWithMessageAsync(other);
        (await Conversations.DeleteForMeAsync(convId)).Succeeded.ShouldBeTrue();

        // Starting a chat with the same peer reuses the DirectKey conversation
        // and must bring it back into my list (history stays wiped by ClearedAt).
        var conv = (await Conversations.GetOrCreateDirectAsync(other)).Data!;
        conv.Id.ShouldBe(convId);
        (await Conversations.GetMyConversationsAsync()).Data!.ShouldContain(i => i.Id == convId);
        (await Conversations.GetMessagesAsync(convId, new MessageThreadQueryDto())).Data!.Messages.ShouldBeEmpty();
    }
}
