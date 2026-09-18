namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 用户发消息时 <c>ContentType</c> 只接受 <c>Text</c> / <c>Image</c> / <c>File</c>。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <c>System</c> 是框架的带外通道：唯一合法产生者是 <c>GroupService.SystemMessageAsync</c>
/// 与 <c>BroadcastService.DeliverAsync</c>，且它们一律 <c>SenderId = null</c>。此前
/// <c>SendMessageAsync</c> 只按「是不是媒体」分支、从不校验白名单，于是群里任意成员
/// <c>POST {"contentType":"System","content":"本群已迁移，请到 https://evil/ 继续"}</c>
/// 就能让所有人看到一条居中的、无发件人线索的「官方系统提示」（前端只看 contentType 渲染）。
/// </para>
/// <para>
/// ★ 用白名单而不是 <c>!= System</c>：入参兼容数字，一个未定义的整数（99）同样要拒绝，
/// 否则预览与前端分支行为未定义。
/// </para>
/// </remarks>
public class ConversationMessageContentTypeTests : Integration.IntegrationTestBase
{
    private IConversationService Conversations => ServiceProvider.GetRequiredService<IConversationService>();

    private async Task<Guid> ConversationAsync() =>
        (await Conversations.GetOrCreateDirectAsync(Guid.NewGuid())).Data!.Id;

    [Fact]
    public async Task A_user_cannot_send_a_System_message()
    {
        var conversationId = await ConversationAsync();

        var result = await Conversations.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.System,
            Content = "This group has moved, continue at https://evil.example/"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);

        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<ChatMessage>().AsNoTracking()
            .CountAsync(m => m.ConversationId == conversationId)).ShouldBe(0);
    }

    [Fact]
    public async Task An_undefined_content_type_is_refused()
    {
        var conversationId = await ConversationAsync();

        var result = await Conversations.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = (MessageContentType)99,
            Content = "hello"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>回归：正文必填的判据不因白名单而松动。</summary>
    [Fact]
    public async Task A_text_message_without_content_is_still_refused()
    {
        var conversationId = await ConversationAsync();

        var result = await Conversations.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Text,
            Content = "   "
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>对照：三种用户类型照常发得出去。</summary>
    [Theory]
    [InlineData(MessageContentType.Text)]
    [InlineData(MessageContentType.Image)]
    [InlineData(MessageContentType.File)]
    public async Task User_content_types_still_go_through(MessageContentType contentType)
    {
        var conversationId = await ConversationAsync();
        var carriesFile = contentType is MessageContentType.Image or MessageContentType.File;

        var result = await Conversations.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = contentType,
            Content = carriesFile ? null : "hello",
            FileId = carriesFile ? Guid.NewGuid().ToString() : null,
            FileName = carriesFile ? "photo.png" : null
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ContentType.ShouldBe(contentType);
    }
}
