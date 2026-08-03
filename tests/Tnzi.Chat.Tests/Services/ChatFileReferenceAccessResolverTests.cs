namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 会话成员读得到会话里的文件。
///
/// 守的是这条不对称:发送方因为是文件创建者而天然放行,接收方两样都不是 ——
/// 没有这条判据,同一张图在发的人那里能看、在收的人那里 404。
/// </summary>
public class ChatFileReferenceAccessResolverTests : Integration.IntegrationTestBase
{
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    private ChatFileReferenceAccessResolver CreateResolver(Guid asUser)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.Id).Returns(asUser);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(asUser != Guid.Empty);

        return new ChatFileReferenceAccessResolver(
            currentUser.Object,
            ServiceProvider.GetRequiredService<IRepository<ChatMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<ConversationMember, Guid>>());
    }

    private static FileReferenceDescriptor MessageReference(Guid messageId, Guid fileId)
        => new(fileId, nameof(ChatMessage), messageId, nameof(ChatMessage.FileId));

    private async Task<(Guid ConversationId, Guid MessageId, Guid FileId)> SendImageAsync(Guid peer)
    {
        var conv = (await Conversations.GetOrCreateDirectAsync(peer)).Data!;
        var fileId = Guid.NewGuid();
        var message = (await Conversations.SendMessageAsync(conv.Id, new SendMessageDto
        {
            ContentType = MessageContentType.Image,
            FileId = fileId.ToString(),
            FileName = "photo.jpg",
            FileSize = 1024
        })).Data!;

        return (conv.Id, message.Id, fileId);
    }

    [Fact]
    public void It_Only_Handles_Chat_Entities()
    {
        var resolver = CreateResolver(CurrentUserId);

        resolver.CanHandle(nameof(ChatMessage)).ShouldBeTrue();
        resolver.CanHandle(nameof(Conversation)).ShouldBeTrue();
        resolver.CanHandle("Invoice").ShouldBeFalse();
    }

    [Fact]
    public async Task The_Recipient_Can_Read_An_Image_Someone_Else_Sent()
    {
        var peer = Guid.NewGuid();
        var (_, messageId, fileId) = await SendImageAsync(peer);

        // 接收方既不是文件创建者也没有 storage.file.view —— 这条判据是他唯一的通道。
        var resolver = CreateResolver(peer);

        (await resolver.CanReadAsync(MessageReference(messageId, fileId))).ShouldBeTrue();
    }

    [Fact]
    public async Task An_Outsider_Cannot_Read_It()
    {
        var peer = Guid.NewGuid();
        var (_, messageId, fileId) = await SendImageAsync(peer);

        var resolver = CreateResolver(Guid.NewGuid());

        (await resolver.CanReadAsync(MessageReference(messageId, fileId))).ShouldBeFalse();
    }

    [Fact]
    public async Task Someone_Removed_From_The_Conversation_Loses_Access()
    {
        // 与消息历史保持一致:被移出之后就读不到了,而不是"曾经看得见就永远看得见"。
        var peer = Guid.NewGuid();
        var (conversationId, messageId, fileId) = await SendImageAsync(peer);

        var row = await DbContext.Set<ConversationMember>()
            .FirstAsync(m => m.ConversationId == conversationId && m.UserId == peer);
        row.RemovedAt = DateTime.UtcNow;
        await DbContext.SaveChangesAsync();

        var resolver = CreateResolver(peer);

        (await resolver.CanReadAsync(MessageReference(messageId, fileId))).ShouldBeFalse();
    }

    [Fact]
    public async Task An_Anonymous_Caller_Is_Never_Allowed()
    {
        var peer = Guid.NewGuid();
        var (_, messageId, fileId) = await SendImageAsync(peer);

        var resolver = CreateResolver(Guid.Empty);

        (await resolver.CanReadAsync(MessageReference(messageId, fileId))).ShouldBeFalse();
    }

    [Fact]
    public async Task A_Reference_To_A_Missing_Message_Is_Not_Allowed()
    {
        // 撤回的消息由全局软删过滤器挡掉,走的也是这条路径:读不到会话 id 即不放行。
        var resolver = CreateResolver(CurrentUserId);

        (await resolver.CanReadAsync(MessageReference(Guid.NewGuid(), Guid.NewGuid()))).ShouldBeFalse();
    }

    [Fact]
    public async Task A_Group_Avatar_Reference_Resolves_Through_The_Conversation_Itself()
    {
        var peer = Guid.NewGuid();
        var (conversationId, _, _) = await SendImageAsync(peer);
        var avatarFileId = Guid.NewGuid();
        var reference = new FileReferenceDescriptor(
            avatarFileId, nameof(Conversation), conversationId, nameof(Conversation.AvatarFileId));

        (await CreateResolver(peer).CanReadAsync(reference)).ShouldBeTrue();
        (await CreateResolver(Guid.NewGuid()).CanReadAsync(reference)).ShouldBeFalse();
    }
}
