using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// 发消息时引用的文件 id 必须是发送者本来就读得到的那些。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>补的是哪个洞。</b><c>SendMessageAsync</c> 把 <c>input.FileId</c> 原样写进实体，
/// 一条 <c>FileReference</c> 随之落库，而 <see cref="ChatFileReferenceAccessResolver"/>
/// 只要看到「你是这个会话的在册成员」就放行读取。于是：攻击者与任意用户建一个直聊，
/// 发一条 <c>FileId</c> 指向<b>受害者私密文件</b>的消息，然后就能下载它。
/// 被移出群的人也能在另一个会话里把同一个 id 重新引用回来。
/// </para>
/// <para>
/// ★ <c>EnableFileMessages = false</c> 拦不住它：那道开关只看内容类型，
/// 而 <c>FileId</c> 是<b>无条件</b>复制进实体的 —— 一条 <c>Text</c> 消息照样带得动它。
/// </para>
/// <para>
/// ★ <see cref="ChatFileReferenceAccessResolverTests"/> 那 7 条覆盖不到这件事：
/// 它们验的是<b>读取</b>侧（谁看得见会话里的文件），夹具用 <c>Guid.NewGuid()</c> 造 id，
/// 也就从未问过「这个 id 凭什么是你的」。
/// </para>
/// </remarks>
public class ChatFileAttachmentGuardTests : Integration.IntegrationTestBase
{
    private readonly Mock<IFileReadAccessProbe> _probe = new();
    private bool _readable = true;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        _probe.Setup(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _readable);

        services.RemoveAll<IFileReadAccessProbe>();
        services.AddScoped(_ => _probe.Object);
    }

    private IConversationService Service => ServiceProvider.GetRequiredService<IConversationService>();

    private async Task<Guid> ConversationAsync() =>
        (await Service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!.Id;

    // ── 越权引用 ─────────────────────────────────────────────────────────────

    /// <summary>★★★ 读不到的文件贴不进会话。</summary>
    [Fact]
    public async Task A_file_the_sender_cannot_read_is_refused()
    {
        _readable = false;
        var conversationId = await ConversationAsync();

        var result = await Service.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Image,
            FileId = Guid.NewGuid().ToString()
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    /// <summary>被拒的那条消息<b>一个字节都不落库</b>：落了就已经是一条可读的引用了。</summary>
    [Fact]
    public async Task A_refused_attachment_leaves_no_message_behind()
    {
        _readable = false;
        var conversationId = await ConversationAsync();

        await Service.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.File,
            FileId = Guid.NewGuid().ToString()
        });

        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<ChatMessage>().AsNoTracking()
            .CountAsync(m => m.ConversationId == conversationId)).ShouldBe(0);
    }

    /// <summary>
    /// ★★ 换个内容类型绕不过去：<c>Text</c> 消息不接受文件引用。
    /// 原实现对 <c>Text</c> 跳过整段媒体校验，却仍然无条件复制 <c>FileId</c>。
    /// </summary>
    [Fact]
    public async Task A_text_message_may_not_carry_a_file_reference()
    {
        var conversationId = await ConversationAsync();

        var result = await Service.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Text,
            Content = "look at this",
            FileId = Guid.NewGuid().ToString()
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>不是 Guid 的引用当场拒绝，而不是留一个 `[FileField]` 静默忽略的空操作。</summary>
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_file_reference_that_is_not_a_file_id_is_refused(string fileId)
    {
        var conversationId = await ConversationAsync();

        var result = await Service.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Image,
            FileId = fileId
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    // ── 正常路径不受影响 ─────────────────────────────────────────────────────

    /// <summary>
    /// 对照：读得到的文件照常贴得进去。少了这条，一个「一律拒绝附件」的实现也会全绿。
    /// </summary>
    [Fact]
    public async Task A_file_the_sender_can_read_is_accepted()
    {
        var conversationId = await ConversationAsync();
        var fileId = Guid.NewGuid();

        var result = await Service.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Image,
            FileId = fileId.ToString()
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.FileId.ShouldBe(fileId.ToString());
    }

    /// <summary>对照：不带附件的普通文本消息一次探针都不问。</summary>
    [Fact]
    public async Task A_plain_text_message_does_not_consult_the_probe()
    {
        var conversationId = await ConversationAsync();

        var result = await Service.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Text,
            Content = "hello"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        _probe.Verify(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

/// <summary>
/// 没有加载 <c>Tnzi.Storage</c> 时，带附件的消息<b>被拒绝</b>，不是跳过校验。
/// </summary>
/// <remarks>
/// ★★ 「跳过校验」与「校验通过」在接口上完全一致 —— 那正是这一整条缺陷的形态。
/// 501 而不是 503：这不是暂时性故障，重试永远不会好。
/// </remarks>
public class ChatFileAttachmentWithoutStorageTests : Integration.IntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.RemoveAll<IFileReadAccessProbe>();
    }

    [Fact]
    public async Task An_attachment_is_refused_when_the_storage_module_is_absent()
    {
        var service = ServiceProvider.GetRequiredService<IConversationService>();
        var conversationId = (await service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!.Id;

        var result = await service.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Image,
            FileId = Guid.NewGuid().ToString()
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message!.ShouldContain("Tnzi.Storage");
    }

    /// <summary>对照：不带附件的消息完全不受影响 —— 没有存储模块的应用照常聊天。</summary>
    [Fact]
    public async Task A_text_message_still_works_without_the_storage_module()
    {
        var service = ServiceProvider.GetRequiredService<IConversationService>();
        var conversationId = (await service.GetOrCreateDirectAsync(Guid.NewGuid())).Data!.Id;

        var result = await service.SendMessageAsync(conversationId, new SendMessageDto
        {
            ContentType = MessageContentType.Text,
            Content = "hello"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
    }
}
