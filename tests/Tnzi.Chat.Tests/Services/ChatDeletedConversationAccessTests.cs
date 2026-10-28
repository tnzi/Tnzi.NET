namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 会话被解散 / 被管理员删除之后，原成员不再读得到它的消息、搜索结果与文件，未读总数也不再算它。
/// </summary>
/// <remarks>
/// 解散与管理端删除都只软删会话行；成员行不实现软删，<c>RemovedAt</c> 仍为空。此前读路径只问
/// 「成员行在不在册」，于是管理员因违规删掉的群，成员凭原会话 id 照样读全部历史、下载其中文件，
/// 而 <c>GET conversations/unread-count</c> 永久多出一份任何界面都清不掉的未读。
/// </remarks>
public class ChatDeletedConversationAccessTests : Integration.IntegrationTestBase
{
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();
    private IGroupService Groups => ServiceProvider.GetRequiredService<IGroupService>();
    private IChatAdminService Admin => ServiceProvider.GetRequiredService<IChatAdminService>();

    private async Task<Guid> CreateDirectWithMessageAsync(Guid peer)
    {
        var conv = (await Conversations.GetOrCreateDirectAsync(peer)).Data!;
        (await Conversations.SendMessageAsync(conv.Id, new SendMessageDto { Content = "secret plan" })).Succeeded.ShouldBeTrue();
        return conv.Id;
    }

    [Fact]
    public async Task AdminDeletedConversation_HistoryAndSearch_AreNoLongerReadable()
    {
        var conversationId = await CreateDirectWithMessageAsync(Guid.NewGuid());
        (await Conversations.GetMessagesAsync(conversationId, new MessageThreadQueryDto())).Succeeded.ShouldBeTrue();

        (await Admin.DeleteConversationAsync(conversationId)).Succeeded.ShouldBeTrue();

        (await Conversations.GetMessagesAsync(conversationId, new MessageThreadQueryDto())).Succeeded.ShouldBeFalse();
        (await Conversations.SearchMessagesAsync(conversationId, "secret", new MessageThreadQueryDto())).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task DissolvedGroup_HistoryIsNoLongerReadable()
    {
        var group = (await Groups.CreateGroupAsync(new CreateGroupDto { Title = "Team", MemberIds = [Guid.NewGuid()] })).Data!;
        (await Conversations.SendMessageAsync(group.Id, new SendMessageDto { Content = "secret plan" })).Succeeded.ShouldBeTrue();

        (await Groups.DissolveGroupAsync(group.Id)).Succeeded.ShouldBeTrue();

        (await Conversations.GetMessagesAsync(group.Id, new MessageThreadQueryDto())).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task AdminDeletedConversation_NoLongerCountsTowardsTheUnreadTotal()
    {
        var kept = await CreateDirectWithMessageAsync(Guid.NewGuid());
        var deleted = await CreateDirectWithMessageAsync(Guid.NewGuid());

        // 本人在两个会话里各有未读。
        foreach (var row in await DbContext.Set<ConversationMember>().Where(m => m.UserId == CurrentUserId).ToListAsync())
        {
            row.UnreadCount = row.ConversationId == kept ? 2 : 5;
        }
        await DbContext.SaveChangesAsync();
        (await Conversations.GetTotalUnreadAsync()).Data.ShouldBe(7);

        (await Admin.DeleteConversationAsync(deleted)).Succeeded.ShouldBeTrue();

        (await Conversations.GetTotalUnreadAsync()).Data.ShouldBe(2);
    }

    [Fact]
    public async Task AdminDeletedConversation_FilesInItAreNoLongerReadable()
    {
        var peer = Guid.NewGuid();
        var conv = (await Conversations.GetOrCreateDirectAsync(peer)).Data!;
        var fileId = Guid.NewGuid();
        var message = (await Conversations.SendMessageAsync(conv.Id, new SendMessageDto
        {
            ContentType = MessageContentType.Image,
            FileId = fileId.ToString(),
            FileName = "photo.jpg",
            FileSize = 1024
        })).Data!;

        var peerUser = new Mock<ICurrentUser>();
        peerUser.SetupGet(u => u.Id).Returns(peer);
        peerUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        var resolver = new ChatFileReferenceAccessResolver(
            peerUser.Object,
            ServiceProvider.GetRequiredService<IRepository<ChatMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<ConversationMember, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<Conversation, Guid>>());
        var reference = new FileReferenceDescriptor(fileId, nameof(ChatMessage), message.Id, nameof(ChatMessage.FileId));
        (await resolver.CanReadAsync(reference)).ShouldBeTrue();

        (await Admin.DeleteConversationAsync(conv.Id)).Succeeded.ShouldBeTrue();

        (await resolver.CanReadAsync(reference)).ShouldBeFalse();
    }
}
